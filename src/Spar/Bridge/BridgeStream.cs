using Spar.Runtime;

namespace Spar.Bridge;

/// <summary>
/// One open stream or subscription of a document (plan section 2c; decision "M3a's decide-first questions"). The page
/// grants credits in items. A one-shot stream's producer waits at its next item while the page has none left, so a slow
/// page slows it. A subscription's producer never waits: its values go to a buffer of the subscription's capacity, which
/// drops the oldest and reports how many with the next batch, or, for one that keeps only its newest value, to a single
/// slot that newer values replace silently. Items travel in batches of at most <see cref="BatchItems"/> items or
/// <see cref="BatchBytes"/> bytes, or what <see cref="Window"/> collected; an item after a quiet window goes at once.
/// Everything this stream sends, its terminal message included, is sent under its lock, so nothing overtakes anything.
/// </summary>
internal sealed class BridgeStream : SparItemSink, IDisposable
{
    public const int BatchItems = 64;

    public const int BatchBytes = 64 * 1024;

    /// <summary>Credits a page may hold at once: the SDK grants 256 at a time, and no page needs a million.</summary>
    private const long MaxCredits = 1 << 20;

    public static readonly TimeSpan Window = TimeSpan.FromMilliseconds(16);

    private readonly long _id;
    private readonly int _capacity;
    private readonly bool _latestOnly;
    private readonly int _maxItemBytes;
    private readonly Action<BridgeMessage> _send;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly List<ReadOnlyMemory<byte>> _batch = [];
    private readonly Queue<ReadOnlyMemory<byte>> _buffer = new();
    private ReadOnlyMemory<byte>? _latest;
    private int _batchBytes;
    private long _credits;
    private long _droppedSinceBatch;
    private long _sent;
    private long _lastSend;
    private bool _sentAny;
    private bool _flushScheduled;
    private ITimer? _timer;
    private TaskCompletionSource? _creditWaiter;
    private bool _ended;

    /// <param name="capacity">The subscription's buffer; 0 for a one-shot stream.</param>
    /// <param name="send">Sends one message to the page; called under this stream's lock, so it must not call back.</param>
    public BridgeStream(long id, int credits, int capacity, bool latestOnly, int maxItemBytes, Action<BridgeMessage> send, TimeProvider time)
    {
        _id = id;
        _credits = Math.Clamp(credits, 0, MaxCredits);
        _capacity = capacity;
        _latestOnly = latestOnly;
        _maxItemBytes = maxItemBytes;
        _send = send;
        _time = time;
    }

    public override long ItemsSent
    {
        get
        {
            lock (_gate)
            {
                return _sent;
            }
        }
    }

    /// <summary>The values a subscription dropped for a page that fell behind, for its span.</summary>
    public long Dropped { get; private set; }

    private bool IsSubscription => _capacity > 0;

    /// <summary>Nothing was sent for a whole window: an item now goes at once instead of waiting for others.</summary>
    private bool Quiet => !_sentAny || _time.GetElapsedTime(_lastSend) >= Window;

    /// <summary>The page took items and grants more: a waiting producer goes on, and a subscription's buffer drains.</summary>
    public void Grant(int credits)
    {
        if (credits <= 0)
        {
            return;
        }

        TaskCompletionSource? waiter;
        lock (_gate)
        {
            if (_ended)
            {
                return;
            }

            _credits = Math.Min(_credits + credits, MaxCredits);
            waiter = _creditWaiter;
            _creditWaiter = null;
            if (IsSubscription)
            {
                PumpLocked();
            }
        }

        waiter?.TrySetResult();
    }

    /// <summary>
    /// The stream ended: everything its producer yielded goes first (a subscription's buffer is bounded, so it is sent
    /// whatever the credits), then the one terminal message, built with the items the page was sent.
    /// </summary>
    public void End(Func<long, BridgeMessage> terminal)
    {
        lock (_gate)
        {
            if (_ended)
            {
                return;
            }

            if (_latest is { } latest)
            {
                _latest = null;
                AddFinalLocked(latest);
            }

            while (_buffer.TryDequeue(out var item))
            {
                AddFinalLocked(item);
            }

            FlushLocked();
            _ended = true;
            _timer?.Dispose();
            _send(terminal(_sent));
        }
    }

    /// <summary>The document went away: nothing more is sent, and a producer waiting for credits stops on its token.</summary>
    public void Dispose()
    {
        TaskCompletionSource? waiter;
        lock (_gate)
        {
            _ended = true;
            _timer?.Dispose();
            _batch.Clear();
            _buffer.Clear();
            waiter = _creditWaiter;
            _creditWaiter = null;
        }

        waiter?.TrySetResult();
    }

    protected override ValueTask PushAsync(ReadOnlyMemory<byte> item, CancellationToken cancellationToken)
    {
        if (item.Length > _maxItemBytes)
        {
            throw new SparStreamStopped(new SparFailure(
                "response", "too-large", "stopped", $"an item is {item.Length} bytes; the limit is {_maxItemBytes}"));
        }

        if (IsSubscription)
        {
            Offer(item);
            return ValueTask.CompletedTask;
        }

        return TryTake(item, out var waiting) ? ValueTask.CompletedTask : new ValueTask(WaitAndTakeAsync(item, waiting!, cancellationToken));
    }

    /// <summary>A one-shot stream's item: batched when the page has a credit; otherwise the batch goes and the producer waits.</summary>
    private bool TryTake(ReadOnlyMemory<byte> item, out Task? waiting)
    {
        lock (_gate)
        {
            waiting = null;
            if (_ended)
            {
                // The document went away; the call's token stops the producer.
                return true;
            }

            if (_credits > 0)
            {
                _credits--;
                AddLocked(item);
                return true;
            }

            // What is batched was credited already: send it, so the page can take it and grant more.
            FlushLocked();
            _creditWaiter ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            waiting = _creditWaiter.Task;
            return false;
        }
    }

    private async Task WaitAndTakeAsync(ReadOnlyMemory<byte> item, Task waiting, CancellationToken cancellationToken)
    {
        while (true)
        {
            await waiting.WaitAsync(cancellationToken);
            if (TryTake(item, out var next))
            {
                return;
            }

            waiting = next!;
        }
    }

    /// <summary>A subscription's value: into the buffer, never waiting; a full buffer drops its oldest.</summary>
    private void Offer(ReadOnlyMemory<byte> item)
    {
        lock (_gate)
        {
            if (_ended)
            {
                return;
            }

            if (_latestOnly)
            {
                // Only the newest matters: an older value that has not gone yet is replaced, and no gap is reported.
                _latest = item;
                PumpLocked();
                return;
            }

            if (_buffer.Count >= _capacity)
            {
                // The values batched so far come before the gap, so they go first, and the next batch reports it.
                FlushLocked();
                _buffer.Dequeue();
                _droppedSinceBatch++;
                Dropped++;
            }

            _buffer.Enqueue(item);
            PumpLocked();
        }
    }

    /// <summary>Moves a subscription's values into the batch while the page has credits.</summary>
    private void PumpLocked()
    {
        if (_latestOnly)
        {
            OfferLatestLocked();
            return;
        }

        while (_credits > 0 && _buffer.TryDequeue(out var item))
        {
            _credits--;
            AddLocked(item);
        }
    }

    /// <summary>
    /// A latest-only subscription's newest value goes alone: at once after a quiet window, else when the window ends,
    /// by which time newer values may have replaced it.
    /// </summary>
    private void OfferLatestLocked()
    {
        if (_latest is null || _credits == 0)
        {
            return;
        }

        if (Quiet)
        {
            SendLatestLocked();
            return;
        }

        ScheduleLocked();
    }

    private void SendLatestLocked()
    {
        _credits--;
        _batch.Add(_latest!.Value);
        _latest = null;
        FlushLocked();
    }

    private void AddLocked(ReadOnlyMemory<byte> item)
    {
        _batch.Add(item);
        _batchBytes += item.Length;
        var full = _batch.Count >= BatchItems || _batchBytes >= BatchBytes;
        if (full || (_batch.Count == 1 && Quiet))
        {
            FlushLocked();
            return;
        }

        ScheduleLocked();
    }

    private void ScheduleLocked()
    {
        if (_flushScheduled)
        {
            return;
        }

        _flushScheduled = true;
        _timer ??= _time.CreateTimer(static state => ((BridgeStream)state!).OnWindow(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _timer.Change(Window, Timeout.InfiniteTimeSpan);
    }

    /// <summary>A value sent at the end whatever the credits, still in batches of the usual size.</summary>
    private void AddFinalLocked(ReadOnlyMemory<byte> item)
    {
        _batch.Add(item);
        _batchBytes += item.Length;
        if (_batch.Count >= BatchItems || _batchBytes >= BatchBytes)
        {
            FlushLocked();
        }
    }

    private void OnWindow()
    {
        lock (_gate)
        {
            if (_ended)
            {
                return;
            }

            FlushLocked();
            if (_latestOnly && _latest is not null && _credits > 0)
            {
                SendLatestLocked();
            }
        }
    }

    /// <summary>Sends the batch as one <c>items</c> message, a JSON array of the items, with the values dropped before it.</summary>
    private void FlushLocked()
    {
        _flushScheduled = false;
        if (_batch.Count == 0)
        {
            return;
        }

        // The items, each preceded by '[' or ',', then the closing ']'.
        var payload = new byte[_batch.Sum(item => item.Length) + _batch.Count + 1];
        var at = 0;
        for (var i = 0; i < _batch.Count; i++)
        {
            payload[at] = i == 0 ? (byte)'[' : (byte)',';
            at++;
            _batch[i].Span.CopyTo(payload.AsSpan(at));
            at += _batch[i].Length;
        }

        payload[at] = (byte)']';
        var dropped = _droppedSinceBatch;
        _droppedSinceBatch = 0;
        _sent += _batch.Count;
        _batch.Clear();
        _batchBytes = 0;
        _lastSend = _time.GetTimestamp();
        _sentAny = true;
        _send(new BridgeMessage(BridgeMessageType.Items) { Id = _id, Dropped = dropped > 0 ? dropped : null, Payload = payload });
    }
}
