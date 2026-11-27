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
        && _dispatcher.Find(call.Contract!, call.Command!)?.SerialGroup is not null;

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
        _running.Add(Task.Run(() => DispatchSerialAsync(waiting.Call, waiting.Received, token)));
        return true;
    }

    private async Task DispatchSerialAsync(BridgeMessage call, Received received, CancellationTokenSource token)
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
            outcome = await _dispatcher.InvokeAsync(call.Contract!, call.Command!, call.Id!.Value, call.Payload, Running, token.Token);
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
