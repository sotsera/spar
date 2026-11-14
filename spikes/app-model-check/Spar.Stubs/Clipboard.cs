namespace Spar;

/// <summary>Why the clipboard could not be used.</summary>
public closed record ClipboardError
{
    /// <summary>Another program holds the clipboard; the host retried briefly first.</summary>
    public sealed record Busy : ClipboardError;

    public sealed record TooLarge(long Limit) : ClipboardError;

    /// <summary>No clipboard is reachable, for example without a display.</summary>
    public sealed record Unavailable(string Reason) : ClipboardError;
}

/// <summary>Text clipboard for the backend; injectable. The page uses the web's navigator.clipboard instead.</summary>
public interface IClipboard
{
    Task<Result<ClipboardError>> SetTextAsync(string text, CancellationToken cancellationToken);

    Task<Result<string, ClipboardError>> GetTextAsync(CancellationToken cancellationToken);
}
