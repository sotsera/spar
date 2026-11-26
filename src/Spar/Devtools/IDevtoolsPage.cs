namespace Spar.Devtools;

/// <summary>
/// The app window's page as the devtools drive it, which each host gives once its page automation is installed
/// (the window tests' engine in an isolated world of every document). Every member may be called from any thread; the
/// host does its part on its UI thread and keeps it short.
/// </summary>
internal interface IDevtoolsPage
{
    /// <summary>
    /// True where clicks and typing are the browser's own trusted input (CDP <c>Input.*</c> on Windows); false where
    /// the engine dispatches the page's own events instead (Linux, decision "M2's decide-first questions", item 4).
    /// </summary>
    bool TrustedInput { get; }

    /// <summary>The current document's number: it grows with each new document, and a snapshot's references are for one.</summary>
    long Document { get; }

    /// <summary>Runs one engine request in the current document's world; null when no document has the engine yet.</summary>
    Task<string?> EngineAsync(string request, TimeSpan wait);

    /// <summary>The page's visible area as PNG, its page-drawn title bar included.</summary>
    Task<byte[]> ScreenshotAsync();

    /// <summary>A trusted left click at a point of the viewport, in CSS pixels; only where <see cref="TrustedInput"/>.</summary>
    Task ClickAsync(double x, double y);

    /// <summary>Types text into the focused element (CDP <c>Input.insertText</c>, or the engine's <c>insertText</c>).</summary>
    Task InsertTextAsync(string text);
}

/// <summary>
/// Where the devtools find the page: empty until the host's window has its page automation, and again after the window
/// closed. The host sets it; the tools read it on each request.
/// </summary>
internal sealed class DevtoolsPages
{
    private IDevtoolsPage? _current;

    public IDevtoolsPage? Current
    {
        get => Volatile.Read(ref _current);
        set => Volatile.Write(ref _current, value);
    }

    /// <summary>Why the page tools cannot run on a host without a window of its own (loopback: the page is in a browser).</summary>
    public string? Unavailable { get; init; }
}
