using Spar;

namespace SqlTool.Scripts;

/// <summary>Why a script could not be copied.</summary>
public closed record CopyScriptError
{
    public sealed record ClipboardBusy : CopyScriptError;

    public sealed record ScriptTooLarge(long Limit) : CopyScriptError;

    public sealed record NoClipboard(string Reason) : CopyScriptError;
}

[SparContract]
public sealed class ScriptService(IClipboard clipboard)
{
    /// <summary>Copies the generated CREATE script of a table.</summary>
    [SparCommand]
    public async Task<Result<CopyScriptError>> CopyCreateScriptAsync(string table, CancellationToken cancellationToken)
    {
        var copied = await clipboard.SetTextAsync($"CREATE TABLE {table} (…);", cancellationToken);
        return copied switch
        {
            Success => Result.Success,
            ClipboardError.Busy => new CopyScriptError.ClipboardBusy(),
            ClipboardError.TooLarge e => new CopyScriptError.ScriptTooLarge(e.Limit),
            ClipboardError.Unavailable e => new CopyScriptError.NoClipboard(e.Reason),
        };
    }
}
