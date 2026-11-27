using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Spar.Runtime;

/// <summary>
/// Where a generated stream handler writes its items: each one is serialized into its own buffer, so one that cannot be
/// written leaves nothing half-sent (plan section 2, "Writer failures"), then handed to the transport, which batches it
/// under the page's credits (a stream) or into a bounded buffer that drops the oldest (a subscription).
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class SparItemSink
{
    /// <summary>The items the page has been sent so far, for a failure's <c>itemsSent</c>.</summary>
    public abstract long ItemsSent { get; }

    /// <summary>
    /// Writes one item. A cancelled call stops here, at the producer's next <c>yield</c>, even when its own code ignores
    /// the token (plan section 2c, "Cancellation"); a stream also waits here while the page has no credit left.
    /// </summary>
    public ValueTask WriteAsync<T>(T item, Action<Utf8JsonWriter, T> write, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadOnlyMemory<byte> json;
        try
        {
            json = SparJson.Write(item, write);
        }
        catch (Exception exception)
        {
            // The command ran and has sent ItemsSent items: the stream stops with a response failure.
            throw new SparStreamStopped(new SparFailure("response", "serialization", "stopped", $"{exception.GetType().Name}: {exception.Message}", exception));
        }

        return PushAsync(json, cancellationToken);
    }

    /// <summary>
    /// An item of a stream that declares errors: true with its value, or false with the declared error that ends the
    /// stream. A <c>default</c> result is neither, and stops the stream with a response failure: the command ran.
    /// </summary>
    public static bool TryGetValue<T, TError>(Result<T, TError> item, [MaybeNullWhen(false)] out T value, [NotNullWhen(false)] out TError? error)
        where TError : class
    {
        try
        {
            error = item.TryGetValue(out value) ? null : item.Error;
            return error is null;
        }
        catch (InvalidOperationException exception)
        {
            throw new SparStreamStopped(new SparFailure("response", "serialization", "stopped", exception.Message, exception));
        }
    }

    /// <summary>The stream's end with its declared error, in-band (plan section 2c, "Stream failure delivery").</summary>
    public SparOutcome End<TError>(TError error, Action<Utf8JsonWriter, TError> write)
    {
        try
        {
            return SparOutcome.FromError(SparJson.Write(error, write));
        }
        catch (Exception exception)
        {
            throw new SparStreamStopped(new SparFailure("response", "serialization", "stopped", $"{exception.GetType().Name}: {exception.Message}", exception));
        }
    }

    /// <summary>Hands one serialized item to the transport.</summary>
    protected abstract ValueTask PushAsync(ReadOnlyMemory<byte> item, CancellationToken cancellationToken);
}

/// <summary>A stream stopped by Spar itself, not by the command: its failure is the stream's terminal message.</summary>
internal sealed class SparStreamStopped(SparFailure failure) : Exception(failure.ToString(), failure.Exception)
{
    public SparFailure Failure { get; } = failure;
}
