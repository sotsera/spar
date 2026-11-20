using System.Diagnostics;
using System.Text.Json;
using Spar.Hosting;

namespace Spar.Testing;

/// <summary>
/// The page driver of a window test, the same on both hosts: it gives the window the page engine
/// (<c>PageEngine.js</c>) to run in an isolated world of every document, records what the page reports (its documents,
/// console messages, errors and script dialogs, which it answers), and sends each engine request, script and input
/// through the window (<see cref="ISparTestWindow"/>), whose host keeps its own mechanism underneath: the WebView's
/// in-process DevTools Protocol session on Windows, WebKitGTK's API and XTEST on Linux.
/// </summary>
internal sealed class PageSession : ISparTestPageEvents
{
    /// <summary>The isolated world the engine runs in: the page's scripts can neither see it nor change what it uses.</summary>
    public const string WorldName = "spar-automation";

    private readonly Lock _gate = new();
    private readonly List<SparConsoleMessage> _console = [];
    private readonly List<string> _errors = [];
    private readonly List<SparDialog> _dialogs = [];
    private ISparTestWindow? _window;
    private string _url = "";
    private int _navigations;
    private bool _framesPending = true;

    public PageSession()
    {
        Automation = new SparTestAutomation(PageEngineSource.Text, WorldName, this);
    }

    /// <summary>What the window installs before its first navigation: the engine, its world, and this session's events.</summary>
    public SparTestAutomation Automation { get; }

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

    /// <summary>The window's lifetime, transport and input.</summary>
    public ISparTestWindow Window => _window ?? throw new InvalidOperationException("The app's page is not open: its window is starting.");

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

    /// <summary>The window the host opened for this session.</summary>
    public void Attach(ISparTestWindow window) => _window = window;

    /// <summary>
    /// One engine request in the current document's isolated world. Null when no document is ready to answer (a
    /// navigation in progress, or a world just destroyed): the caller tries again.
    /// </summary>
    public async Task<JsonElement?> EngineAsync(string op, Action<Utf8JsonWriter> fields, TimeSpan wait)
    {
        var request = PageJson.Object(writer =>
        {
            writer.WriteString("op", op);
            fields(writer);
        });
        string? answer;
        try
        {
            answer = await Window.EngineAsync(request, wait);
        }
        catch (SparTestScriptException exception)
        {
            throw new SparPageException("Script in the page engine threw: " + exception.Message);
        }

        return answer is null ? null : PageJson.Parse(answer);
    }

    /// <summary>
    /// Runs <paramref name="expression"/> in the page's own world, awaiting a promise, and returns its value as JSON
    /// (<see cref="JsonValueKind.Undefined"/> for <c>undefined</c>). A function (<c>"() => document.title"</c>) is called,
    /// as the browser automation library does.
    /// </summary>
    public async Task<JsonElement> EvaluateAsync(string expression)
    {
        string? value;
        try
        {
            value = await Window.EvaluateAsync(expression);
        }
        catch (SparTestScriptException exception)
        {
            throw new SparPageException("Script in the page threw: " + exception.Message);
        }

        return value is null ? default : PageJson.Parse(value);
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

    void ISparTestPageEvents.Navigated(string url, bool newDocument)
    {
        lock (_gate)
        {
            _url = url;
            if (newDocument)
            {
                _framesPending = true;
                _navigations++;
            }
        }
    }

    void ISparTestPageEvents.Console(string type, string text)
    {
        lock (_gate)
        {
            _console.Add(new SparConsoleMessage(type, text));
        }
    }

    void ISparTestPageEvents.PageError(string text)
    {
        lock (_gate)
        {
            _errors.Add(text);
        }
    }

    bool ISparTestPageEvents.Dialog(string type, string message)
    {
        // A dialog blocks the page until it is answered; nobody else may answer it in a test window.
        lock (_gate)
        {
            _dialogs.Add(new SparDialog(type, message));
            return AcceptDialogs || type == "beforeunload";
        }
    }
}

/// <summary>A message the page wrote to its console: its type (<c>log</c>, <c>error</c>, <c>warning</c>…) and text.</summary>
public sealed record SparConsoleMessage(string Type, string Text);

/// <summary>A script dialog the page opened (<c>alert</c>, <c>confirm</c>, <c>prompt</c>, <c>beforeunload</c>) and its message.</summary>
public sealed record SparDialog(string Type, string Message);
