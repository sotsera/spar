using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Spar.Bridge;

namespace Spar.Runtime;

/// <summary>
/// Runs the app's streams and subscriptions for its typed test clients (decision "M3a's decide-first questions", item 4),
/// through the bridge's own batching, credits and subscription buffer, and reads their items with the app's own
/// serializers. It takes items as a page does: 256 credits, topped up once half are used. A test can drop the channel and
/// restore it: the streams that were open end as lost, and an automatic subscription waits for the channel, subscribes
/// again and reports <c>Reconnecting</c>, <c>Reset</c> and <c>Resumed</c>, as the page's SDK does.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class SparStreamClient
{
    /// <summary>The page's credit window (decision "M3a's decide-first questions", item 2).</summary>
    internal const int Credits = 256;

    /// <summary>How long an automatic subscription waits for the channel before it ends as lost.</summary>
    private static readonly TimeSpan ResumeLimit = TimeSpan.FromSeconds(30);

    private static readonly JsonReaderOptions ItemsOptions = new() { MaxDepth = SparJson.MaxDepth + 1 };

    private readonly SparDispatcher _dispatcher;
    private readonly Func<long> _nextId;
    private readonly Lock _gate = new();
    private CancellationTokenSource _channel = new();
    private TaskCompletionSource _restored = Restored();

    internal SparStreamClient(SparDispatcher dispatcher, Func<long> nextId)
    {
        _dispatcher = dispatcher;
        _nextId = nextId;
    }

    /// <summary>A one-shot stream: its values; a failure throws <see cref="SparCallFailedException"/>.</summary>
    public async IAsyncEnumerable<T> Stream<T>(
        string contract, string command, ReadOnlyMemory<byte> request, SparReader<T> value,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var run = Open(contract, command, request, cancellationToken);
        while (await run.NextAsync() is { } batch)
        {
            foreach (var item in Items(batch, value))
            {
                yield return item;
                run.Consumed(1);
            }
        }

        run.ThrowUnlessEnded(contract, command, hasErrors: false, cancellationToken);
    }

    /// <summary>A one-shot stream that declares errors: its values, then the error that ended it, if one did.</summary>
    public async IAsyncEnumerable<Result<T, TError>> Stream<T, TError>(
        string contract, string command, ReadOnlyMemory<byte> request, SparReader<T> value, SparReader<TError> error,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TError : class
    {
        await using var run = Open(contract, command, request, cancellationToken);
        while (await run.NextAsync() is { } batch)
        {
            foreach (var item in Items(batch, value))
            {
                yield return item;
                run.Consumed(1);
            }
        }

        if (run.EndedWithError)
        {
            yield return SparJson.Read(run.Outcome.Payload, error);
            yield break;
        }

        run.ThrowUnlessEnded(contract, command, hasErrors: true, cancellationToken);
    }

    /// <summary>A subscription without declared errors: its values and status events.</summary>
    public async IAsyncEnumerable<SparSubscriptionItem<T>> Subscribe<T>(
        string contract, string command, ReadOnlyMemory<byte> request, SparReader<T> value, bool automatic,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            await using (var run = Open(contract, command, request, cancellationToken))
            {
                if (attempt > 0)
                {
                    yield return new(new SparSubscriptionEvent.Reset());
                    yield return new(new SparSubscriptionEvent.Resumed());
                }

                while (await run.NextAsync() is { } batch)
                {
                    if (batch.Dropped is { } dropped)
                    {
                        yield return new(new SparSubscriptionEvent.Gap(dropped));
                    }

                    foreach (var item in Items(batch, value))
                    {
                        yield return new(item);
                        run.Consumed(1);
                    }
                }

                if (!(automatic && run.Lost))
                {
                    run.ThrowUnlessEnded(contract, command, hasErrors: false, cancellationToken);
                    yield break;
                }
            }

            yield return new(new SparSubscriptionEvent.Reconnecting(attempt + 1));
            await WaitForChannelAsync(contract, command, cancellationToken);
        }
    }

    /// <summary>A subscription that declares errors: its values, its status events, and the error that ended it, if one did.</summary>
    public async IAsyncEnumerable<SparSubscriptionItem<T, TError>> Subscribe<T, TError>(
        string contract, string command, ReadOnlyMemory<byte> request, SparReader<T> value, SparReader<TError> error, bool automatic,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TError : class
    {
        for (var attempt = 0; ; attempt++)
        {
            await using (var run = Open(contract, command, request, cancellationToken))
            {
                if (attempt > 0)
                {
                    yield return new(new SparSubscriptionEvent.Reset());
                    yield return new(new SparSubscriptionEvent.Resumed());
                }

                while (await run.NextAsync() is { } batch)
                {
                    if (batch.Dropped is { } dropped)
                    {
                        yield return new(new SparSubscriptionEvent.Gap(dropped));
                    }

                    foreach (var item in Items(batch, value))
                    {
                        yield return new(item);
                        run.Consumed(1);
                    }
                }

                if (run.EndedWithError)
                {
                    yield return new(SparJson.Read(run.Outcome.Payload, error));
                    yield break;
                }

                if (!(automatic && run.Lost))
                {
                    run.ThrowUnlessEnded(contract, command, hasErrors: true, cancellationToken);
                    yield break;
                }
            }

            yield return new(new SparSubscriptionEvent.Reconnecting(attempt + 1));
            await WaitForChannelAsync(contract, command, cancellationToken);
        }
    }

    /// <summary>The test drops the channel: every open stream ends as lost, and none opens until it is restored.</summary>
    internal void Drop()
    {
        CancellationTokenSource dropped;
        lock (_gate)
        {
            if (!_restored.Task.IsCompleted)
            {
                return;
            }

            dropped = _channel;
            _channel = new CancellationTokenSource();
            _restored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        dropped.Cancel();
        dropped.Dispose();
    }

    /// <summary>The channel is back: waiting subscriptions subscribe again.</summary>
    internal void Restore()
    {
        TaskCompletionSource restored;
        lock (_gate)
        {
            restored = _restored;
        }

        restored.TrySetResult();
    }

    private static TaskCompletionSource Restored()
    {
        var restored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        restored.SetResult();
        return restored;
    }

    private static List<T> Items<T>(BridgeMessage batch, SparReader<T> read)
    {
        var items = new List<T>();
        var reader = new Utf8JsonReader(batch.Payload.Span, ItemsOptions);
        reader.Read();
        SparJson.Expect(ref reader, JsonTokenType.StartArray);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            items.Add(read(ref reader));
        }

        return items;
    }

    private Run Open(string contract, string command, ReadOnlyMemory<byte> request, CancellationToken cancellationToken)
    {
        CancellationToken channel;
        lock (_gate)
        {
            channel = _restored.Task.IsCompleted ? _channel.Token : new CancellationToken(canceled: true);
        }

        return new Run(this, contract, command, request, channel, cancellationToken);
    }

    private async Task WaitForChannelAsync(string contract, string command, CancellationToken cancellationToken)
    {
        Task restored;
        lock (_gate)
        {
            restored = _restored.Task;
        }

        try
        {
            await restored.WaitAsync(ResumeLimit, _dispatcher.Time, cancellationToken);
        }
        catch (TimeoutException)
        {
            throw new SparCallFailedException(contract, command, new SparFailure("transport", "lost", "unknown"));
        }
    }

    /// <summary>One stream's run: its messages as they come, its credits, and how it ended.</summary>
    private sealed class Run : IAsyncDisposable
    {
        private readonly Channel<BridgeMessage> _messages = Channel.CreateUnbounded<BridgeMessage>(new UnboundedChannelOptions { SingleReader = true });
        private readonly BridgeStream _stream;
        private readonly CancellationTokenSource _call;
        private readonly CancellationToken _channel;
        private readonly CancellationToken _caller;
        private readonly Task _running;
        private int _consumed;

        public Run(SparStreamClient client, string contract, string command, ReadOnlyMemory<byte> request, CancellationToken channel, CancellationToken caller)
        {
            var dispatcher = client._dispatcher;
            var entry = dispatcher.Find(contract, command);
            var id = client._nextId();
            _channel = channel;
            _caller = caller;
            _call = CancellationTokenSource.CreateLinkedTokenSource(caller, channel);
            _stream = new BridgeStream(
                id, Credits, entry?.Capacity ?? 0, entry?.LatestOnly ?? false, dispatcher.Limits.MaxResultBytes,
                message => _messages.Writer.TryWrite(message), dispatcher.Time);
            _running = Task.Run(async () =>
            {
                Outcome = await dispatcher.InvokeStreamAsync(contract, command, id, request, _stream, _call.Token, SparCaller.Test);

                // The last items go before the end; the end itself is read from Outcome.
                _stream.End(_ => new BridgeMessage(BridgeMessageType.End) { Id = id });
                _stream.Dispose();
                _messages.Writer.TryComplete();
            });
        }

        public SparOutcome Outcome { get; private set; }

        /// <summary>The test dropped the channel while the stream ran, and the caller did not cancel it.</summary>
        public bool Lost => Outcome.Kind == SparOutcomeKind.Failure && _channel.IsCancellationRequested && !_caller.IsCancellationRequested;

        public bool EndedWithError => Outcome.Kind == SparOutcomeKind.Error;

        /// <summary>The next batch of items, or null once the stream has ended.</summary>
        public async ValueTask<BridgeMessage?> NextAsync()
        {
            while (await _messages.Reader.WaitToReadAsync(CancellationToken.None))
            {
                if (_messages.Reader.TryRead(out var message) && message.Type == BridgeMessageType.Items)
                {
                    return message;
                }
            }

            await _running;
            return null;
        }

        /// <summary>Items the test took: credits go back to the stream once half the window is used.</summary>
        public void Consumed(int count)
        {
            _consumed += count;
            if (_consumed >= Credits / 2)
            {
                _stream.Grant(_consumed);
                _consumed = 0;
            }
        }

        /// <summary>
        /// The stream ended: nothing more for one that finished; the caller's own cancellation as such; a lost channel, a
        /// failure, or an error the client does not expect as <see cref="SparCallFailedException"/>.
        /// </summary>
        public void ThrowUnlessEnded(string contract, string command, bool hasErrors, CancellationToken cancellationToken)
        {
            if (Outcome.Kind == SparOutcomeKind.Value)
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var failure = Lost ? new SparFailure("transport", "lost", "unknown")
                : Outcome.Kind == SparOutcomeKind.Failure ? Outcome.Failure!
                : new SparFailure("protocol", hasErrors ? "malformed" : "unknown-error-case", "unknown");
            throw new SparCallFailedException(contract, command, failure);
        }

        /// <summary>A test that leaves the loop early stops the stream and waits for it to end, as a page waits for the confirmation.</summary>
        public async ValueTask DisposeAsync()
        {
            await _call.CancelAsync();
            await _running;
            _call.Dispose();
        }
    }
}
