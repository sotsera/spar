using System.Diagnostics;
using Spar.Runtime;

namespace Spar.Bridge;

/// <summary>
/// Streams and subscriptions (plan section 2c; decision "M3a's decide-first questions"): a <c>stream</c> message opens
/// one with the page's first credits, <c>credits</c> grants more, <c>cancel</c> stops it like a call, and it ends with
/// exactly one <c>end</c> or <c>failure</c> after its last <c>items</c>. They do not take the call slots: a document
/// keeps at most <see cref="BridgeLimits.StreamsOpen"/> open, and one more is refused as busy.
/// </summary>
internal sealed partial class BridgeSession
{
    private void OpenStream(BridgeMessage message, Received received)
    {
        var id = message.Id!.Value;
        var entry = _dispatcher.Find(message.Contract!, message.Command!);
        SparFailure? refusal = null;
        BridgeStream? stream = null;
        CancellationTokenSource? token = null;
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            if (!TakeId(id))
            {
                Dropped?.Invoke($"stream {id}: an id must be higher than every earlier one in the document");
                return;
            }

            refusal = !_open ? SparFailure.InvalidRequest("The page must send hello first.")
                : _mismatch ? new SparFailure("rejected", "contract-mismatch", "notStarted", _fingerprint)
                : entry is null ? SparFailure.UnknownCommand(message.Contract!, message.Command!)
                : !entry.IsStream ? SparFailure.InvalidRequest($"{message.Contract}.{message.Command} is a unary command, called with a call message.")
                : TakeEarlyCancel(id) ? new SparFailure("cancelled", "cancelled-before-start", "notStarted")
                : _streams.Count >= _limits.StreamsOpen ? SparFailure.Busy()
                : null;
            if (refusal is null)
            {
                stream = new BridgeStream(
                    id, message.Credits!.Value, entry!.Capacity, entry.LatestOnly, _limits.MaxResultBytes, Send, _time);
                token = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
                _streams[id] = stream;
                _pending[id] = token;
                _running.Add(Task.Run(() => DispatchStreamAsync(message, received, stream, token)));
            }
        }

        if (refusal is not null)
        {
            _telemetry.Ended(message, id, refusal, received, atTheDoor: true);
            Send(FailureMessage(id, refusal, message) with { ItemsSent = 0 });
        }
    }

    private async Task DispatchStreamAsync(BridgeMessage message, Received received, BridgeStream stream, CancellationTokenSource token)
    {
        var id = message.Id!.Value;
        using var activity = _telemetry.Started(message, received);
        SparOutcome outcome;
        try
        {
            outcome = await _dispatcher.InvokeStreamAsync(message.Contract!, message.Command!, id, message.Payload, stream, token.Token);
        }
        finally
        {
            token.Dispose();
        }

        _telemetry.Completed(activity, message, outcome, received);
        bool deliver;
        lock (_gate)
        {
            // A stream that was failed, or a document that went away, gets no late end.
            deliver = _pending.Remove(id) && !_closed;
            _streams.Remove(id);
        }

        // Outside the gate: the stream sends under its own lock, and its send takes the gate.
        if (deliver)
        {
            stream.End(sent => StreamEnd(message, outcome, sent, activity));
        }

        stream.Dispose();
    }

    /// <summary>The page took items and grants more; credits for a stream that has ended are dropped.</summary>
    private void Credit(long id, int credits)
    {
        BridgeStream? stream;
        lock (_gate)
        {
            _streams.TryGetValue(id, out stream);
        }

        stream?.Grant(credits);
    }

    /// <summary>The terminal message: <c>end</c> with <c>ok</c>, the declared error in-band, or a failure with <c>itemsSent</c>.</summary>
    private BridgeMessage StreamEnd(BridgeMessage message, SparOutcome outcome, long sent, Activity? activity)
    {
        var id = message.Id!.Value;
        return outcome.Kind switch
        {
            SparOutcomeKind.Value => new BridgeMessage(BridgeMessageType.End) { Id = id, Ok = true },
            SparOutcomeKind.Error => new BridgeMessage(BridgeMessageType.End) { Id = id, Ok = false, Payload = outcome.Payload },
            _ => FailureMessage(id, outcome.Failure!, message, activity) with { ItemsSent = sent },
        };
    }
}
