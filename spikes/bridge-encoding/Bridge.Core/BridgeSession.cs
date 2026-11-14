using System.Buffers;
using System.Diagnostics;
using System.Text.Json;

namespace Bridge;

/// <summary>
/// The host side of one document's bridge, for any transport and encoding. Frames come in through
/// <see cref="Receive"/>; every reply goes out through the send callback, which must copy the bytes before it returns
/// and may be called from any thread. Commands of the <c>bench</c> contract: <c>echo</c>, <c>save</c>, <c>fail</c>
/// (a response-serialization failure), <c>wait</c> (runs until cancelled), and the streams <c>rows</c> (pull, credits)
/// and <c>watch</c> (subscription: a bounded drop-oldest buffer filled at a fixed rate).
/// </summary>
public sealed class BridgeSession(ICodec codec, Action<ReadOnlyMemory<byte>> send) : IDisposable
{
    private static readonly JsonWriterOptions s_writerOptions = new() { SkipValidation = true };

    [ThreadStatic]
    private static ArrayBufferWriter<byte>? t_frame;

    private readonly Dictionary<long, Call> _calls = [];

    // A cancel can overtake its call when they travel on different connections (loopback POSTs): remember it.
    private readonly HashSet<long> _cancelledEarly = [];
    private long _malformed;
    private long _unrecognized;
    private long _framesOut;
    private long _bytesOut;
    private long _pushFrames;
    private long _pushItems;

    public string? Document { get; private set; }

    public ICodec Codec => codec;

    /// <summary>Counters for the run report.</summary>
    public (long Malformed, long Unrecognized, long FramesOut, long BytesOut, long PushFrames, long PushItems) Stats =>
        (Interlocked.Read(ref _malformed), Interlocked.Read(ref _unrecognized), Interlocked.Read(ref _framesOut),
         Interlocked.Read(ref _bytesOut), Interlocked.Read(ref _pushFrames), Interlocked.Read(ref _pushItems));

    public void Receive(ReadOnlyMemory<byte> data)
    {
        var result = codec.TryRead(data, out var frame);
        if (!result.Ok)
        {
            Interlocked.Increment(ref _malformed);
            if (frame.Id >= 0)
            {
                Send(new Frame { Kind = Kind.NotExecuted, Id = frame.Id, Code = "invalid-request", Detail = result.Error });
            }

            return;
        }

        switch (frame.Kind)
        {
            case Kind.Hello:
                Document = frame.Doc;
                Send(new Frame { Kind = Kind.Open, Id = -1, Doc = frame.Doc, Protocol = 1 });
                break;
            case Kind.Call:
                Unary(frame);
                break;
            case Kind.Stream:
                StartStream(frame);
                break;
            case Kind.Credits:
                Find(frame.Id)?.Credits.Release(frame.Credits);
                break;
            case Kind.Cancel:
                CancelCall(frame.Id);
                break;
            case Kind.Unrecognized:
                Interlocked.Increment(ref _unrecognized);
                break;
        }
    }

    public void Dispose()
    {
        lock (_calls)
        {
            foreach (var call in _calls.Values)
            {
                call.Cancel();
            }
        }
    }

    private void Unary(in Frame frame)
    {
        var id = frame.Id;
        if (Refuse(frame))
        {
            return;
        }

        switch (frame.Command)
        {
            case "echo":
                if (!frame.Payload.IsEmpty && !Payloads.Validate(frame.Payload.Span))
                {
                    Send(new Frame { Kind = Kind.NotExecuted, Id = id, Code = "invalid-request", Detail = "The argument is not valid JSON." });
                    return;
                }

                SendResult(id, frame.Payload.Span);
                break;
            case "save":
                if (!Payloads.Validate(frame.Payload.Span))
                {
                    Send(new Frame { Kind = Kind.NotExecuted, Id = id, Code = "invalid-request", Detail = "The argument is not valid JSON." });
                    return;
                }

                Send(new Frame { Kind = Kind.Result, Id = id, Payload = Payloads.NoteResult(id) });
                break;
            case "fail":
                Send(new Frame { Kind = Kind.Result, Id = id, Payload = Payloads.SerializationFailure() });
                break;
            case "wait":
                {
                    var call = Register(id, 0);
                    _ = Task.Delay(Timeout.Infinite, call.Token).ContinueWith(
                        _ =>
                        {
                            End(call);
                            Send(Frame.Of(Kind.Cancelled, id));
                        },
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                    break;
                }

            default:
                Send(new Frame { Kind = Kind.NotExecuted, Id = id, Code = "unknown-command", Detail = frame.Contract + "." + frame.Command });
                break;
        }
    }

    private bool Refuse(in Frame frame)
    {
        if (frame.Fingerprint != Payloads.Fingerprint)
        {
            Send(new Frame { Kind = Kind.NotExecuted, Id = frame.Id, Code = "contract-mismatch", Detail = Payloads.Fingerprint });
            return true;
        }

        if (frame.Contract != "bench")
        {
            Send(new Frame { Kind = Kind.NotExecuted, Id = frame.Id, Code = "unknown-command", Detail = frame.Contract + "." + frame.Command });
            return true;
        }

        return false;
    }

    private void SendResult(long id, ReadOnlySpan<byte> value)
    {
        // The command's result, buffered before any of it is sent (wire rule): {"ok":true[,"value":…]}.
        var buffer = new ArrayBufferWriter<byte>(value.Length + 24);
        if (value.IsEmpty)
        {
            buffer.Write("{\"ok\":true}"u8);
        }
        else
        {
            buffer.Write("{\"ok\":true,\"value\":"u8);
            buffer.Write(value);
            buffer.Write("}"u8);
        }

        Send(new Frame { Kind = Kind.Result, Id = id, Payload = buffer.WrittenMemory });
    }

    private void StartStream(in Frame frame)
    {
        if (Refuse(frame))
        {
            return;
        }

        var id = frame.Id;
        var batch = Math.Clamp(frame.Batch, 1, 100_000);
        var arg = ReadArguments(frame.Payload.Span);
        switch (frame.Command)
        {
            case "rows":
                {
                    var call = Register(id, frame.Credits);
                    _ = Task.Run(() => PumpRowsAsync(call, arg.Count, batch), CancellationToken.None);
                    break;
                }

            case "watch":
                {
                    var call = Register(id, frame.Credits);
                    _ = Task.Run(() => PumpWatchAsync(call, arg, batch), CancellationToken.None);
                    break;
                }

            default:
                Send(new Frame { Kind = Kind.NotExecuted, Id = id, Code = "unknown-command", Detail = frame.Contract + "." + frame.Command });
                break;
        }
    }

    private async Task PumpRowsAsync(Call call, long count, int batch)
    {
        var buffer = new ArrayBufferWriter<byte>(batch * 256);
        using var writer = new Utf8JsonWriter(buffer, s_writerOptions);
        Frame final;
        try
        {
            for (long sent = 0; sent < count;)
            {
                await call.Credits.WaitAsync(call.Token).ConfigureAwait(false);
                var n = (int)Math.Min(batch, count - sent);
                buffer.ResetWrittenCount();
                writer.Reset(buffer);
                Payloads.WriteRows(writer, sent, n);
                writer.Flush();
                Send(new Frame { Kind = Kind.Items, Id = call.Id, Payload = buffer.WrittenMemory });
                sent += n;
            }

            final = Frame.Of(Kind.End, call.Id);
        }
        catch (OperationCanceledException) when (call.Token.IsCancellationRequested)
        {
            final = Frame.Of(Kind.Cancelled, call.Id);
        }

        End(call);
        Send(final);
    }

    private async Task PumpWatchAsync(Call call, Arguments arg, int batch)
    {
        var ring = new Ring(arg.Capacity);
        var producer = new Thread(() => ring.Produce(arg.Rate, arg.Milliseconds, call.Token)) { IsBackground = true, Name = "watch producer" };
        producer.Start();
        var buffer = new ArrayBufferWriter<byte>(batch * 128);
        using var writer = new Utf8JsonWriter(buffer, s_writerOptions);
        var events = new List<(long Seq, double At)>(batch);
        Frame final;
        try
        {
            while (true)
            {
                await call.Credits.WaitAsync(call.Token).ConfigureAwait(false);
                var dropped = await ring.TakeAsync(events, batch, call.Token).ConfigureAwait(false);
                if (events.Count == 0)
                {
                    break;
                }

                buffer.ResetWrittenCount();
                writer.Reset(buffer);
                writer.WriteStartArray();
                for (var i = 0; i < events.Count; i++)
                {
                    Payloads.WriteEvent(writer, events[i].Seq, events[i].At);
                }

                writer.WriteEndArray();
                writer.Flush();
                Interlocked.Increment(ref _pushFrames);
                Interlocked.Add(ref _pushItems, events.Count);
                Send(new Frame { Kind = Kind.Items, Id = call.Id, Dropped = dropped, Payload = buffer.WrittenMemory });
            }

            final = Frame.Of(Kind.End, call.Id);
        }
        catch (OperationCanceledException) when (call.Token.IsCancellationRequested)
        {
            final = Frame.Of(Kind.Cancelled, call.Id);
        }

        call.Cancel();
        producer.Join();
        End(call);
        Send(final);
    }

    private void Send(in Frame frame)
    {
        var buffer = t_frame ??= new ArrayBufferWriter<byte>(64 * 1024);
        buffer.ResetWrittenCount();
        codec.Write(frame, buffer);
        Interlocked.Increment(ref _framesOut);
        Interlocked.Add(ref _bytesOut, buffer.WrittenCount);
        send(buffer.WrittenMemory);
    }

    private Call Register(long id, int credits)
    {
        var call = new Call(id, credits);
        bool early;
        lock (_calls)
        {
            _calls[id] = call;
            early = _cancelledEarly.Remove(id);
        }

        if (early)
        {
            call.Cancel();
        }

        return call;
    }

    private void CancelCall(long id)
    {
        Call? call;
        lock (_calls)
        {
            if (!_calls.TryGetValue(id, out call))
            {
                _cancelledEarly.Add(id);
                return;
            }
        }

        call.Cancel();
    }

    private Call? Find(long id)
    {
        lock (_calls)
        {
            return _calls.GetValueOrDefault(id);
        }
    }

    private void End(Call call)
    {
        lock (_calls)
        {
            _calls.Remove(call.Id);
        }
    }

    private static Arguments ReadArguments(ReadOnlySpan<byte> json)
    {
        var arg = new Arguments(100_000, 20_000, 3_000, 1_000);
        if (json.IsEmpty)
        {
            return arg;
        }

        var reader = new Utf8JsonReader(json);
        while (reader.Read())
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            var name = reader.GetString();
            reader.Read();
            if (reader.TokenType != JsonTokenType.Number)
            {
                continue;
            }

            arg = name switch
            {
                "count" => arg with { Count = reader.GetInt64() },
                "rate" => arg with { Rate = reader.GetInt32() },
                "ms" => arg with { Milliseconds = reader.GetInt32() },
                "capacity" => arg with { Capacity = reader.GetInt32() },
                _ => arg,
            };
        }

        return arg;
    }

    private readonly record struct Arguments(long Count, int Rate, int Milliseconds, int Capacity);

    private sealed class Call(long id, int credits)
    {
        private readonly CancellationTokenSource _cancellation = new();

        public long Id { get; } = id;

        public SemaphoreSlim Credits { get; } = new(credits);

        public CancellationToken Token => _cancellation.Token;

        public void Cancel()
        {
            try
            {
                _cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    /// <summary>A bounded drop-oldest buffer filled by a producer thread at a fixed rate.</summary>
    private sealed class Ring(int capacity)
    {
        private readonly Queue<(long Seq, double At)> _items = new(capacity);
        private TaskCompletionSource? _waiter;
        private long _dropped;
        private bool _completed;

        public void Produce(int rate, int milliseconds, CancellationToken token)
        {
            var clock = Stopwatch.StartNew();
            long produced = 0;
            while (!token.IsCancellationRequested && clock.ElapsedMilliseconds < milliseconds)
            {
                var due = (long)(clock.Elapsed.TotalSeconds * rate);
                if (due > produced)
                {
                    var at = Payloads.NowUnixMs();
                    TaskCompletionSource? waiter;
                    lock (_items)
                    {
                        for (; produced < due; produced++)
                        {
                            if (_items.Count == capacity)
                            {
                                _items.Dequeue();
                                _dropped++;
                            }

                            _items.Enqueue((produced, at));
                        }

                        waiter = _waiter;
                        _waiter = null;
                    }

                    waiter?.TrySetResult();
                }

                Thread.Sleep(1);
            }

            TaskCompletionSource? last;
            lock (_items)
            {
                _completed = true;
                last = _waiter;
                _waiter = null;
            }

            last?.TrySetResult();
        }

        /// <summary>Takes up to <paramref name="max"/> items; none means the producer finished.</summary>
        public async ValueTask<long> TakeAsync(List<(long Seq, double At)> into, int max, CancellationToken token)
        {
            into.Clear();
            while (true)
            {
                Task wait;
                lock (_items)
                {
                    if (_items.Count > 0)
                    {
                        while (into.Count < max && _items.TryDequeue(out var item))
                        {
                            into.Add(item);
                        }

                        var dropped = _dropped;
                        _dropped = 0;
                        return dropped;
                    }

                    if (_completed)
                    {
                        return 0;
                    }

                    _waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    wait = _waiter.Task;
                }

                await wait.WaitAsync(token).ConfigureAwait(false);
            }
        }
    }
}
