using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Integ.Linux;

/// <summary>
/// Synchronization context for the GTK thread: posted callbacks are queued and run by one GLib idle source, so async
/// continuations come back to the thread that owns GTK, WebKit and the D-Bus objects. g_idle_add_full is thread-safe.
/// </summary>
internal sealed class GLibContext : SynchronizationContext, IDisposable
{
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();
    private readonly GCHandle _self;
    private int _scheduled;

    public GLibContext()
    {
        _self = GCHandle.Alloc(this);
    }

    public override unsafe void Post(SendOrPostCallback d, object? state)
    {
        _queue.Enqueue((d, state));
        if (Interlocked.Exchange(ref _scheduled, 1) == 0)
        {
            GLib.g_idle_add_full(0, &Drain, GCHandle.ToIntPtr(_self), 0);
        }
    }

    public override void Send(SendOrPostCallback d, object? state) =>
        throw new NotSupportedException("Synchronous Send is not supported on the GTK thread.");

    public void Dispose() => _self.Free();

    [UnmanagedCallersOnly]
    private static int Drain(nint data)
    {
        var context = (GLibContext)GCHandle.FromIntPtr(data).Target!;
        Volatile.Write(ref context._scheduled, 0);
        while (context._queue.TryDequeue(out var item))
        {
            try
            {
                item.Callback(item.State);
            }
            catch (Exception exception)
            {
                // Never unwind into GLib.
                Log.Mark("ui-exception " + exception);
            }
        }

        return 0;
    }
}
