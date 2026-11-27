using Spar.Runtime;

namespace Spar.Bridge;

/// <summary>
/// Calls of a serial group (decision "M3b's decide-first questions", item 1): one waits for its group's turn in the
/// dispatcher, app-wide, holding no call slot; while it waits it counts in the document's queue (<c>QueuedCalls</c>,
/// <c>QueuedBytes</c>), and a cancel meanwhile ends it <c>cancelled-before-start</c>.
/// </summary>
internal sealed partial class BridgeSession
{
    /// <summary>The serial calls of this document still waiting for their group's turn.</summary>
    private int _waitingSerial;

    private bool IsSerial(BridgeMessage call) =>
        !(HostCommands is not null && HostCommands.IsHostContract(call.Contract!))
        && Forbidden(call) is null
        && _dispatcher.Find(call.Contract!, call.Command!)?.SerialGroup is not null;

    /// <summary>
    /// The refusal of a call or stream outside the window's allow-list (decision "Per-window command allow-lists"), or
    /// null; Spar's own page commands are never limited. A refused serial call never takes a place in its group.
    /// </summary>
    private SparFailure? Forbidden(BridgeMessage message) =>
        !HostCommands.IsHostContract(message.Contract!) && Window?.Access is { } access && !access.Allows(message.Contract!, message.Command!)
            ? SparFailure.ForbiddenCommand(access.Window, message.Contract!, message.Command!)
            : null;

    /// <summary>Admits a serial call when the document's queue has room for it; false when it is full.</summary>
    private bool TryStartSerialLocked(Waiting waiting)
    {
        var bytes = waiting.Call.Payload.Length;
        if (_queue.Count + _waitingSerial >= _limits.QueuedCalls || _queuedBytes + bytes > _limits.QueuedBytes)
        {
            return false;
        }

        _waitingSerial++;
        _queuedBytes += bytes;
        var token = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
        _pending[waiting.Call.Id!.Value] = token;

        // The call takes its place in its group now, on the reader, so the group runs its calls in arrival order; the
        // task that waits for the turn may start in any order.
        var group = _dispatcher.Find(waiting.Call.Contract!, waiting.Call.Command!)!.SerialGroup!;
        var place = _dispatcher.Serial.EnterAsync(group, token.Token);
        _running.Add(Task.Run(() => DispatchSerialAsync(waiting.Call, waiting.Received, token, place)));
        return true;
    }

    private async Task DispatchSerialAsync(BridgeMessage call, Received received, CancellationTokenSource token, ValueTask<SerialGroups.Turn> place)
    {
        using var activity = _telemetry.Started(call, received);
        var waiting = true;
        void Running()
        {
            lock (_gate)
            {
                if (waiting)
                {
                    waiting = false;
                    _waitingSerial--;
                    _queuedBytes -= call.Payload.Length;
                }
            }
        }

        SparOutcome outcome;
        try
        {
            outcome = await _dispatcher.InvokeAsync(call.Contract!, call.Command!, call.Id!.Value, call.Payload, Running, _blobs, token.Token, place);
        }
        finally
        {
            // A call that never ran (cancelled while it waited, or refused) leaves the queue here.
            Running();
            token.Dispose();
        }

        Deliver(call, outcome, received, activity);
    }
}
