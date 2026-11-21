using System.Text;
using System.Text.Json;
using Spar.Hosting;

namespace Spar.Windows.Testing;

/// <summary>
/// The page driver's transport on Windows: the WebView's own in-process DevTools Protocol session (decision "D8:
/// Devtools automation transport, in-process CDP"; no port, no second process). Before the first navigation it
/// installs the page engine in an isolated world of every document, then follows the documents and their worlds, hands
/// the page's console messages, errors, script dialogs and navigations to the driver, and runs each engine request in
/// the current document's world. The session is the host's own: the host's <c>Page.enable</c> and file-chooser
/// interception live in it, so nothing here ever calls <c>Page.disable</c> or turns the interception off.
/// <para>
/// Events arrive on the window's UI thread; their handlers only copy the JSON, and one reader task parses them in
/// order, so the UI thread does no parsing for the tests.
/// </para>
/// </summary>
internal sealed class TestPage : IDisposable
{
    private const string EngineCall = "function (request) { return globalThis.__sparAutomation.run(request); }";

    private static readonly string[] Events =
    [
        "Runtime.executionContextCreated", "Runtime.executionContextDestroyed", "Runtime.executionContextsCleared",
        "Runtime.consoleAPICalled", "Runtime.exceptionThrown", "Page.frameNavigated", "Page.navigatedWithinDocument",
        "Page.javascriptDialogOpening",
    ];

    private readonly Func<string, string, Task<string>> _call;
    private readonly SparTestAutomation _automation;
    private readonly System.Threading.Channels.Channel<(string Name, string Json)> _events =
        System.Threading.Channels.Channel.CreateUnbounded<(string, string)>(new() { SingleReader = true });
    private readonly List<IDisposable> _subscriptions = [];
    private readonly Lock _gate = new();
    private readonly Dictionary<int, (string FrameId, string Name, bool IsDefault)> _contexts = [];
    private TaskCompletionSource _engineReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string _mainFrame = "";
    private int _engineContext;
    private Task _reader = Task.CompletedTask;

    private TestPage(Func<string, string, Task<string>> call, SparTestAutomation automation)
    {
        _call = call;
        _automation = automation;
    }

    /// <summary>
    /// Runs before the window's first navigation, on its UI thread: the events, <c>Runtime.enable</c>, the main frame's
    /// id, and the engine as a script of every new document's isolated world.
    /// </summary>
    public static async Task<TestPage> InstallAsync(
        Func<string, string, Task<string>> call, Func<string, Action<string>, Task<IDisposable>> subscribe, SparTestAutomation automation)
    {
        var page = new TestPage(call, automation);
        foreach (var name in Events)
        {
            page._subscriptions.Add(await subscribe(name, json => page._events.Writer.TryWrite((name, json))));
        }

        page._reader = Task.Run(page.ReadAsync);

        // Page.enable is the host's already, unless the app allowed the page's own pickers; it is never undone.
        await call("Page.enable", "{}");
        await call("Runtime.enable", "{}");
        using (var tree = JsonDocument.Parse(await call("Page.getFrameTree", "{}")))
        {
            page._mainFrame = tree.RootElement.GetProperty("frameTree").GetProperty("frame").GetProperty("id").GetString() ?? "";
        }

        await call("Page.addScriptToEvaluateOnNewDocument", Json(writer =>
        {
            writer.WriteString("source", automation.EngineScript);
            writer.WriteString("worldName", automation.World);
        }));
        return page;
    }

    public async Task<string?> EngineAsync(string request, TimeSpan wait)
    {
        var context = await EngineContextAsync(wait);
        if (context == 0)
        {
            return null;
        }

        try
        {
            using var result = await CallAsync("Runtime.callFunctionOn", writer =>
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
            ThrowOnException(result.RootElement);
            return Value(result.RootElement) ?? "null";
        }
        catch (SparTestCallException)
        {
            // The document or its world went between the request and the answer.
            Forget(context);
            return null;
        }
    }

    public async Task<string?> EvaluateAsync(string expression)
    {
        using var result = await EvaluateOnceAsync(expression);
        if (Text(result.RootElement.GetProperty("result"), "type") != "function")
        {
            return Value(result.RootElement);
        }

        using var called = await EvaluateOnceAsync("(" + expression + ")()");
        return Value(called.RootElement);
    }

    public async Task NavigateAsync(string? address)
    {
        using var _ = await CallAsync(address is null ? "Page.reload" : "Page.navigate", writer =>
        {
            if (address is not null)
            {
                writer.WriteString("url", address);
            }
        });
    }

    public async Task<byte[]> ScreenshotAsync((double X, double Y, double Width, double Height)? clip)
    {
        using var result = await CallAsync("Page.captureScreenshot", writer =>
        {
            writer.WriteString("format", "png");
            if (clip is { } area)
            {
                writer.WriteStartObject("clip");
                writer.WriteNumber("x", area.X);
                writer.WriteNumber("y", area.Y);
                writer.WriteNumber("width", area.Width);
                writer.WriteNumber("height", area.Height);
                writer.WriteNumber("scale", 1);
                writer.WriteEndObject();
            }
        });
        return Convert.FromBase64String(Text(result.RootElement, "data") ?? "");
    }

    public async Task KeyAsync(SparTestKey key)
    {
        // A combination with Control, Alt or Meta types nothing: the page sees the key and acts on the shortcut.
        using var _ = await CallAsync("Input.dispatchKeyEvent", writer =>
        {
            writer.WriteString("type", !key.Down ? "keyUp" : key.Text is null ? "rawKeyDown" : "keyDown");
            writer.WriteNumber("modifiers", key.Modifiers);
            writer.WriteString("key", key.Key);
            writer.WriteString("code", key.Code);
            writer.WriteNumber("windowsVirtualKeyCode", key.VirtualKey);
            if (key.Down && key.Text is { } text)
            {
                writer.WriteString("text", text);
                writer.WriteString("unmodifiedText", text);
            }
        });
    }

    public async Task InsertTextAsync(string text)
    {
        using var _ = await CallAsync("Input.insertText", writer => writer.WriteString("text", text));
    }

    public async Task PointerAsync(SparTestPointer pointer)
    {
        using var _ = await CallAsync("Input.dispatchMouseEvent", writer =>
        {
            writer.WriteString("type", pointer.Kind switch
            {
                SparTestPointerKind.Press => "mousePressed",
                SparTestPointerKind.Release => "mouseReleased",
                SparTestPointerKind.Wheel => "mouseWheel",
                _ => "mouseMoved",
            });
            writer.WriteNumber("x", pointer.X);
            writer.WriteNumber("y", pointer.Y);
            if (pointer.Kind == SparTestPointerKind.Wheel)
            {
                writer.WriteNumber("deltaX", pointer.DeltaX);
                writer.WriteNumber("deltaY", pointer.DeltaY);
            }
            else if (pointer.ClickCount > 0)
            {
                writer.WriteString("button", "left");
                writer.WriteNumber("buttons", pointer.Kind == SparTestPointerKind.Press ? 1 : 0);
                writer.WriteNumber("clickCount", pointer.ClickCount);
            }
        });
    }

    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _events.Writer.TryComplete();
    }

    /// <summary>A DevTools call, its result parsed; a refusal that came back as a result is a refusal.</summary>
    private async Task<JsonDocument> CallAsync(string method, Action<Utf8JsonWriter> parameters)
    {
        string json;
        try
        {
            json = await _call(method, Json(parameters));
        }
        catch (DevToolsException exception)
        {
            throw IsContextGone(exception) ? new SparTestCallException(method, exception.Error ?? exception.Message) : exception;
        }

        var result = JsonDocument.Parse(json);
        var root = result.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("code", out _) && root.TryGetProperty("message", out _)
            && !root.TryGetProperty("result", out _))
        {
            result.Dispose();
            throw new SparTestCallException(method, json);
        }

        return result;
    }

    private async Task<JsonDocument> EvaluateOnceAsync(string expression)
    {
        var result = await CallAsync("Runtime.evaluate", writer =>
        {
            writer.WriteString("expression", expression);
            writer.WriteBoolean("returnByValue", true);
            writer.WriteBoolean("awaitPromise", true);
        });
        try
        {
            ThrowOnException(result.RootElement);
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    private static string Json(Action<Utf8JsonWriter> write)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            write(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;

    /// <summary>The call's value by value, as JSON; null for <c>undefined</c>.</summary>
    private static string? Value(JsonElement result) =>
        result.GetProperty("result").TryGetProperty("value", out var value) ? value.GetRawText() : null;

    private static void ThrowOnException(JsonElement result)
    {
        if (!result.TryGetProperty("exceptionDetails", out var details))
        {
            return;
        }

        var text = details.TryGetProperty("exception", out var exception) ? Text(exception, "description") : null;
        throw new SparTestScriptException(text ?? Text(details, "text") ?? "an exception");
    }

    private static bool IsContextGone(DevToolsException exception) =>
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
                using var data = JsonDocument.Parse(json);
                OnEvent(name, data.RootElement);
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
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
                Forget(Number(data, "executionContextId"));
                break;
            case "Runtime.executionContextsCleared":
                ClearContexts();
                break;
            case "Runtime.consoleAPICalled":
                OnConsole(data);
                break;
            case "Runtime.exceptionThrown":
                var details = data.GetProperty("exceptionDetails");
                var text = details.TryGetProperty("exception", out var exception) ? Text(exception, "description") : null;
                _automation.Events.PageError(text ?? Text(details, "text") ?? "an exception");
                break;
            case "Page.frameNavigated":
                OnNavigated(data.GetProperty("frame"));
                break;
            case "Page.navigatedWithinDocument":
                if (Text(data, "frameId") == MainFrame())
                {
                    _automation.Events.Navigated(Text(data, "url") ?? "", newDocument: false);
                }

                break;
            case "Page.javascriptDialogOpening":
                var accept = _automation.Events.Dialog(Text(data, "type") ?? "alert", Text(data, "message") ?? "");

                // A dialog blocks the page until it is answered; nobody else may answer it in a test window.
                _ = AnswerAsync(accept);
                break;
        }
    }

    private string MainFrame()
    {
        lock (_gate)
        {
            return _mainFrame;
        }
    }

    private void OnContextCreated(JsonElement context)
    {
        var id = Number(context, "id");
        var contextName = Text(context, "name") ?? "";
        var aux = context.TryGetProperty("auxData", out var data) ? data : default;
        var frame = aux.ValueKind == JsonValueKind.Object ? Text(aux, "frameId") ?? "" : "";
        var isDefault = aux.ValueKind == JsonValueKind.Object && aux.TryGetProperty("isDefault", out var flag) && flag.ValueKind == JsonValueKind.True;
        lock (_gate)
        {
            _contexts[id] = (frame, contextName, isDefault);
            if (frame == _mainFrame && !isDefault && contextName == _automation.World)
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
            _mainFrame = Text(frame, "id") ?? _mainFrame;
        }

        _automation.Events.Navigated(Text(frame, "url") ?? "", newDocument: true);
    }

    private void OnConsole(JsonElement data)
    {
        var context = Number(data, "executionContextId");
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

        _automation.Events.Console(Text(data, "type") ?? "log", string.Join(" ", parts));
    }

    private static string Describe(JsonElement remote)
    {
        if (remote.TryGetProperty("value", out var value))
        {
            return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
        }

        return Text(remote, "description") ?? Text(remote, "type") ?? "";
    }

    private async Task AnswerAsync(bool accept)
    {
        try
        {
            using var _ = await CallAsync("Page.handleJavaScriptDialog", writer => writer.WriteBoolean("accept", accept));
        }
        catch (Exception exception) when (exception is DevToolsException or SparTestCallException)
        {
            // The dialog went with its document.
        }
    }
}
