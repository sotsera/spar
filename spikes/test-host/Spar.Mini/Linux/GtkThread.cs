using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Linux.Desktop;

namespace Spar.Linux;

/// <summary>
/// The thread that initialised GTK and runs the GLib main loop, with a SynchronizationContext drained by an idle
/// source. GTK is initialised once per process, so a test process has exactly one, shared by all its windows.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class GtkThread : SynchronizationContext
{
    private static readonly Lazy<GtkThread> SharedThread = new(() => Start("Spar UI (tests)"));

    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private GCHandle _self;
    private nint _loop;
    private int _scheduled;

    public static GtkThread Shared => SharedThread.Value;

    public Thread Thread { get; private set; } = null!;

    public static GtkThread Start(string name)
    {
        var gtk = new GtkThread();
        gtk._self = GCHandle.Alloc(gtk);
        gtk.Thread = new Thread(gtk.Loop) { Name = name, IsBackground = true };
        gtk.Thread.Start();
        gtk._ready.Task.GetAwaiter().GetResult();
        return gtk;
    }

    public override unsafe void Post(SendOrPostCallback d, object? state)
    {
        _queue.Enqueue((d, state));
        if (Interlocked.Exchange(ref _scheduled, 1) == 0)
        {
            GLib.g_idle_add_full(GLib.PriorityDefault, &Drain, GCHandle.ToIntPtr(_self), 0);
        }
    }

    public override void Send(SendOrPostCallback d, object? state) =>
        throw new NotSupportedException("Never block on the UI thread.");

    public Task<T> InvokeAsync<T>(Func<Task<T>> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(
            async _ =>
            {
                try
                {
                    completion.TrySetResult(await work());
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            },
            null);
        return completion.Task;
    }

    public Task InvokeAsync(Action work) => InvokeAsync(() =>
    {
        work();
        return Task.FromResult(true);
    });

    public void Quit() => Post(_ => GLib.g_main_loop_quit(_loop), null);

    private void Loop()
    {
        if (Gtk.gtk_init_check() == 0)
        {
            _ready.SetException(new InvalidOperationException("gtk_init_check failed: no display?"));
            return;
        }

        SetSynchronizationContext(this);
        _loop = GLib.g_main_loop_new(0, 0);
        _ready.SetResult();
        GLib.g_main_loop_run(_loop);
        GLib.g_main_loop_unref(_loop);
    }

    [UnmanagedCallersOnly]
    private static int Drain(nint data)
    {
        var gtk = (GtkThread)GCHandle.FromIntPtr(data).Target!;

        // Clear first: work posted while draining schedules another source instead of being lost.
        Volatile.Write(ref gtk._scheduled, 0);
        while (gtk._queue.TryDequeue(out var item))
        {
            item.Callback(item.State);
        }

        return 0;
    }
}
