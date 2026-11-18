using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Spar.Hosting;

namespace Spar.Testing;

/// <summary>
/// The page driver of a window test on Windows: the WebView's own in-process DevTools Protocol session (decision "D8:
/// Devtools automation transport, in-process CDP"; no port, no second process). It installs the page engine
/// (<c>PageEngine.js</c>) in an isolated world of every document before the first navigation, follows the documents
/// and their worlds, records console messages, page errors and script dialogs, and runs each engine request in the
/// current document's world. The session is the host's own: the host's <c>Page.enable</c> and file-chooser
/// interception live in it, so nothing here ever calls <c>Page.disable</c> or turns the interception off.
/// <para>
/// Events arrive on the window's UI thread; their handlers only queue the JSON, and one reader task parses them in
/// order, so the UI thread does no parsing for the tests.
/// </para>
/// </summary>
internal sealed class PageSession : IAsyncDisposable
{
    /// <summary>The isolated world the engine runs in: the page's scripts can neither see it nor change what it uses.</summary>
    public const string WorldName = "spar-automation";

    private const string EngineCall = "function (request) { return globalThis.__sparAutomation.run(request); }";

    private static readonly string[] Events =
    [
        "Runtime.executionContextCreated", "Runtime.executionContextDestroyed", "Runtime.executionContextsCleared",
        "Runtime.consoleAPICalled", "Runtime.exceptionThrown", "Page.frameNavigated", "Page.navigatedWithinDocument",
        "Page.javascriptDialogOpening",
    ];

    private readonly ISparTestWindow _window;
    private readonly Channel<(string Name, string Json)> _events =
        Channel.CreateUnbounded<(string, string)>(new UnboundedChannelOptions { SingleReader = true });
    private readonly List<IDisposable> _subscriptions = [];
    private readonly Lock _gate = new();
    private readonly List<SparConsoleMessage> _console = [];
    private readonly List<string> _errors = [];
    private readonly List<SparDialog> _dialogs = [];
    private readonly Dictionary<int, (string FrameId, string Name, bool IsDefault)> _contexts = [];
    private Task _reader = Task.CompletedTask;
    private TaskCompletionSource _engineReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string _mainFrame = "";
    private string _url = "";
    private int _engineContext;
    private int _navigations;
    private int _mainContext;
    private bool _framesPending = true;

    private PageSession(ISparTestWindow window)
    {
        _window = window;
    }

    /// <summary>The document's address, from the navigation events.</summary>
    public string Url
    {
        get
        {
            lock (_gate)
            {
                return _url;
            }
        }
    }

    /// <summary>How many documents the main frame has loaded since the session started (a new one for each navigation).</summary>
    public int Navigations
    {
        get
        {
            lock (_gate)
            {
                return _navigations;
            }
        }
    }

    /// <summary>The window's lifetime and DevTools session.</summary>
    public ISparTestWindow Window => _window;

    /// <summary>Whether the page's script dialogs are accepted; dismissed by default, as the browser automation library does.</summary>
    public bool AcceptDialogs { get; set; }

    public IReadOnlyList<SparConsoleMessage> ConsoleMessages
    {
        get
        {
            lock (_gate)
            {
                return [.. _console];
            }
        }
    }

    public IReadOnlyList<string> PageErrors
    {
        get
        {
            lock (_gate)
            {
                return [.. _errors];
            }
        }
    }

    public IReadOnlyList<SparDialog> Dialogs
    {
        get
        {
            lock (_gate)
            {
                return [.. _dialogs];
            }
        }
    }

    /// <summary>The stopwatch-based remaining time of a wait, never negative.</summary>
    public static TimeSpan Remaining(long started, TimeSpan timeout)
    {
        var left = timeout - Stopwatch.GetElapsedTime(started);
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    /// <summary>
    /// Runs before the window's first navigation, on its UI thread: the events, <c>Runtime.enable</c>, the main frame's
    /// id, and the engine as a script of every new document's isolated world.
    /// </summary>
    public static async Task<PageSession> InstallAsync(ISparTestWindow window)
    {
        var session = new PageSession(window);
        foreach (var name in Events)
        {
            session._subscriptions.Add(await window.SubscribeAsync(name, json => session._events.Writer.TryWrite((name, json))));
        }

        session._reader = Task.Run(session.ReadAsync);

        // Page.enable is the host's already, unless the app allowed the page's own pickers; it is never undone.
        await window.CallDevToolsAsync("Page.enable", "{}");
        await window.CallDevToolsAsync("Runtime.enable", "{}");
        var tree = PageJson.Parse(await window.CallDevToolsAsync("Page.getFrameTree", "{}"));
        session._mainFrame = tree.GetProperty("frameTree").GetProperty("frame").GetProperty("id").GetString() ?? "";
        var install = PageJson.Object(writer =>
        {
            writer.WriteString("source", PageEngineSource.Text);
            writer.WriteString("worldName", WorldName);
        });
        await window.CallDevToolsAsync("Page.addScriptToEvaluateOnNewDocument", install);
        return session;
    }

    /// <summary>Calls a DevTools Protocol method and returns its result object.</summary>
    public async Task<JsonElement> CallAsync(string method, Action<Utf8JsonWriter>? parameters = null)
    {
        var json = await _window.CallDevToolsAsync(method, parameters is null ? "{}" : PageJson.Object(parameters));
        var result = PageJson.Parse(json);

        // A refused call that came back as a result rather than a failure: the protocol's error object.
        if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("code", out _) && result.TryGetProperty("message", out _)
            && !result.TryGetProperty("result", out _))
        {
            throw new SparDevToolsException(method, 0, json);
        }

        return result;
    }

    /// <summary>
    /// One engine request in the current document's isolated world. Null when no document is ready to answer (a
    /// navigation in progress, or a world just destroyed): the caller tries again.
    /// </summary>
    public async Task<JsonElement?> EngineAsync(string op, Action<Utf8JsonWriter> fields, TimeSpan wait)
    {
        var context = await EngineContextAsync(wait);
        if (context == 0)
        {
            return null;
        }

        var request = PageJson.Object(writer =>
        {
            writer.WriteString("op", op);
            fields(writer);
        });
        try
        {
            var result = await CallAsync("Runtime.callFunctionOn", writer =>
            {
                writer.WriteString("functionDeclaration", EngineCall);
                writer.WriteNumber("executionContextId", context);
                writer.WriteStartArray("arguments");
                writer.WriteStartObject();
                writer.WriteString("value", request);
                writer.WriteEndObject();
                writer.WriteEndArray();
                writer.WriteBoolean("returnByValue", true);
                writer.WriteBoolean("awaitPromise", true);
            });
            ThrowOnException(result, "the page engine");
            return result.GetProperty("result").TryGetProperty("value", out var value) ? value : default;
        }
        catch (SparDevToolsException exception) when (IsContextGone(exception))
        {
            Forget(context);
            return null;
        }
    }

    /// <summary>
    /// Runs <paramref name="expression"/> in the page's own world, awaiting a promise, and returns its value as JSON. A
    /// function (<c>"() => document.title"</c>) is called, as the browser automation library does.
    /// </summary>
    public async Task<JsonElement> EvaluateAsync(string expression)
    {
        var result = await CallAsync("Runtime.evaluate", writer =>
        {
            writer.WriteString("expression", expression);
            writer.WriteBoolean("returnByValue", true);
            writer.WriteBoolean("awaitPromise", true);
        });
        ThrowOnException(result, "the page");
        var remote = result.GetProperty("result");
        if (PageJson.String(remote, "type") != "function")
        {
            return remote.TryGetProperty("value", out var value) ? value : default;
        }

        var called = await CallAsync("Runtime.evaluate", writer =>
        {
            writer.WriteString("expression", "(" + expression + ")()");
            writer.WriteBoolean("returnByValue", true);
            writer.WriteBoolean("awaitPromise", true);
        });
        ThrowOnException(called, "the page");
        return called.GetProperty("result").TryGetProperty("value", out var returned) ? returned : default;
    }

    /// <summary>
    /// Before trusted input: two animation frames after a navigation, so the first input does not stall (pitfall "The
    /// first trusted input after navigation can stall for 5 s").
    /// </summary>
    public async Task BeforeInputAsync(TimeSpan wait)
    {
        bool pending;
        lock (_gate)
        {
            pending = _framesPending;
        }

        if (!pending)
        {
            return;
        }

        if (await EngineAsync("frames", _ => { }, wait) is not null)
        {
            lock (_gate)
            {
                _framesPending = false;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _events.Writer.TryComplete();
        await _reader;
    }

    private static void ThrowOnException(JsonElement result, string where)
    {
        if (!result.TryGetProperty("exceptionDetails", out var details))
        {
            return;
        }

        var text = details.TryGetProperty("exception", out var exception) ? PageJson.String(exception, "description") : null;
        throw new SparPageException($"Script in {where} threw: {text ?? PageJson.String(details, "text") ?? "an exception"}");
    }

    private static bool IsContextGone(SparDevToolsException exception) =>
        exception.Error is { } error
        && (error.Contains("context", StringComparison.OrdinalIgnoreCase) || error.Contains("Inspected target navigated", StringComparison.Ordinal));

    private async Task<int> EngineContextAsync(TimeSpan wait)
    {
        Task ready;
        lock (_gate)
        {
            if (_engineContext != 0)
            {
                return _engineContext;
            }

            ready = _engineReady.Task;
        }

        if (await Task.WhenAny(ready, Task.Delay(wait)) != ready)
        {
            return 0;
        }

        lock (_gate)
        {
            return _engineContext;
        }
    }

    private void Forget(int context)
    {
        lock (_gate)
        {
            _contexts.Remove(context);
            if (_engineContext == context)
            {
                _engineContext = 0;
                _engineReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
    }

    private async Task ReadAsync()
    {
        await foreach (var (name, json) in _events.Reader.ReadAllAsync())
        {
            try
            {
                OnEvent(name, PageJson.Parse(json));
            }
            catch (JsonException)
            {
                // A malformed event is not the test's concern; the next one is read.
            }
        }
    }

    private void OnEvent(string name, JsonElement data)
    {
        switch (name)
        {
            case "Runtime.executionContextCreated":
                OnContextCreated(data.GetProperty("context"));
                break;
            case "Runtime.executionContextDestroyed":
                Forget(PageJson.Int(data, "executionContextId"));
                break;
            case "Runtime.executionContextsCleared":
                ClearContexts();
                break;
            case "Runtime.consoleAPICalled":
                OnConsole(data);
                break;
            case "Runtime.exceptionThrown":
                OnException(data);
                break;
            case "Page.frameNavigated":
                OnNavigated(data.GetProperty("frame"));
                break;
            case "Page.navigatedWithinDocument":
                OnNavigatedWithinDocument(data);
                break;
            case "Page.javascriptDialogOpening":
                OnDialog(data);
                break;
        }
    }

    private void OnContextCreated(JsonElement context)
    {
        var id = PageJson.Int(context, "id");
        var contextName = PageJson.String(context, "name") ?? "";
        var aux = context.TryGetProperty("auxData", out var data) ? data : default;
        var frame = aux.ValueKind == JsonValueKind.Object ? PageJson.String(aux, "frameId") ?? "" : "";
        var isDefault = aux.ValueKind == JsonValueKind.Object && PageJson.Bool(aux, "isDefault");
        lock (_gate)
        {
            _contexts[id] = (frame, contextName, isDefault);
            if (frame != _mainFrame)
            {
                return;
            }

            if (isDefault)
            {
                _mainContext = id;
            }
            else if (contextName == WorldName)
            {
                _engineContext = id;
                _engineReady.TrySetResult();
            }
        }
    }

    private void ClearContexts()
    {
        lock (_gate)
        {
            _contexts.Clear();
            _mainContext = 0;
            if (_engineContext != 0)
            {
                _engineContext = 0;
                _engineReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
    }

    private void OnNavigated(JsonElement frame)
    {
        if (frame.TryGetProperty("parentId", out _))
        {
            return;
        }

        lock (_gate)
        {
            _mainFrame = PageJson.String(frame, "id") ?? _mainFrame;
            _url = PageJson.String(frame, "url") ?? _url;
            _framesPending = true;
            _navigations++;
        }
    }

    private void OnNavigatedWithinDocument(JsonElement data)
    {
        lock (_gate)
        {
            if (PageJson.String(data, "frameId") == _mainFrame)
            {
                _url = PageJson.String(data, "url") ?? _url;
            }
        }
    }

    private void OnConsole(JsonElement data)
    {
        var context = PageJson.Int(data, "executionContextId");
        lock (_gate)
        {
            // The engine's own world logs nothing; anything from another world is not the page's.
            if (_contexts.TryGetValue(context, out var known) && !known.IsDefault)
            {
                return;
            }
        }

        var parts = new List<string>();
        if (data.TryGetProperty("args", out var arguments))
        {
            foreach (var argument in arguments.EnumerateArray())
            {
                parts.Add(Describe(argument));
            }
        }

        var message = new SparConsoleMessage(PageJson.String(data, "type") ?? "log", string.Join(" ", parts));
        lock (_gate)
        {
            _console.Add(message);
        }
    }

    private static string Describe(JsonElement remote)
    {
        if (remote.TryGetProperty("value", out var value))
        {
            return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
        }

        return PageJson.String(remote, "description") ?? PageJson.String(remote, "type") ?? "";
    }

    private void OnException(JsonElement data)
    {
        var details = data.GetProperty("exceptionDetails");
        var text = details.TryGetProperty("exception", out var exception)
            ? PageJson.String(exception, "description")
            : null;
        lock (_gate)
        {
            _errors.Add(text ?? PageJson.String(details, "text") ?? "an exception");
        }
    }

    private void OnDialog(JsonElement data)
    {
        var dialog = new SparDialog(PageJson.String(data, "type") ?? "alert", PageJson.String(data, "message") ?? "");
        bool accept;
        lock (_gate)
        {
            _dialogs.Add(dialog);
            accept = AcceptDialogs || dialog.Type == "beforeunload";
        }

        // A dialog blocks the page until it is answered; nobody else may answer it in a test window.
        _ = AnswerAsync(accept);
    }

    private async Task AnswerAsync(bool accept)
    {
        try
        {
            await CallAsync("Page.handleJavaScriptDialog", writer => writer.WriteBoolean("accept", accept));
        }
        catch (SparDevToolsException)
        {
            // The dialog went with its document.
        }
    }
}

/// <summary>A message the page wrote to its console: its type (<c>log</c>, <c>error</c>, <c>warning</c>…) and text.</summary>
public sealed record SparConsoleMessage(string Type, string Text);

/// <summary>A script dialog the page opened (<c>alert</c>, <c>confirm</c>, <c>prompt</c>, <c>beforeunload</c>) and its message.</summary>
public sealed record SparDialog(string Type, string Message);
