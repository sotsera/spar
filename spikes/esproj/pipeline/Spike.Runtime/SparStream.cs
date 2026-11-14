using System.Buffers;
using System.Runtime.ExceptionServices;
using System.Text.Json;

namespace Spike;

/// <summary>One batch read from a stream: the items written, and how many older items a subscription dropped before them.</summary>
public readonly record struct SparBatch(int Count, long Dropped);

/// <summary>
/// The backend side of a streaming command. A host pulls it in batches; each batch is written as one JSON array.
/// A pull stream (<see cref="Create{T}"/>) runs the command only as fast as the host reads, so back-pressure reaches
/// the producer. A subscription (<see cref="Subscribe{T}"/>) never blocks its producer: it keeps a bounded buffer and
/// drops the oldest items when the reader falls behind, reporting how many it dropped.
/// </summary>
public abstract class SparStream : IAsyncDisposable
{
    /// <summary>Writes up to <paramref name="maxItems"/> items as one JSON array. A count of 0 means the stream ended.</summary>
    public abstract ValueTask<SparBatch> ReadBatchAsync(IBufferWriter<byte> output, int maxItems, CancellationToken cancellationToken);

    /// <summary>Completes only when the command's code has stopped running (its iterator was disposed).</summary>
    public abstract ValueTask DisposeAsync();

    public static SparStream Create<T>(IAsyncEnumerable<T> source, Action<Utf8JsonWriter, T> write, CancellationToken cancellationToken) =>
        new PullStream<T>(source.GetAsyncEnumerator(cancellationToken), write);

    public static SparStream Subscribe<T>(IAsyncEnumerable<T> source, Action<Utf8JsonWriter, T> write, int capacity, CancellationToken cancellationToken) =>
        new RingStream<T>(source, write, capacity, cancellationToken);

    private sealed class PullStream<T>(IAsyncEnumerator<T> enumerator, Action<Utf8JsonWriter, T> write) : SparStream
    {
        private readonly Utf8JsonWriter _writer = new(new ArrayBufferWriter<byte>(1));
        private Task<bool>? _pending;
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
                bool hasItem;
                if (_pending is { } pending)
                {
                    // Flush what the batch has rather than hold rows back while the source waits.
                    if (count > 0 && !pending.IsCompleted)
                    {
                        break;
                    }

                    _pending = null;
                    hasItem = await pending.ConfigureAwait(false);
                }
                else
                {
                    var next = enumerator.MoveNextAsync();
                    if (count > 0 && !next.IsCompleted)
                    {
                        _pending = next.AsTask();
                        break;
                    }

                    hasItem = await next.ConfigureAwait(false);
                }

                if (!hasItem)
                {
                    _ended = true;
                    break;
                }

                write(_writer, enumerator.Current);
                count++;
            }

            _writer.WriteEndArray();
            _writer.Flush();
            return new SparBatch(count, 0);
        }

        public override async ValueTask DisposeAsync()
        {
            // An iterator cannot be disposed while a MoveNextAsync is running; wait for it to finish first.
            if (_pending is { } pending)
            {
                _pending = null;
                await pending.ContinueWith(static _ => { }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default)
                    .ConfigureAwait(false);
            }

            await enumerator.DisposeAsync().ConfigureAwait(false);
            _writer.Dispose();
        }
    }

    private sealed class RingStream<T> : SparStream
    {
        private readonly Queue<T> _items;
        private readonly int _capacity;
        private readonly Action<Utf8JsonWriter, T> _write;
        private readonly Utf8JsonWriter _writer = new(new ArrayBufferWriter<byte>(1));
        private readonly Task _pump;
        private readonly List<T> _batch = [];
        private TaskCompletionSource? _waiter;
        private long _dropped;
        private bool _completed;
        private ExceptionDispatchInfo? _fault;

        public RingStream(IAsyncEnumerable<T> source, Action<Utf8JsonWriter, T> write, int capacity, CancellationToken cancellationToken)
        {
            _items = new Queue<T>(capacity);
            _capacity = capacity;
            _write = write;
            _pump = Task.Run(() => PumpAsync(source, cancellationToken), CancellationToken.None);
        }

        public override async ValueTask<SparBatch> ReadBatchAsync(IBufferWriter<byte> output, int maxItems, CancellationToken cancellationToken)
        {
            long dropped;
            while (true)
            {
                Task wait;
                lock (_items)
                {
                    if (_items.Count > 0)
                    {
                        _batch.Clear();
                        while (_batch.Count < maxItems && _items.TryDequeue(out var item))
                        {
                            _batch.Add(item);
                        }

                        dropped = _dropped;
                        _dropped = 0;
                        break;
                    }

                    if (_completed)
                    {
                        _fault?.Throw();
                        return default;
                    }

                    _waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    wait = _waiter.Task;
                }

                await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            _writer.Reset(output);
            _writer.WriteStartArray();
            foreach (var item in _batch)
            {
                _write(_writer, item);
            }

            _writer.WriteEndArray();
            _writer.Flush();
            return new SparBatch(_batch.Count, dropped);
        }

        public override async ValueTask DisposeAsync()
        {
            // The pump ends when the subscription's token is cancelled (the host cancels before disposing).
            await _pump.ConfigureAwait(false);
            _writer.Dispose();
        }

        private async Task PumpAsync(IAsyncEnumerable<T> source, CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    TaskCompletionSource? waiter;
                    lock (_items)
                    {
                        if (_items.Count == _capacity)
                        {
                            _items.Dequeue();
                            _dropped++;
                        }

                        _items.Enqueue(item);
                        waiter = _waiter;
                        _waiter = null;
                    }

                    waiter?.TrySetResult();
                }
            }
            catch (Exception exception)
            {
                lock (_items)
                {
                    _fault = ExceptionDispatchInfo.Capture(exception);
                }
            }
            finally
            {
                TaskCompletionSource? waiter;
                lock (_items)
                {
                    _completed = true;
                    waiter = _waiter;
                    _waiter = null;
                }

                waiter?.TrySetResult();
            }
        }
    }
}

/// <summary>The request payload was not valid for the command: it never ran.</summary>
public sealed class SparInvalidRequestException(string message, Exception inner) : Exception(message, inner);

/// <summary>No such contract or command: nothing ran.</summary>
public sealed class SparUnknownCommandException(string message) : InvalidOperationException(message);

/// <summary>Marks a streaming command as a subscription: a bounded buffer that drops the oldest items instead of blocking.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class SparSubscriptionAttribute(int capacity) : Attribute
{
    public int Capacity { get; } = capacity;
}
