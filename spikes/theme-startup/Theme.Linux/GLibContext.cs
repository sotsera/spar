using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Theme.Linux;

/// <summary>
/// Single-threaded synchronization context backed by the GLib main context: posted work is queued and drained by one
/// idle source at default priority, so async continuations stay on the GTK thread. g_idle_add_full is thread-safe and
/// wakes the main loop; at most one drain source is pending at a time.
/// </summary>
internal sealed class GLibContext : SynchronizationContext, IDisposable
{
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();
    private readonly int _threadId = Environment.CurrentManagedThreadId;
    private readonly GCHandle _self;
    private int _scheduled;

    public GLibContext()
    {
        _self = GCHandle.Alloc(this);
    }

    public long Drains { get; private set; }

    public bool IsOwnerThread => Environment.CurrentManagedThreadId == _threadId;

    public override unsafe void Post(SendOrPostCallback d, object? state)
    {
        _queue.Enqueue((d, state));
        if (Interlocked.Exchange(ref _scheduled, 1) == 0)
        {
            GLib.g_idle_add_full(GLib.PriorityDefault, &Drain, GCHandle.ToIntPtr(_self), 0);
        }
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        if (!IsOwnerThread)
        {
            throw new NotSupportedException("Synchronous cross-thread Send is not supported.");
        }

        d(state);
    }

    public void Dispose() => _self.Free();

    [UnmanagedCallersOnly]
    private static int Drain(nint data)
    {
        var context = (GLibContext)GCHandle.FromIntPtr(data).Target!;

        // Clear first: work posted while draining schedules another source instead of being lost.
        Volatile.Write(ref context._scheduled, 0);
        context.Drains++;
        while (context._queue.TryDequeue(out var item))
        {
            try
            {
                item.Callback(item.State);
            }
            catch (Exception exception)
            {
                // An exception must never unwind into GLib.
                Console.Error.WriteLine($"SPAR_UI_EXCEPTION {exception}");
            }
        }

        return 0; // G_SOURCE_REMOVE
    }
}
