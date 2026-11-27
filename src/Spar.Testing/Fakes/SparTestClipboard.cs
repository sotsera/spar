using System.Collections.Concurrent;
using System.Text;

namespace Spar.Testing;

/// <summary>
/// The clipboard of a test app (<c>app.Clipboard</c>): text held in memory, so a test never reads or replaces the
/// real clipboard of the machine it runs on. Set <see cref="Text"/> to what another program copied; read it after the
/// app copied. <see cref="FailNext"/> makes the next call fail, as when another program holds the clipboard.
/// </summary>
public sealed class SparTestClipboard : IClipboard
{
    private readonly ConcurrentQueue<ClipboardError> _failures = new();
    private volatile string _text = "";

    internal SparTestClipboard()
    {
    }

    /// <summary>The clipboard's text; empty when it holds none.</summary>
    public string Text
    {
        get => _text;
        set => _text = value ?? "";
    }

    /// <summary>The next call fails with <paramref name="error"/>, such as <see cref="ClipboardError.Busy"/>.</summary>
    public SparTestClipboard FailNext(ClipboardError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        _failures.Enqueue(error);
        return this;
    }

    Task<Result<ClipboardError>> IClipboard.SetTextAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (_failures.TryDequeue(out var failure))
        {
            return Task.FromResult<Result<ClipboardError>>(failure);
        }

        if (Encoding.UTF8.GetByteCount(text) > IClipboard.MaxTextBytes)
        {
            return Task.FromResult<Result<ClipboardError>>(new ClipboardError.TooLarge(IClipboard.MaxTextBytes));
        }

        _text = text;
        return Task.FromResult<Result<ClipboardError>>(Result.Success);
    }

    Task<Result<string, ClipboardError>> IClipboard.GetTextAsync(CancellationToken cancellationToken) =>
        Task.FromResult<Result<string, ClipboardError>>(_failures.TryDequeue(out var failure) ? failure : _text);
}
