using System.Text;
using System.Text.Json;
using Spar.Runtime;

namespace Spar.Bridge;

/// <summary>
/// The bridge for one document (plan section 2c): hello and open, the fingerprint checked once, increasing ids,
/// commands dispatched off the caller's thread, cancels (also one that overtakes its call), exactly one terminal
/// message per call, and nothing sent after <see cref="Close"/>. Transport-neutral: hosts pass in messages and a send
/// function, which must be safe to call from any thread.
/// </summary>
internal sealed class BridgeSession : IDisposable
{
    /// <summary>How long a cancel for a call that has not arrived yet is remembered (spikes/react, "Cancellation").</summary>
    private static readonly TimeSpan EarlyCancelMemory = TimeSpan.FromSeconds(30);

    private readonly SparDispatcher _dispatcher;
    private readonly string _fingerprint;
    private readonly string _doc;
    private readonly Action<string> _send;
    private readonly bool _includeDetails;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _closing = new();
    private readonly Dictionary<long, CancellationTokenSource> _pending = [];
    private readonly Dictionary<long, DateTimeOffset> _earlyCancels = [];
    private readonly List<Task> _running = [];
    private HashSet<string> _pageTypes = new(StringComparer.Ordinal);
    private bool _open;
    private bool _mismatch;
    private bool _closed;
    private long _lastId;

    public BridgeSession(SparDispatcher dispatcher, string fingerprint, string doc, Action<string> send, bool includeDetails, TimeProvider? time = null)
    {
        _dispatcher = dispatcher;
        _fingerprint = fingerprint;
        _doc = doc;
        _send = send;
        _includeDetails = includeDetails;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Raised when the page reports <c>ui-ready</c>.</summary>
    public event Action? Ready;

    /// <summary>Raised when the page has finished its close work.</summary>
    public event Action? ClosingDone;

    /// <summary>Messages that could not be used, with why; for the host's log.</summary>
    public event Action<string>? Dropped;

    public void Receive(string message)
    {
        if (!BridgeCodec.TryDecode(message, out var decoded, out var error))
        {
            Refuse(error!);
            return;
        }

        switch (decoded!.Type)
        {
            case BridgeMessageType.Hello:
                Hello(decoded);
                break;
            case BridgeMessageType.Call:
                Call(decoded);
                break;
            case BridgeMessageType.Cancel:
                Cancel(decoded.Id!.Value);
                break;
            case BridgeMessageType.Stream:
                Refuse(decoded, "Streams and subscriptions arrive in M3.");
                break;
            case BridgeMessageType.Ready:
                Ready?.Invoke();
                break;
            case BridgeMessageType.ClosingDone:
                ClosingDone?.Invoke();
                break;
            default:
                Unexpected(decoded);
                break;
        }
    }

    /// <summary>The page asks for its close work; it answers <c>closing-done</c>.</summary>
    public void RequestClosing() => Send(new BridgeMessage(BridgeMessageType.Closing));

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

    /// <summary>The document is gone: cancel every call, and send nothing more.</summary>
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
        }

        _closing.Cancel();
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
    }

    private void Call(BridgeMessage call)
    {
        var id = call.Id!.Value;
        CancellationTokenSource? token = null;
        SparFailure? refusal = null;
        lock (_gate)
        {
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
            else
            {
                token = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
                _pending[id] = token;
                _running.Add(Task.Run(() => DispatchAsync(call, token)));
            }
        }

        if (refusal is not null)
        {
            Send(FailureMessage(id, refusal));
        }
    }

    private async Task DispatchAsync(BridgeMessage call, CancellationTokenSource token)
    {
        var id = call.Id!.Value;
        SparOutcome outcome;
        try
        {
            outcome = await _dispatcher.InvokeAsync(call.Contract!, call.Command!, id, call.Payload, token.Token);
        }
        finally
        {
            token.Dispose();
        }

        lock (_gate)
        {
            // A call that was failed, or a document that went away, gets no late reply.
            if (!_pending.Remove(id) || _closed)
            {
                return;
            }
        }

        Send(OutcomeMessage(id, outcome));
    }

    private void Cancel(long id)
    {
        lock (_gate)
        {
            if (_pending.TryGetValue(id, out var token))
            {
                token.Cancel();
                return;
            }

            if (id > _lastId)
            {
                // The cancel overtook its call (another connection, on loopback): the call will never run.
                _earlyCancels[id] = _time.GetUtcNow();
            }
        }
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

    private void Refuse(BridgeDecodeError error)
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
            Send(FailureMessage(id, SparFailure.InvalidRequest(error.Reason)));
        }
        else
        {
            Dropped?.Invoke($"{error.Reason} (id {id} was already used)");
        }
    }

    private void Refuse(BridgeMessage message, string reason)
    {
        var id = message.Id!.Value;
        bool fresh;
        lock (_gate)
        {
            fresh = TakeId(id);
        }

        if (fresh)
        {
            Send(FailureMessage(id, SparFailure.InvalidRequest(reason)));
        }
    }

    private bool FailPending(long id, SparFailure failure)
    {
        lock (_gate)
        {
            if (!_pending.Remove(id, out var token))
            {
                return false;
            }

            token.Cancel();
        }

        Send(FailureMessage(id, failure));
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

    private BridgeMessage OutcomeMessage(long id, SparOutcome outcome) => outcome.Kind switch
    {
        SparOutcomeKind.Value => new BridgeMessage(BridgeMessageType.Result) { Id = id, Ok = true, Payload = outcome.Payload },
        SparOutcomeKind.Error => new BridgeMessage(BridgeMessageType.Result) { Id = id, Ok = false, Payload = outcome.Payload },
        _ => FailureMessage(id, outcome.Failure!),
    };

    private BridgeMessage FailureMessage(long id, SparFailure failure) => new(BridgeMessageType.Failure)
    {
        Id = id,
        Kind = failure.Kind,
        Code = failure.Code,
        Execution = failure.Execution,
        Payload = _includeDetails && failure.Detail is { } detail ? DetailJson(detail) : ReadOnlyMemory<byte>.Empty,
    };

    private static byte[] DetailJson(string detail)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("detail"u8, detail);
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
}
