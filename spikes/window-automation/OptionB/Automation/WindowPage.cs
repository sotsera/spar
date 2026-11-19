using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Spar.WindowAutomation.Interop;

namespace Spar.WindowAutomation.Automation;

/// <summary>
/// The window test's page: locators, waits, trusted input, screenshots, console and page errors, script dialogs,
/// all over the WebView's in-process DevTools session. No port, no second process, NativeAOT-clean.
/// </summary>
internal sealed class WindowPage
{
    private static readonly int[] s_delays = [0, 20, 50, 100, 100, 250];

    private readonly ConcurrentQueue<string> _console = new();
    private readonly ConcurrentQueue<string> _errors = new();
    private readonly ConcurrentQueue<string> _dialogs = new();
    private int _groups;

    /// <summary>Why attempts were retried (the reason without the element), for the spike's report.</summary>
    public static ConcurrentDictionary<string, int> RetryReasons { get; } = new();

    private WindowPage(Cdp cdp)
    {
        Cdp = cdp;
        Mouse = new Mouse(cdp);
        Keyboard = new Keyboard(cdp);
    }

    public Cdp Cdp { get; }

    public Mouse Mouse { get; }

    public Keyboard Keyboard { get; }

    /// <summary>Actions and assertions wait this long by default (milliseconds).</summary>
    public int DefaultTimeout { get; set; } = 5000;

    /// <summary>"type: text" of each console call since the page was attached.</summary>
    public IReadOnlyCollection<string> ConsoleMessages => _console;

    /// <summary>Uncaught exceptions (<c>Runtime.exceptionThrown</c>).</summary>
    public IReadOnlyCollection<string> PageErrors => _errors;

    /// <summary>Script dialogs seen ("alert: Saved!"), each accepted.</summary>
    public IReadOnlyCollection<string> Dialogs => _dialogs;

    /// <summary>Attaches to a view before its first navigation: console, page errors and script dialogs.</summary>
    public static async Task<WindowPage> AttachAsync(View view)
    {
        var page = new WindowPage(new Cdp(view));
        await Ui.Invoke(() =>
        {
            view.Subscribe("Runtime.consoleAPICalled", json => page._console.Enqueue(json));
            view.Subscribe("Runtime.exceptionThrown", json => page._errors.Enqueue(json));

            // The host's Page.enable is already on in this session, so dialogs arrive here; answer them at once.
            view.Subscribe("Page.javascriptDialogOpening", json =>
            {
                page._dialogs.Enqueue(json);
                _ = view.CallAsync("Page.handleJavaScriptDialog", """{"accept":true}""");
            });
        });
        await page.Cdp.CallAsync("Runtime.enable");
        return page;
    }

    public Locator GetByRole(string role, string? name = null, bool exact = false) => new(this, null, new RoleQuery(role, name, exact));

    public Locator GetByLabel(string text, bool exact = false) => new(this, null, Queries.Label(text, exact));

    public Locator GetByText(string text, bool exact = false) => new(this, null, Queries.Text(text, exact));

    public Locator GetByTestId(string id) => new(this, null, Queries.TestId(id));

    /// <summary>Two animation frames: the page has rendered what the last navigation or input produced.</summary>
    public Task WaitForUiReadyAsync() => Cdp.EvaluateAsync(Scripts.TwoFrames);

    public Task<JsonElement> EvaluateAsync(string expression) => Cdp.EvaluateAsync(expression);

    public async Task<string> UrlAsync() => (await Cdp.EvaluateAsync("location.href", awaitPromise: false)).GetString()!;

    /// <summary>Waits until the URL matches a glob (<c>**</c> any characters, <c>*</c> any but '/').</summary>
    public Task WaitForUrlAsync(string glob, int? timeout = null)
    {
        var pattern = new Regex("^" + Regex.Escape(glob).Replace(@"\*\*", ".*").Replace(@"\*", "[^/]*") + "$");
        return RetryAsync($"wait for URL {glob}", timeout, async _ =>
        {
            var url = await UrlAsync();
            return pattern.IsMatch(url) ? null : $"URL is {url}";
        });
    }

    /// <summary>A PNG of the viewport, or of <paramref name="clip"/> (x, y, width, height in page CSS pixels).</summary>
    public async Task<byte[]> CaptureAsync(double[]? clip = null)
    {
        var result = await Cdp.CallAsync("Page.captureScreenshot", w =>
        {
            w.WriteString("format", "png");
            if (clip is [var x, var y, var width, var height])
            {
                w.WriteStartObject("clip");
                w.WriteNumber("x", x);
                w.WriteNumber("y", y);
                w.WriteNumber("width", width);
                w.WriteNumber("height", height);
                w.WriteNumber("scale", 1);
                w.WriteEndObject();
            }
        });
        return Convert.FromBase64String(result.GetProperty("data").GetString()!);
    }

    public async Task ScreenshotAsync(string path) => await File.WriteAllBytesAsync(path, await CaptureAsync());

    /// <summary>
    /// Runs <paramref name="attempt"/> until it returns null (done) or the timeout passes; a non-null result says why
    /// not yet. DevTools errors (a node or context gone after a re-render or navigation) count as "not yet". Each
    /// attempt's remote objects are released afterwards.
    /// </summary>
    internal async Task RetryAsync(string what, int? timeout, Func<Attempt, Task<string?>> attempt)
    {
        var limit = timeout ?? DefaultTimeout;
        var started = Stopwatch.GetTimestamp();
        var tries = 0;
        string? why;
        while (true)
        {
            var current = new Attempt(Cdp, "spar-" + Interlocked.Increment(ref _groups));
            try
            {
                why = await attempt(current);
            }
            catch (CdpException exception)
            {
                why = exception.Message;
            }
            catch (PageException exception)
            {
                why = "page script failed: " + exception.Message;
            }
            finally
            {
                _ = current.Cdp.ReleaseGroupAsync(current.Group);
            }

            if (why is null)
            {
                return;
            }

            var reason = why.Contains(": ", StringComparison.Ordinal) ? why[(why.LastIndexOf(": ", StringComparison.Ordinal) + 2)..] : why;
            RetryReasons.AddOrUpdate(what.Split(' ')[0] + " / " + (reason.StartsWith("text is", StringComparison.Ordinal) || reason.StartsWith("count is", StringComparison.Ordinal) || reason.StartsWith("URL is", StringComparison.Ordinal) ? reason.Split(' ')[0] + " " + reason.Split(' ')[1] + " (not yet)" : reason), 1, (_, n) => n + 1);

            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (elapsed >= limit)
            {
                throw new WaitTimeoutException($"{what}: timed out after {limit} ms ({tries + 1} attempts). Last attempt: {why}");
            }

            // The stability check itself spans two frames, so an element still moving is tried again at once.
            var delay = why.EndsWith("not stable", StringComparison.Ordinal) ? 0 : s_delays[Math.Min(tries, s_delays.Length - 1)];
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(delay, Math.Max(1, limit - elapsed))));
            tries++;
        }
    }
}

/// <summary>Trusted mouse input (<c>Input.dispatchMouseEvent</c>) in CSS pixels of the viewport.</summary>
internal sealed class Mouse(Cdp cdp)
{
    public Task MoveAsync(double x, double y) => DispatchAsync("mouseMoved", x, y, null);

    public async Task ClickAsync(double x, double y)
    {
        await DispatchAsync("mouseMoved", x, y, null);
        await DispatchAsync("mousePressed", x, y, "left");
        await DispatchAsync("mouseReleased", x, y, "left");
    }

    private Task DispatchAsync(string type, double x, double y, string? button) => cdp.CallAsync("Input.dispatchMouseEvent", w =>
    {
        w.WriteString("type", type);
        w.WriteNumber("x", x);
        w.WriteNumber("y", y);
        if (button is not null)
        {
            w.WriteString("button", button);
            w.WriteNumber("buttons", type == "mousePressed" ? 1 : 0);
            w.WriteNumber("clickCount", 1);
        }
    });
}

/// <summary>Trusted key input (<c>Input.dispatchKeyEvent</c>) for named keys and single characters.</summary>
internal sealed class Keyboard(Cdp cdp)
{
    private static readonly Dictionary<string, (string Code, int KeyCode, string? Text)> s_keys = new()
    {
        ["Enter"] = ("Enter", 13, "\r"),
        ["Escape"] = ("Escape", 27, null),
        ["Tab"] = ("Tab", 9, null),
        ["Backspace"] = ("Backspace", 8, null),
        ["Delete"] = ("Delete", 46, null),
        ["ArrowDown"] = ("ArrowDown", 40, null),
        ["ArrowUp"] = ("ArrowUp", 38, null),
        ["ArrowLeft"] = ("ArrowLeft", 37, null),
        ["ArrowRight"] = ("ArrowRight", 39, null),
        ["Home"] = ("Home", 36, null),
        ["End"] = ("End", 35, null),
        [" "] = ("Space", 32, " "),
    };

    public async Task PressAsync(string key)
    {
        var (code, keyCode, text) = s_keys.TryGetValue(key, out var known) ? known
            : key.Length == 1 ? ("Key" + char.ToUpperInvariant(key[0]), char.ToUpperInvariant(key[0]), key)
            : throw new ArgumentException("Unknown key: " + key, nameof(key));
        await cdp.CallAsync("Input.dispatchKeyEvent", w =>
        {
            w.WriteString("type", text is null ? "rawKeyDown" : "keyDown");
            w.WriteString("key", key);
            w.WriteString("code", code);
            w.WriteNumber("windowsVirtualKeyCode", keyCode);
            if (text is not null)
            {
                w.WriteString("text", text);
            }
        });
        await cdp.CallAsync("Input.dispatchKeyEvent", w =>
        {
            w.WriteString("type", "keyUp");
            w.WriteString("key", key);
            w.WriteString("code", code);
            w.WriteNumber("windowsVirtualKeyCode", keyCode);
        });
    }

    public Task InsertTextAsync(string text) => cdp.CallAsync("Input.insertText", w => w.WriteString("text", text));
}
