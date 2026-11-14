using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Spike;

/// <summary>
/// One in-flight command, keyed by the page's document and call id. The host cancels its token when the page sends a
/// cancel message, when the document goes away, or when the host shuts down. Streams sent over a message channel
/// consume one credit per batch; the page grants credits as it consumes.
/// </summary>
public sealed class SparCall
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly object _gate = new();
    private int _credits;
    private TaskCompletionSource? _creditWaiter;

    internal SparCall(string document, string id, long epoch, SparTraceContext trace)
    {
        Document = document;
        Id = id;
        Epoch = epoch;
        Trace = trace;
    }

    public string Document { get; }

    /// <summary>The page's trace context for this call (empty when the page sent none).</summary>
    public SparTraceContext Trace { get; }

    public string Id { get; }

    /// <summary>The host's document epoch when the call started; replies from an older epoch are dropped.</summary>
    public long Epoch { get; }

    public CancellationToken Token => _cancellation.Token;

    /// <summary>Monotonic timestamp of the first cancel request, for latency measurements.</summary>
    public long CancelRequestedAt { get; private set; }

    public void Cancel()
    {
        if (CancelRequestedAt == 0)
        {
            CancelRequestedAt = Stopwatch.GetTimestamp();
        }

        _cancellation.Cancel();
    }

    public void AddCredits(int credits)
    {
        TaskCompletionSource? waiter;
        lock (_gate)
        {
            _credits += credits;
            waiter = _creditWaiter;
            _creditWaiter = null;
        }

        waiter?.TrySetResult();
    }

    public async ValueTask TakeCreditAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Task wait;
            lock (_gate)
            {
                if (_credits > 0)
                {
                    _credits--;
                    return;
                }

                _creditWaiter ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                wait = _creditWaiter.Task;
            }

            await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>The host's table of in-flight calls. Thread-safe.</summary>
public sealed class SparCallRegistry
{
    private readonly Dictionary<string, SparCall> _calls = new(StringComparer.Ordinal);

    // A cancel can overtake its call when they travel on different connections; remember it briefly.
    private readonly Dictionary<string, long> _cancelledEarly = new(StringComparer.Ordinal);
    private long _epoch;

    public long Epoch => Interlocked.Read(ref _epoch);

    public int Count
    {
        get
        {
            lock (_calls)
            {
                return _calls.Count;
            }
        }
    }

    /// <summary>Registers a call, or returns null when the page already cancelled it (it never runs).</summary>
    public SparCall? Start(string document, string id, SparTraceContext trace = default)
    {
        var key = document + ":" + id;
        lock (_calls)
        {
            if (_cancelledEarly.Remove(key))
            {
                return null;
            }

            var call = new SparCall(document, id, _epoch, trace);
            _calls[key] = call;
            return call;
        }
    }

    public void Cancel(string document, string id)
    {
        var key = document + ":" + id;
        SparCall? call;
        lock (_calls)
        {
            if (!_calls.TryGetValue(key, out call))
            {
                PruneEarly();
                _cancelledEarly[key] = Stopwatch.GetTimestamp();
                return;
            }
        }

        call.Cancel();
    }

    public SparCall? Find(string document, string id)
    {
        lock (_calls)
        {
            return _calls.GetValueOrDefault(document + ":" + id);
        }
    }

    public void End(SparCall call)
    {
        lock (_calls)
        {
            _calls.Remove(call.Document + ":" + call.Id);
        }
    }

    /// <summary>Cancels every call of one document (its page went away).</summary>
    public int CancelDocument(string document)
    {
        List<SparCall> cancelled;
        lock (_calls)
        {
            cancelled = [.. _calls.Values.Where(c => c.Document == document)];
        }

        cancelled.ForEach(c => c.Cancel());
        return cancelled.Count;
    }

    /// <summary>Starts a new epoch (navigation) and cancels every call of the previous ones.</summary>
    public int NewEpoch()
    {
        List<SparCall> cancelled;
        lock (_calls)
        {
            _epoch++;
            cancelled = [.. _calls.Values];
            _cancelledEarly.Clear();
        }

        cancelled.ForEach(c => c.Cancel());
        return cancelled.Count;
    }

    private void PruneEarly()
    {
        var limit = Stopwatch.GetTimestamp() - Stopwatch.Frequency * 30;
        foreach (var stale in _cancelledEarly.Where(e => e.Value < limit).Select(e => e.Key).ToList())
        {
            _cancelledEarly.Remove(stale);
        }
    }
}

/// <summary>How a command ended, as the page learns it.</summary>
public enum SparOutcome
{
    /// <summary>Completed: <c>r</c> (unary result) or <c>e</c> (stream end).</summary>
    Completed,

    /// <summary>Confirmed stopped by cancellation: the command's code is no longer running.</summary>
    Cancelled,

    /// <summary>Refused before it ran (bad request, unknown command, cancelled before start).</summary>
    NotExecuted,

    /// <summary>Failed while running: effects are unknown.</summary>
    Unknown,
}

/// <summary>
/// One message of the bridge protocol, shared by every transport. Text, one per postMessage or per line of a streamed
/// response: a kind letter, the call id, kind-specific fields, then optional JSON (compact JSON never contains a raw
/// newline). Host to page:
/// <c>r id json</c> unary result; <c>i id dropped json-array</c> items; <c>e id</c> end; <c>c id</c> cancelled and
/// stopped; <c>n id code json</c> not executed; <c>u id code json</c> outcome unknown; <c>o doc</c> channel open.
/// Page to host: <c>h doc</c> hello; <c>q id contract command fingerprint trace json</c> unary;
/// <c>s id contract command fingerprint trace batch credits json</c> stream; <c>k id credits</c>; <c>x id</c> cancel.
/// <c>trace</c> is <c>-</c>, or a W3C traceparent optionally followed by <c>;</c> and the URI-escaped tracestate.
/// </summary>
public readonly record struct SparMessage(char Kind, string Id, string? Field, ReadOnlyMemory<byte> Json)
{
    public static SparMessage Result(string id, ReadOnlyMemory<byte> json) => new('r', id, null, json);

    public static SparMessage Items(string id, long dropped, ReadOnlyMemory<byte> json) =>
        new('i', id, dropped.ToString(System.Globalization.CultureInfo.InvariantCulture), json);

    public static SparMessage End(string id) => new('e', id, null, default);

    public static SparMessage Cancelled(string id) => new('c', id, null, default);

    public static SparMessage NotExecuted(string id, string code, string? detail) => new('n', id, code, ErrorJson(code, detail));

    public static SparMessage Unknown(string id, string code, string? detail) => new('u', id, code, ErrorJson(code, detail));

    public SparOutcome Outcome => Kind switch
    {
        'c' => SparOutcome.Cancelled,
        'n' => SparOutcome.NotExecuted,
        'u' => SparOutcome.Unknown,
        _ => SparOutcome.Completed,
    };

    /// <summary>Writes the message as UTF-8, optionally followed by a newline (for line-framed streams).</summary>
    public void WriteTo(IBufferWriter<byte> output, bool newline)
    {
        var header = Field is null ? $"{Kind} {Id}" : $"{Kind} {Id} {Field}";
        var bytes = output.GetSpan(header.Length * 3 + 2);
        var written = Encoding.UTF8.GetBytes(header, bytes);
        if (!Json.IsEmpty)
        {
            bytes[written++] = (byte)' ';
        }

        output.Advance(written);
        if (!Json.IsEmpty)
        {
            output.Write(Json.Span);
        }

        if (newline)
        {
            output.Write("\n"u8);
        }
    }

    public override string ToString()
    {
        var buffer = new ArrayBufferWriter<byte>(Json.Length + 32);
        WriteTo(buffer, newline: false);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    public static byte[] ErrorJson(string code, string? detail)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("error"u8, code);
            writer.WriteString("detail"u8, detail);
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }
}

/// <summary>Runs commands for a host and turns every ending into exactly one protocol message.</summary>
public static class SparDispatch
{
    /// <summary>Runs a unary command. Never throws: the result is the single reply for the page.</summary>
    public static async Task<SparMessage> InvokeAsync(
        SparInvoke invoke,
        SparCall call,
        string contract,
        string command,
        ReadOnlyMemory<byte> payload)
    {
        try
        {
            var result = await invoke(contract, command, payload, call.Trace, call.Token).ConfigureAwait(false);
            return SparMessage.Result(call.Id, result);
        }
        catch (Exception exception)
        {
            return Classify(call, exception);
        }
    }

    /// <summary>
    /// Pumps a stream to the page until it ends, fails or is cancelled, then sends exactly one terminal message.
    /// "Cancelled" is sent only after the command's iterator has been disposed, so its code is no longer running.
    /// </summary>
    public static async Task PumpAsync(
        Func<CancellationToken, SparStream> open,
        SparCall call,
        int batchSize,
        bool useCredits,
        Func<SparMessage, CancellationToken, ValueTask> send)
    {
        SparStream stream;
        try
        {
            stream = open(call.Token);
        }
        catch (Exception exception)
        {
            await SendFinalAsync(send, Classify(call, exception)).ConfigureAwait(false);
            return;
        }

        var buffer = new ArrayBufferWriter<byte>(64 * 1024);
        SparMessage final;
        try
        {
            while (true)
            {
                if (useCredits)
                {
                    await call.TakeCreditAsync(call.Token).ConfigureAwait(false);
                }

                buffer.ResetWrittenCount();
                var batch = await stream.ReadBatchAsync(buffer, batchSize, call.Token).ConfigureAwait(false);
                if (batch.Count == 0)
                {
                    break;
                }

                await send(SparMessage.Items(call.Id, batch.Dropped, buffer.WrittenMemory), call.Token).ConfigureAwait(false);
            }

            final = SparMessage.End(call.Id);
        }
        catch (Exception exception)
        {
            final = Classify(call, exception);
        }

        // A failed send (the page went away) leaves a subscription's producer running: stop it before disposing.
        if (final.Kind != 'e')
        {
            call.Cancel();
        }

        try
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (final.Kind is 'e' or 'c')
        {
            // The iterator failed while stopping (its finally block threw): the outcome is no longer known.
            final = SparMessage.Unknown(call.Id, "failed-while-stopping", exception.Message);
        }

        await SendFinalAsync(send, final).ConfigureAwait(false);
    }

    public static SparMessage Classify(SparCall call, Exception exception) => exception switch
    {
        // Confirmed only when our token was cancelled and the command ended by observing it.
        OperationCanceledException when call.Token.IsCancellationRequested => SparMessage.Cancelled(call.Id),
        SparInvalidRequestException invalid => SparMessage.NotExecuted(call.Id, "invalid-request", invalid.Message),
        SparUnknownCommandException unknown => SparMessage.NotExecuted(call.Id, "unknown-command", unknown.Message),
        _ => SparMessage.Unknown(call.Id, "failed", exception.GetType().Name + ": " + exception.Message),
    };

    private static async ValueTask SendFinalAsync(Func<SparMessage, CancellationToken, ValueTask> send, SparMessage final)
    {
        try
        {
            await send(final, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or OperationCanceledException)
        {
            // The page or its channel is gone; nobody is left to tell.
        }
    }
}
