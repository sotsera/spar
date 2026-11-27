namespace Spar;

/// <summary>Why the clipboard could not be used.</summary>
public closed record ClipboardError
{
    /// <summary>Another program holds the clipboard; the host retried for about 100 ms first.</summary>
    public sealed record Busy : ClipboardError;

    /// <summary>The text is larger than the clipboard takes from Spar; the limit is in UTF-8 bytes.</summary>
    public sealed record TooLarge(int Limit) : ClipboardError;

    /// <summary>No clipboard is reachable, for example without a display, or in a host that has none.</summary>
    public sealed record Unavailable(string Reason) : ClipboardError;
}

/// <summary>
/// The backend's text clipboard, for copies a command makes itself (<c>docs/app-model.md</c> 3.7); inject it into a
/// contract. The page uses the web's own <c>navigator.clipboard</c> instead. Text only. Under the test host it is a
/// fake that holds text (<c>app.Clipboard</c>), so a test never touches the real clipboard.
/// </summary>
public interface IClipboard
{
    /// <summary>The most text <see cref="SetTextAsync"/> takes, in UTF-8 bytes: 4 MiB.</summary>
    const int MaxTextBytes = 4 * 1024 * 1024;

    /// <summary>Puts <paramref name="text"/> on the clipboard, replacing what it held.</summary>
    Task<Result<ClipboardError>> SetTextAsync(string text, CancellationToken cancellationToken);

    /// <summary>The clipboard's text; empty when it holds none (an image, a file, or nothing).</summary>
    Task<Result<string, ClipboardError>> GetTextAsync(CancellationToken cancellationToken);
}

/// <summary>The clipboard of a host that has none (a page in a browser on loopback): every call is unavailable.</summary>
internal sealed class NoClipboard : IClipboard
{
    private const string Reason = "This host has no clipboard of its own: run the app in its window, or use the page's navigator.clipboard.";

    public Task<Result<ClipboardError>> SetTextAsync(string text, CancellationToken cancellationToken) =>
        Task.FromResult<Result<ClipboardError>>(new ClipboardError.Unavailable(Reason));

    public Task<Result<string, ClipboardError>> GetTextAsync(CancellationToken cancellationToken) =>
        Task.FromResult<Result<string, ClipboardError>>(new ClipboardError.Unavailable(Reason));
}
