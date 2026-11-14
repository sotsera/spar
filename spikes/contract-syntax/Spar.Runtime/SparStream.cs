using System.Buffers;
using System.Text.Json;

namespace Spar;

/// <summary>One batch read from a stream: the number of items written.</summary>
public readonly record struct SparBatch(int Count);

/// <summary>
/// The backend side of a streaming command. A host pulls it in batches; each batch is written as one JSON array.
/// The command runs only as fast as the host reads, so back-pressure reaches the producer. Disposing the stream
/// disposes the command's iterator, then the DI scope that owns the contract instance.
/// </summary>
public abstract class SparStream : IAsyncDisposable
{
    /// <summary>Writes up to <paramref name="maxItems"/> items as one JSON array. A count of 0 means the stream ended.</summary>
    public abstract ValueTask<SparBatch> ReadBatchAsync(IBufferWriter<byte> output, int maxItems, CancellationToken cancellationToken);

    /// <summary>Completes only when the command's code has stopped running and its scope is disposed.</summary>
    public abstract ValueTask DisposeAsync();

    public static SparStream Create<T>(
        IAsyncEnumerable<T> source, Action<Utf8JsonWriter, T> write, IAsyncDisposable? owner, CancellationToken cancellationToken) =>
        new PullStream<T>(source.GetAsyncEnumerator(cancellationToken), write, owner);

    private sealed class PullStream<T>(IAsyncEnumerator<T> enumerator, Action<Utf8JsonWriter, T> write, IAsyncDisposable? owner)
        : SparStream
    {
        private readonly Utf8JsonWriter _writer = new(new ArrayBufferWriter<byte>(1));
        private bool _ended;

        public override async ValueTask<SparBatch> ReadBatchAsync(IBufferWriter<byte> output, int maxItems, CancellationToken cancellationToken)
        {
            if (_ended)
            {
                return default;
            }

            var count = 0;
            _writer.Reset(output);
            _writer.WriteStartArray();
            while (count < maxItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    _ended = true;
                    break;
                }

                write(_writer, enumerator.Current);
                count++;
            }

            _writer.WriteEndArray();
            _writer.Flush();
            return new SparBatch(count);
        }

        public override async ValueTask DisposeAsync()
        {
            try
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                _writer.Dispose();
                if (owner is not null)
                {
                    await owner.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }
}

/// <summary>The request payload was not valid for the command: it never ran.</summary>
public sealed class SparInvalidRequestException(string message, Exception inner) : Exception(message, inner);

/// <summary>No such contract or command: nothing ran.</summary>
public sealed class SparUnknownCommandException(string message) : InvalidOperationException(message);
