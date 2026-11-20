using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Spar.Testing;

/// <summary>
/// The page in the app's real window, for a window test (plan section 5, level 5): <c>app.Page</c> once the app started
/// with <c>o.Surface = SparTestSurface.Window</c>. Its locators, input and assertions use the method names of Microsoft's
/// browser automation library, so a window journey reads like a browser journey, but it is Spar's own API: it runs in
/// the test process over the window's in-process DevTools session, with no port and no second process, under CoreCLR
/// and NativeAOT (decision "Step 8's shape: window tests driven by Spar's own API").
/// </summary>
public sealed class SparPage
{
    internal SparPage(PageSession session)
    {
        Session = session;
        Keyboard = new SparKeyboard(session);
        Mouse = new SparMouse(session);
    }

    /// <summary>Trusted key input to the element that has the focus.</summary>
    public SparKeyboard Keyboard { get; }

    /// <summary>Trusted pointer input at a point of the viewport.</summary>
    public SparMouse Mouse { get; }

    /// <summary>The current document's address, such as <c>https://app.localhost/</c>.</summary>
    public string Url => Session.Url;

    /// <summary>How long actions wait for their element, in milliseconds: 10 s unless set.</summary>
    public float DefaultTimeout { get; set; } = 10_000;

    /// <summary>How long assertions retry, in milliseconds: 5 s unless set.</summary>
    public float DefaultExpectTimeout { get; set; } = 5_000;

    /// <summary>Whether script dialogs (<c>alert</c>, <c>confirm</c>, <c>prompt</c>) are accepted; they are dismissed unless set.</summary>
    public bool AcceptDialogs
    {
        get => Session.AcceptDialogs;
        set => Session.AcceptDialogs = value;
    }

    /// <summary>What the page wrote to its console so far, in order.</summary>
    public IReadOnlyList<SparConsoleMessage> ConsoleMessages => Session.ConsoleMessages;

    /// <summary>The page's uncaught exceptions so far, with their stacks.</summary>
    public IReadOnlyList<string> PageErrors => Session.PageErrors;

    /// <summary>The script dialogs the page opened so far; each was answered at once.</summary>
    public IReadOnlyList<SparDialog> Dialogs => Session.Dialogs;

    internal PageSession Session { get; }

    public SparLocator GetByRole(SparRole role, SparGetByRoleOptions? options = null) => Root(LocatorStep.Role(role, options));

    public SparLocator GetByLabel(string text, SparGetByTextOptions? options = null) =>
        Root(LocatorStep.Text("label", "GetByLabel", text, null, options));

    public SparLocator GetByLabel(Regex text) => Root(LocatorStep.Text("label", "GetByLabel", null, text, null));

    public SparLocator GetByText(string text, SparGetByTextOptions? options = null) =>
        Root(LocatorStep.Text("text", "GetByText", text, null, options));

    public SparLocator GetByText(Regex text) => Root(LocatorStep.Text("text", "GetByText", null, text, null));

    public SparLocator GetByPlaceholder(string text, SparGetByTextOptions? options = null) =>
        Root(LocatorStep.Text("placeholder", "GetByPlaceholder", text, null, options));

    public SparLocator GetByAltText(string text, SparGetByTextOptions? options = null) =>
        Root(LocatorStep.Text("alt", "GetByAltText", text, null, options));

    public SparLocator GetByTitle(string text, SparGetByTextOptions? options = null) =>
        Root(LocatorStep.Text("title", "GetByTitle", text, null, options));

    /// <summary>Elements matching a CSS selector, for structure that roles do not name.</summary>
    public SparLocator Locator(string selector) => Root(LocatorStep.Css(selector));

    /// <summary>
    /// Runs script in the page's own world and returns its value as JSON; a function (<c>"() => document.title"</c>)
    /// is called, and a promise awaited.
    /// </summary>
    public Task<JsonElement> EvaluateAsync(string expression) => Session.EvaluateAsync(expression);

    /// <summary>
    /// <see cref="EvaluateAsync(string)"/> read as <typeparamref name="T"/>: <see cref="string"/>, <see cref="bool"/>,
    /// <see cref="int"/>, <see cref="long"/>, <see cref="double"/> or <see cref="JsonElement"/>, read without
    /// reflection so it works under NativeAOT.
    /// </summary>
    public async Task<T> EvaluateAsync<T>(string expression)
    {
        var value = await Session.EvaluateAsync(expression);
        object? read = typeof(T) == typeof(string) ? value.ValueKind == JsonValueKind.Null ? null : value.GetString()
            : typeof(T) == typeof(bool) ? value.GetBoolean()
            : typeof(T) == typeof(int) ? value.GetInt32()
            : typeof(T) == typeof(long) ? value.GetInt64()
            : typeof(T) == typeof(double) ? value.GetDouble()
            : typeof(T) == typeof(JsonElement) ? value
            : throw new NotSupportedException($"EvaluateAsync reads string, bool, int, long, double or JsonElement, not {typeof(T).Name}.");
        return (T)read!;
    }

    /// <summary>A PNG of the viewport, also written to <see cref="SparScreenshotOptions.Path"/> when set.</summary>
    public Task<byte[]> ScreenshotAsync(SparScreenshotOptions? options = null) => CaptureAsync(null, options?.Path);

    /// <summary>
    /// Waits until the page's address matches <paramref name="url"/>: the whole address, or a glob in which <c>**</c>
    /// matches anything and <c>*</c> anything but <c>/</c> (<c>"**/settings"</c>).
    /// </summary>
    public Task WaitForURLAsync(string url, SparActionOptions? options = null) => WaitForURLAsync(Glob(url), url, options);

    public Task WaitForURLAsync(Regex url, SparActionOptions? options = null) => WaitForURLAsync(url, "/" + url + "/", options);

    /// <summary>
    /// Waits until the current document has loaded and the page has drawn two frames since: what a person would see is
    /// on the screen, and trusted input reaches it at once.
    /// </summary>
    public Task WaitForUiReadyAsync(SparActionOptions? options = null) =>
        PageRetry.RunAsync("WaitForUiReadyAsync", Timeout(options?.Timeout), async left =>
        {
            var ready = await Session.EvaluateAsync("document.readyState === 'complete'");
            if (ready.ValueKind != JsonValueKind.True)
            {
                return Attempt<bool>.Retry("the document is still loading");
            }

            return await Session.EngineAsync("frames", _ => { }, left) is null
                ? Attempt<bool>.Retry("the page is loading")
                : Attempt<bool>.Success(true);
        });

    /// <summary>Loads the current document again and waits for its load to finish.</summary>
    public Task ReloadAsync(SparActionOptions? options = null) => NavigateAsync(null, options);

    /// <summary>Opens <paramref name="path"/> of the app's own origin (<c>"/settings"</c>) and waits for its load to finish.</summary>
    public Task GotoAsync(string path, SparActionOptions? options = null)
    {
        var address = new Uri(Session.Window.Origin, path).ToString();
        return NavigateAsync(address, options);
    }

    /// <summary>The document's title.</summary>
    public async Task<string> TitleAsync() => (await Session.EvaluateAsync("document.title")).GetString() ?? "";

    internal TimeSpan Timeout(float? milliseconds) => TimeSpan.FromMilliseconds(milliseconds ?? DefaultTimeout);

    internal TimeSpan ExpectTimeout(float? milliseconds) => TimeSpan.FromMilliseconds(milliseconds ?? DefaultExpectTimeout);

    /// <summary>A PNG of the viewport, or of <paramref name="clip"/> in CSS pixels; written to <paramref name="path"/> when given.</summary>
    internal async Task<byte[]> CaptureAsync((double X, double Y, double Width, double Height)? clip, string? path)
    {
        var png = await Session.Window.ScreenshotAsync(clip);
        if (path is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            await File.WriteAllBytesAsync(path, png);
        }

        return png;
    }

    /// <summary>A glob as a regular expression: <c>**</c> any characters, <c>*</c> any but <c>/</c>, <c>?</c> one.</summary>
    internal static Regex Glob(string glob)
    {
        var pattern = Regex.Escape(glob).Replace(@"\*\*", "\u0001", StringComparison.Ordinal)
            .Replace(@"\*", "[^/]*", StringComparison.Ordinal)
            .Replace("\u0001", ".*", StringComparison.Ordinal)
            .Replace(@"\?", ".", StringComparison.Ordinal);
        return new Regex("^" + pattern + "$", RegexOptions.CultureInvariant);
    }

    private SparLocator Root(LocatorStep step) => new(this, [step]);

    private Task WaitForURLAsync(Regex pattern, string shown, SparActionOptions? options) =>
        PageRetry.RunAsync($"WaitForURLAsync({shown})", Timeout(options?.Timeout), _ =>
        {
            var url = Url;
            return Task.FromResult(pattern.IsMatch(url) ? Attempt<bool>.Success(true) : Attempt<bool>.Retry($"the address is {url}"));
        });

    /// <summary>A navigation (a reload when <paramref name="address"/> is null), which the host's navigation lock still sees; then its load.</summary>
    private async Task NavigateAsync(string? address, SparActionOptions? options)
    {
        var started = Stopwatch.GetTimestamp();
        var timeout = Timeout(options?.Timeout);
        var before = Session.Navigations;
        await Session.Window.NavigateAsync(address);
        var left = PageSession.Remaining(started, timeout);
        await PageRetry.RunAsync($"{(address is null ? "ReloadAsync" : $"GotoAsync(\"{address}\")")}", left, async remaining =>
        {
            if (Session.Navigations == before)
            {
                return Attempt<bool>.Retry("the navigation has not started");
            }

            var state = await Session.EngineAsync("frames", _ => { }, remaining);
            return state is null
                ? Attempt<bool>.Retry("the new document is not ready")
                : Attempt<bool>.Success(true);
        });
    }
}
