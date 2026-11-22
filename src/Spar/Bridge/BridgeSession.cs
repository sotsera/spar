using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Spar.Runtime;

namespace Spar.Bridge;

/// <summary>
/// The bridge for one document (plan section 2c): hello and open, the fingerprint checked once, increasing ids,
/// commands dispatched off the caller's thread, cancels (also one that overtakes its call), exactly one terminal
/// message per call, and nothing sent after <see cref="Close"/>. Its limits (<see cref="BridgeOptions"/>) apply before
/// any work: a message's size before it is decoded, and a slot or a place in the queue before its payload is read.
/// An exception reaches the page as the call's correlation id, its details only in the app's log and, in Development,
/// in the payload. Every call that names a command is counted, and an admitted one traced, by <see cref="BridgeTelemetry"/>.
/// Transport-neutral: hosts pass in messages and a send function, which must be safe on any thread.
/// </summary>
internal sealed class BridgeSession : IDisposable
{
    /// <summary>How far into an oversized message its header is looked for, to answer it by its id.</summary>
    private const int HeaderSearchChars = 16 * 1024;

    /// <summary>How long a cancel for a call that has not arrived yet is remembered (spikes/react, "Cancellation").</summary>
    private static readonly TimeSpan EarlyCancelMemory = TimeSpan.FromSeconds(30);

    private static readonly Action<ILogger, string, string, long, string, Exception?> CommandFailed = LoggerMessage.Define<string, string, long, string>(
        LogLevel.Error, new EventId(1, "CommandFailed"), "Command {Contract}.{Command} (call {CallId}) failed; correlation {Correlation}");

    private static readonly Action<ILogger, string, string, long, int, int, Exception?> ResultTooLarge = LoggerMessage.Define<string, string, long, int, int>(
        LogLevel.Warning, new EventId(2, "ResultTooLarge"), "Command {Contract}.{Command} (call {CallId}) returned {Bytes} bytes, over the limit of {Limit}; the call failed");

    private readonly SparDispatcher _dispatcher;
    private readonly BridgeLimits _limits;
    private readonly ILogger _logger;
    private readonly string _fingerprint;
    private readonly string _doc;
    private readonly Action<string> _send;
    private readonly bool _includeDetails;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _closing = new();
    private readonly Dictionary<long, CancellationTokenSource> _pending = [];
    private readonly LinkedList<Waiting> _queue = [];
    private readonly BridgeTelemetry _telemetry;
    private readonly Dictionary<long, DateTimeOffset> _earlyCancels = [];
    private readonly List<Task> _running = [];
    private HashSet<string> _pageTypes = new(StringComparer.Ordinal);
    private int _inFlight;
    private long _queuedBytes;
    private bool _open;
    private bool _mismatch;
    private bool _closed;
    private long _lastId;

    public BridgeSession(SparDispatcher dispatcher, string fingerprint, string doc, Action<string> send, bool includeDetails, TimeProvider? time = null)
    {
        _dispatcher = dispatcher;
        _limits = dispatcher.Limits;
        _logger = dispatcher.Logger;
        _fingerprint = fingerprint;
        _doc = doc;
        _send = send;
        _includeDetails = includeDetails;
        _time = time ?? dispatcher.Time;
        _telemetry = new BridgeTelemetry(dispatcher, _time, doc);
    }

    /// <summary>Raised when the page reports <c>ui-ready</c>.</summary>
    public event Action? Ready;

    /// <summary>Raised when the page has finished its close work.</summary>
    public event Action? ClosingDone;

    /// <summary>Raised after the page's <c>hello</c> was answered.</summary>
    public event Action? Opened;

    /// <summary>
    /// Raised with the <c>seq</c> of each <c>pong</c>, the page's answer to a <see cref="Ping"/>, and whether its payload
    /// says the page shows a modal dialog of its own (<c>{"dialog":true}</c>, from the host-injected script).
    /// </summary>
    public event Action<long, bool>? Pong;

    /// <summary>
    /// Raised with the payload of each <c>csp</c> message: a batch of Content Security Policy violations from the
    /// host-injected script (plan section 3b). The payload is the page's, untrusted; the host parses it.
    /// </summary>
    public event Action<ReadOnlyMemory<byte>>? Violations;

    /// <summary>
    /// Raised with the payload of each <c>errors</c> message: a batch of the page's console errors, uncaught exceptions
    /// and unhandled rejections from the host-injected script. The payload is the page's, untrusted; the host parses it.
    /// </summary>
    public event Action<ReadOnlyMemory<byte>>? PageErrors;

    /// <summary>
    /// Raised with the <c>seq</c> and the payload of each <c>close-answer</c>, the page guards' answer to a
    /// <see cref="RequestClose"/>. The payload is the page's, untrusted; the host reads it with <c>PageCloseAnswer</c>.
    /// </summary>
    public event Action<long, ReadOnlyMemory<byte>>? CloseAnswered;

    /// <summary>Messages that could not be used, with why; for the host's log.</summary>
    public event Action<string>? Dropped;

    /// <summary>
    /// Whether the document has the host's injected script, which answers the heartbeat's <c>ping</c> (plan section 3,
    /// "Hung pages"): set by a host that adds it to every document. A session without it is never pinged, so the
    /// loopback host, whose pages have no injected script, never pings.
    /// </summary>
    public bool HostAnswersPing { get; init; }

    /// <summary>
    /// The host's own commands (contract <c>spar</c>, such as the SDK's <c>setColorScheme</c>): null for a session whose
    /// host has none, where they are unknown commands like any other name the app does not have.
    /// </summary>
    public HostCommands? HostCommands { get; init; }

    public void Receive(string message)
    {
        var received = _telemetry.Now();

        // Counting allocates nothing; decoding an oversized message would allocate three times its size.
        var bytes = Encoding.UTF8.GetByteCount(message);
        if (bytes > _limits.MaxMessageBytes)
        {
            RefuseTooLarge(message, bytes, received);
            return;
        }

        if (!BridgeCodec.TryDecode(message, out var decoded, out var error))
        {
            Refuse(error!, received);
            return;
        }

        switch (decoded!.Type)
        {
            case BridgeMessageType.Hello:
                Hello(decoded);
                break;
            case BridgeMessageType.Call:
                Call(decoded, received);
                break;
            case BridgeMessageType.Cancel:
                Cancel(decoded.Id!.Value);
                break;
            case BridgeMessageType.Stream:
                Refuse(decoded, "Streams and subscriptions arrive in M3.", received);
                break;
            case BridgeMessageType.Ready:
                Ready?.Invoke();
                break;
            case BridgeMessageType.ClosingDone:
                ClosingDone?.Invoke();
                break;
            case BridgeMessageType.Pong:
                Pong?.Invoke(decoded.Seq!.Value, ShowsDialog(decoded.Payload.Span));
                break;
            case BridgeMessageType.Csp:
                Violations?.Invoke(decoded.Payload);
                break;
            case BridgeMessageType.Errors:
                PageErrors?.Invoke(decoded.Payload);
                break;
            case BridgeMessageType.CloseAnswer:
                CloseAnswered?.Invoke(decoded.Seq!.Value, decoded.Payload);
                break;
            default:
                Unexpected(decoded);
                break;
        }
    }

    /// <summary>The page asks for its close work; it answers <c>closing-done</c>.</summary>
    public void RequestClosing() => Send(new BridgeMessage(BridgeMessageType.Closing));

    /// <summary>
    /// The heartbeat's ping (plan section 3, "Hung pages"); the host's injected script answers <c>pong</c> from its message
    /// handler. False, with nothing sent, when the document has no such script or is closed.
    /// </summary>
    public bool Ping(long seq)
    {
        lock (_gate)
        {
            if (_closed || !HostAnswersPing)
            {
                return false;
            }
        }

        Send(new BridgeMessage(BridgeMessageType.Ping) { Seq = seq });
        return true;
    }

    /// <summary>
    /// Asks the page's close guards (decision "Two-stage close"): <c>close-request</c> numbered <paramref name="seq"/>,
    /// with the payload <c>{"reason":…,"automated":…}</c>. False, with nothing sent, when the page did not list
    /// <c>close-request</c> in its <c>hello</c> (no SDK, or an older one) or the document is closed.
    /// </summary>
    public bool RequestClose(long seq, ReadOnlyMemory<byte> payload)
    {
        lock (_gate)
        {
            if (_closed || !_pageTypes.Contains("close-request"))
            {
                return false;
            }
        }

        Send(new BridgeMessage(BridgeMessageType.CloseRequest) { Seq = seq, Payload = payload });
        return true;
    }

    /// <summary>The close request <paramref name="seq"/> ended without the page's answer: its guards' signal is aborted.</summary>
    public void EndClose(long seq)
    {
        lock (_gate)
        {
            if (_closed || !_pageTypes.Contains("close-ended"))
            {
                return;
            }
        }

        Send(new BridgeMessage(BridgeMessageType.CloseEnded) { Seq = seq });
    }

    /// <summary>A host event (for example <c>backend-updated</c>), sent only when the page listed <c>event</c>.</summary>
    public void SendEvent(string name, ReadOnlyMemory<byte> payload)
    {
        lock (_gate)
        {
            if (!_pageTypes.Contains("event"))
            {
                return;
            }
        }

        Send(new BridgeMessage(BridgeMessageType.Event) { Name = name, Payload = payload });
    }

    /// <summary>The document is gone: cancel every call, drop the waiting ones, and send nothing more.</summary>
    public void Close()
    {
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            foreach (var pending in _pending.Values)
            {
                pending.Cancel();
            }

            _pending.Clear();
            _queue.Clear();
            _queuedBytes = 0;
        }

        _closing.Cancel();
        _telemetry.Flush();
    }

    /// <summary>Completes when every dispatched command has finished (tests use it; hosts do not need it).</summary>
    public Task WhenIdleAsync()
    {
        lock (_gate)
        {
            return Task.WhenAll(_running.ToArray());
        }
    }

    public void Dispose()
    {
        Close();
        _closing.Dispose();
    }

    /// <summary>
    /// Whether a <c>pong</c>'s payload is <c>{"dialog":true}</c>: the page shows a modal dialog of its own, so a close
    /// guard that has not answered yet is waiting for the person, not hung. Anything else, an empty payload included, is no.
    /// </summary>
    internal static bool ShowsDialog(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
        {
            return false;
        }

        try
        {
            var reader = new Utf8JsonReader(payload, new JsonReaderOptions { MaxDepth = 2 });
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var dialog = reader.ValueTextEquals("dialog"u8);
                reader.Read();
                if (dialog)
                {
                    return reader.TokenType == JsonTokenType.True;
                }

                reader.Skip();
            }
        }
        catch (JsonException)
        {
            // A page can post anything as a pong; a payload that is not JSON says nothing.
        }

        return false;
    }

    private void Hello(BridgeMessage hello)
    {
        lock (_gate)
        {
            _open = true;
            _pageTypes = new HashSet<string>(hello.Types ?? [], StringComparer.Ordinal);
            _mismatch = !string.Equals(hello.Contracts, _fingerprint, StringComparison.Ordinal);
        }

        var protocol = Math.Min(hello.Protocol!.Value, BridgeCodec.ProtocolVersion);
        Send(new BridgeMessage(BridgeMessageType.Open) { Protocol = protocol, Doc = _doc, Contracts = _fingerprint });
        Opened?.Invoke();
    }

    /// <summary>A call starts when a slot is free, waits in the queue when one is not, and is refused as busy past both.</summary>
    private void Call(BridgeMessage call, Received received)
    {
        var id = call.Id!.Value;
        SparFailure? refusal = null;
        lock (_gate)
        {
            // A closed document runs nothing more: its page is going away, and no reply could reach it.
            if (_closed)
            {
                return;
            }

            if (!TakeId(id))
            {
                Dropped?.Invoke($"call {id}: an id must be higher than every earlier one in the document");
                return;
            }

            if (!_open)
            {
                refusal = SparFailure.InvalidRequest("The page must send hello first.");
            }
            else if (_mismatch)
            {
                refusal = new SparFailure("rejected", "contract-mismatch", "notStarted", _fingerprint);
            }
            else if (TakeEarlyCancel(id))
            {
                refusal = new SparFailure("cancelled", "cancelled-before-start", "notStarted");
            }
            else if (_inFlight < _limits.CallsInFlight)
            {
                StartLocked(new Waiting(call, received));
            }
            else if (_queue.Count < _limits.QueuedCalls && _queuedBytes + call.Payload.Length <= _limits.QueuedBytes)
            {
                _queue.AddLast(new Waiting(call, received));
                _queuedBytes += call.Payload.Length;
            }
            else
            {
                refusal = SparFailure.Busy();
            }
        }

        if (refusal is not null)
        {
            RefuseAtTheDoor(call, id, refusal, received);
        }
    }

    /// <summary>A call refused before it was admitted: the page gets the failure, the telemetry counts it.</summary>
    private void RefuseAtTheDoor(BridgeMessage? call, long id, SparFailure refusal, Received received)
    {
        _telemetry.Ended(call, id, refusal, received, atTheDoor: true);
        Send(FailureMessage(id, refusal, call));
    }

    /// <summary>Takes a slot and runs the call; the slot is given back only when the command has really ended.</summary>
    private void StartLocked(Waiting waiting)
    {
        var token = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
        _pending[waiting.Call.Id!.Value] = token;
        _inFlight++;
        _running.Add(Task.Run(() => DispatchAsync(waiting.Call, waiting.Received, token)));
    }

    private async Task DispatchAsync(BridgeMessage call, Received received, CancellationTokenSource token)
    {
        var id = call.Id!.Value;
        using var activity = _telemetry.Started(call, received);
        SparOutcome outcome;
        try
        {
            outcome = HostCommands is { } host && HostCommands.IsHostContract(call.Contract!)
                ? await host.InvokeAsync(call.Command!, call.Payload, token.Token)
                : await _dispatcher.InvokeAsync(call.Contract!, call.Command!, id, call.Payload, token.Token);
        }
        finally
        {
            token.Dispose();
            lock (_gate)
            {
                _inFlight--;
                if (!_closed && _queue.First is { } next)
                {
                    _queue.RemoveFirst();
                    _queuedBytes -= next.Value.Call.Payload.Length;
                    StartLocked(next.Value);
                }
            }
        }

        outcome = Deliverable(call, outcome);
        _telemetry.Completed(activity, call, outcome, received);
        lock (_gate)
        {
            // A call that was failed, or a document that went away, gets no late reply.
            if (!_pending.Remove(id) || _closed)
            {
                return;
            }
        }

        Send(OutcomeMessage(call, outcome, activity));
    }

    /// <summary>The outcome as the page will get it: a result over the limit becomes the failure of its own call.</summary>
    private SparOutcome Deliverable(BridgeMessage call, SparOutcome outcome)
    {
        if (outcome.Kind == SparOutcomeKind.Failure || outcome.Payload.Length <= _limits.MaxResultBytes)
        {
            return outcome;
        }

        ResultTooLarge(_logger, call.Contract!, call.Command!, call.Id!.Value, outcome.Payload.Length, _limits.MaxResultBytes, null);
        return SparOutcome.FromFailure(SparFailure.ResultTooLarge(outcome.Payload.Length, _limits.MaxResultBytes));
    }

    private void Cancel(long id)
    {
        Waiting? waiting = null;
        lock (_gate)
        {
            if (_pending.TryGetValue(id, out var token))
            {
                token.Cancel();
                return;
            }

            if (RemoveQueued(id) is { } queued)
            {
                waiting = queued;
            }
            else if (id > _lastId)
            {
                // The cancel overtook its call (another connection, on loopback): the call will never run.
                _earlyCancels[id] = _time.GetUtcNow();
            }
        }

        if (waiting is { } ended)
        {
            var cancelled = new SparFailure("cancelled", "cancelled-before-start", "notStarted");
            _telemetry.Ended(ended.Call, id, cancelled, ended.Received, atTheDoor: false);
            Send(FailureMessage(id, cancelled, ended.Call));
        }
    }

    private Waiting? RemoveQueued(long id)
    {
        for (var node = _queue.First; node is not null; node = node.Next)
        {
            if (node.Value.Call.Id == id)
            {
                _queue.Remove(node);
                _queuedBytes -= node.Value.Call.Payload.Length;
                return node.Value;
            }
        }

        return null;
    }

    /// <summary>A message of a type this host does not take: it fails a pending call with that id, so none can hang.</summary>
    private void Unexpected(BridgeMessage message)
    {
        if (message.Id is { } id && FailPending(id, new SparFailure("protocol", "malformed", "unknown", $"unexpected '{message.TypeName}' message")))
        {
            return;
        }

        Dropped?.Invoke($"'{message.TypeName}' message ignored");
    }

    /// <summary>
    /// A message over the size limit is never decoded whole: only its header, when it sits within the first
    /// characters, is read to answer the call by its id. One that names no fresh call id gets no reply.
    /// </summary>
    private void RefuseTooLarge(string message, int bytes, Received received)
    {
        var tab = message.AsSpan(0, Math.Min(message.Length, HeaderSearchChars)).IndexOf('\t');
        if (tab < 0 || !BridgeCodec.TryDecode(string.Concat(message.AsSpan(0, tab), "\t"), out var header, out _) || header!.Type != BridgeMessageType.Call)
        {
            Dropped?.Invoke($"a message of {bytes} bytes, over the limit of {_limits.MaxMessageBytes}, with no call header");
            return;
        }

        var id = header.Id!.Value;
        bool fresh;
        lock (_gate)
        {
            fresh = TakeId(id);
        }

        if (!fresh)
        {
            Dropped?.Invoke($"a message of {bytes} bytes for id {id}, which was already used");
            return;
        }

        RefuseAtTheDoor(header, id, SparFailure.TooLarge(bytes, _limits.MaxMessageBytes), received);
    }

    private void Refuse(BridgeDecodeError error, Received received)
    {
        if (error.Id is not { } id)
        {
            Dropped?.Invoke(error.Reason);
            return;
        }

        if (FailPending(id, new SparFailure("protocol", "malformed", "unknown", error.Reason)))
        {
            return;
        }

        bool fresh;
        lock (_gate)
        {
            fresh = TakeId(id);
        }

        if (fresh)
        {
            RefuseAtTheDoor(null, id, SparFailure.InvalidRequest(error.Reason), received);
        }
        else
        {
            Dropped?.Invoke($"{error.Reason} (id {id} was already used)");
        }
    }

    private void Refuse(BridgeMessage message, string reason, Received received)
    {
        var id = message.Id!.Value;
        bool fresh;
        lock (_gate)
        {
            fresh = TakeId(id);
        }

        if (fresh)
        {
            RefuseAtTheDoor(message, id, SparFailure.InvalidRequest(reason), received);
        }
    }

    /// <summary>Fails a running or waiting call; a running one keeps its slot until its command ends.</summary>
    private bool FailPending(long id, SparFailure failure)
    {
        Waiting? waiting = null;
        lock (_gate)
        {
            if (_pending.Remove(id, out var token))
            {
                token.Cancel();
            }
            else if (RemoveQueued(id) is { } queued)
            {
                waiting = queued;
            }
            else
            {
                return false;
            }
        }

        if (waiting is { } ended)
        {
            _telemetry.Ended(ended.Call, id, failure, ended.Received, atTheDoor: false);
        }

        Send(FailureMessage(id, failure, waiting?.Call));
        return true;
    }

    /// <summary>Ids increase within a document; a repeated or lower one cannot be answered without confusing the page.</summary>
    private bool TakeId(long id)
    {
        if (id <= _lastId)
        {
            return false;
        }

        _lastId = id;
        return true;
    }

    private bool TakeEarlyCancel(long id)
    {
        var now = _time.GetUtcNow();
        foreach (var expired in _earlyCancels.Where(entry => now - entry.Value > EarlyCancelMemory).Select(entry => entry.Key).ToList())
        {
            _earlyCancels.Remove(expired);
        }

        return _earlyCancels.Remove(id);
    }

    private BridgeMessage OutcomeMessage(BridgeMessage call, SparOutcome outcome, Activity? activity)
    {
        var id = call.Id!.Value;
        return outcome.Kind switch
        {
            SparOutcomeKind.Value => new BridgeMessage(BridgeMessageType.Result) { Id = id, Ok = true, Payload = outcome.Payload },
            SparOutcomeKind.Error => new BridgeMessage(BridgeMessageType.Result) { Id = id, Ok = false, Payload = outcome.Payload },
            _ => FailureMessage(id, outcome.Failure!, call, activity),
        };
    }

    /// <summary>
    /// A failure as the page sees it. One with an exception is logged under the call's correlation id, which the
    /// payload carries in every build; the detail goes with it only in Development.
    /// </summary>
    private BridgeMessage FailureMessage(long id, SparFailure failure, BridgeMessage? call, Activity? activity = null)
    {
        string? correlation = null;
        if (failure.Exception is { } exception)
        {
            correlation = activity?.TraceId.ToHexString() ?? Correlation(call?.TraceParent);
            CommandFailed(_logger, call?.Contract ?? "", call?.Command ?? "", id, correlation, exception);
        }

        var detail = _includeDetails ? failure.Detail : null;
        return new BridgeMessage(BridgeMessageType.Failure)
        {
            Id = id,
            Kind = failure.Kind,
            Code = failure.Code,
            Execution = failure.Execution,
            Payload = correlation is null && detail is null ? ReadOnlyMemory<byte>.Empty : FailurePayload(correlation, detail),
        };
    }

    /// <summary>
    /// Without an activity (nothing listens): the trace id of the call's <c>traceparent</c> (decision "Bridge limits …
    /// the behaviour"), else a new one. With one, the activity's trace id is used instead, so the page, the log and the
    /// trace name one id.
    /// </summary>
    private static string Correlation(string? traceParent)
    {
        if (traceParent is { Length: >= 55 } && ActivityContext.TryParse(traceParent, null, out var context))
        {
            return context.TraceId.ToHexString();
        }

        return ActivityTraceId.CreateRandom().ToHexString();
    }

    private static byte[] FailurePayload(string? correlation, string? detail)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            if (correlation is not null)
            {
                writer.WriteString("correlation"u8, correlation);
            }

            if (detail is not null)
            {
                writer.WriteString("detail"u8, detail);
            }

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private void Send(BridgeMessage message)
    {
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }
        }

        _send(Encoding.UTF8.GetString(BridgeCodec.Encode(message)));
    }

    /// <summary>A call waiting for a slot, with when it arrived.</summary>
    private readonly record struct Waiting(BridgeMessage Call, Received Received);
}
