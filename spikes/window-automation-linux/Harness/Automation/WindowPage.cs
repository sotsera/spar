using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Spar.WindowAutomation.Linux.Automation;

/// <summary>
/// The window test's page on Linux: the same API shape as the Windows spike's Option B, over the page-script engine
/// (one <c>call_async_javascript_function</c> per attempt, in the isolated world) and an <see cref="IInput"/> backend
/// (XTEST for trusted input, or script-dispatched events). No port, no second process.
/// </summary>
internal sealed class WindowPage
{
    private static readonly int[] s_delays = [0, 20, 50, 100, 100, 250];

    private WindowPage(View view, IInput input)
    {
        View = view;
        Input = input;
        Mouse = new Mouse(this);
        Keyboard = new Keyboard(this);
    }

    /// <summary>Why attempts were retried (the reason without the element), for the spike's report.</summary>
    public static ConcurrentDictionary<string, int> RetryReasons { get; } = new();

    /// <summary>Input that the watcher saw arrive untrusted, off target or not at all.</summary>
    public static ConcurrentDictionary<string, int> InputProblems { get; } = new();

    public View View { get; }

    public IInput Input { get; }

    public Mouse Mouse { get; }

    public Keyboard Keyboard { get; }

    public int DefaultTimeout { get; set; } = 5000;

    public IReadOnlyCollection<string> ConsoleMessages => View.ConsoleMessages;

    public IReadOnlyCollection<string> PageErrors => View.PageErrors;

    public IReadOnlyCollection<string> Dialogs => View.Dialogs;

    public static WindowPage Attach(View view, IInput input) => new(view, input);

    public Locator GetByRole(string role, string? name = null, bool exact = false) => new(this, [Step.Role(role, name, exact)]);

    public Locator GetByRole(string role, Regex name) => new(this, [Step.Role(role, name)]);

    public Locator GetByLabel(string text, bool exact = false) => new(this, [Step.Label(text, exact)]);

    public Locator GetByText(string text, bool exact = false) => new(this, [Step.Text(text, exact)]);

    public Locator GetByTestId(string id) => new(this, [Step.TestId(id)]);

    /// <summary>Two animation frames: the page has rendered what the last navigation or input produced.</summary>
    public Task WaitForUiReadyAsync() => EngineAsync("""{"op":"frames"}""");

    /// <summary>Runs an expression in the page's main world and returns its value as JSON.</summary>
    public async Task<JsonElement> EvaluateAsync(string expression)
    {
        var json = await View.CallAsync("return (" + expression + ");", world: null);
        return Parse(json);
    }

    public async Task<string> UrlAsync() => (await EngineAsync("""{"op":"url"}""")).GetString()!;

    public Task WaitForUrlAsync(string glob, int? timeout = null)
    {
        var pattern = new Regex("^" + Regex.Escape(glob).Replace(@"\*\*", ".*").Replace(@"\*", "[^/]*") + "$");
        return RetryAsync($"wait for URL {glob}", timeout, async () =>
        {
            var url = await UrlAsync();
            return pattern.IsMatch(url) ? null : $"URL is {url}";
        });
    }

    /// <summary>A PNG of the visible region (<c>webkit_web_view_get_snapshot</c>), or of a clip in CSS pixels.</summary>
    public async Task<byte[]> CaptureAsync(double[]? clip = null)
    {
        var (pixels, width, height) = await View.SnapshotAsync();
        return clip is [var x, var y, var w, var h]
            ? Png.Encode(pixels, width, height, (int)Math.Floor(x), (int)Math.Floor(y), (int)Math.Ceiling(w), (int)Math.Ceiling(h))
            : Png.Encode(pixels, width, height, 0, 0, width, height);
    }

    public async Task ScreenshotAsync(string path) => await File.WriteAllBytesAsync(path, await CaptureAsync());

    /// <summary>One engine call in the isolated world; the request is JSON.</summary>
    internal async Task<JsonElement> EngineAsync(string request) =>
        Parse(await View.CallAsync("return globalThis.__sparAutomation.run(" + request + ");"));

    internal void NoteInput(string what, JsonElement landed)
    {
        var trusted = landed.TryGetProperty("trusted", out var t) && t.GetBoolean();
        var onTarget = landed.TryGetProperty("onTarget", out var o) && o.GetBoolean();
        var timedOut = landed.TryGetProperty("timedOut", out _);
        var key = timedOut ? "not seen" : !onTarget ? "off target" : Input.Trusted && !trusted ? "untrusted" : null;
        if (key is not null)
        {
            InputProblems.AddOrUpdate(what + ": " + key, 1, (_, n) => n + 1);
        }
    }

    /// <summary>
    /// Runs <paramref name="attempt"/> until it returns null (done) or the timeout passes; a non-null result says why
    /// not yet. A script exception counts as "not yet" (a navigation can replace the world mid-call).
    /// </summary>
    internal async Task RetryAsync(string what, int? timeout, Func<Task<string?>> attempt)
    {
        var limit = timeout ?? DefaultTimeout;
        var started = Stopwatch.GetTimestamp();
        var tries = 0;
        while (true)
        {
            string? why;
            try
            {
                why = await attempt();
            }
            catch (PageException exception)
            {
                why = "page script failed: " + exception.Message;
            }

            if (why is null)
            {
                return;
            }

            var reason = why.Contains(": ", StringComparison.Ordinal) ? why[(why.LastIndexOf(": ", StringComparison.Ordinal) + 2)..] : why;
            var bucket = reason.StartsWith("text is", StringComparison.Ordinal) || reason.StartsWith("count is", StringComparison.Ordinal) || reason.StartsWith("URL is", StringComparison.Ordinal)
                ? string.Join(' ', reason.Split(' ').Take(2)) + " (not yet)" : reason;
            RetryReasons.AddOrUpdate(what.Split(' ')[0] + " / " + bucket, 1, (_, n) => n + 1);

            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (elapsed >= limit)
            {
                throw new WaitTimeoutException($"{what}: timed out after {limit} ms ({tries + 1} attempts). Last attempt: {why}");
            }

            // The stability check itself spans a frame, so an element still moving is tried again at once.
            var delay = why.EndsWith("not stable", StringComparison.Ordinal) ? 0 : s_delays[Math.Min(tries, s_delays.Length - 1)];
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(delay, Math.Max(1, limit - elapsed))));
            tries++;
        }
    }

    internal static JsonElement Parse(string? json)
    {
        if (json is null)
        {
            return default;
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

/// <summary>Pointer input at viewport CSS pixels, through the page's input backend; waits until the page saw it.</summary>
internal sealed class Mouse(WindowPage page)
{
    public Task MoveAsync(double x, double y) => page.Input.MoveAsync(page.View, x, y);

    public async Task ClickAsync(double x, double y)
    {
        await page.EngineAsync("""{"op":"arm","kind":"pointer"}""");
        await page.Input.ClickAsync(page.View, x, y);
        page.NoteInput("click", await page.EngineAsync("""{"op":"landed","timeout":2000}"""));
    }
}

/// <summary>Keys and text through the page's input backend; each waits until the page saw the last key-up.</summary>
internal sealed class Keyboard(WindowPage page)
{
    public async Task PressAsync(string key)
    {
        await page.EngineAsync("""{"op":"arm","kind":"key"}""");
        await page.Input.PressAsync(page.View, key);
        page.NoteInput("press", await page.EngineAsync("""{"op":"landed","timeout":2000}"""));
    }

    public async Task TypeAsync(string text)
    {
        await page.EngineAsync("""{"op":"arm","kind":"key"}""");
        await page.Input.TypeAsync(page.View, text);
        page.NoteInput("type", await page.EngineAsync($$"""{"op":"landed","timeout":3000,"count":{{text.Length}}}"""));
    }
}
