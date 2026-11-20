using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Spar.Hosting;
using Spar.Linux.Interop;

namespace Spar.Linux;

/// <summary>
/// The window's UI thread on Linux (plan section 3, "Threading"): one dedicated thread that initialises GTK, owns every
/// GTK and WebKit object and runs a <c>GMainLoop</c> on the default main context until <see cref="Quit"/>. It is also the
/// synchronization context of its own thread, so an <c>await</c> there continues there; work posted from other threads
/// goes through <c>g_idle_add_full</c>, one source per burst. Nothing slow runs on it: callers post short work only, and
/// each piece runs in a named <see cref="Enter"/> scope for the stall probe.
/// </summary>
internal sealed class MainLoop : SynchronizationContext
{
    /// <summary>The name of posted work that gives none, and of <c>await</c> continuations.</summary>
    private const string PostedWork = "posted work";

    private const string AwaitContinuation = "await continuation";

    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State, string Name)> _queue = new();
    private readonly Action<Exception> _report;
    private int _threadId;
    private int _drainPosted;
    private nint _loop;
    private nint _self;
    private IUiThreadWatch? _probe;

    private MainLoop(Action<Exception> report) => _report = report;

    /// <summary>Whether the caller is on this thread.</summary>
    public bool IsCurrent => Environment.CurrentManagedThreadId == _threadId;

    /// <summary>
    /// Runs <paramref name="start"/> on a new thread, then its main loop, and returns when the loop ends
    /// (<see cref="Quit"/>). <paramref name="start"/> initialises GTK on that thread; when it returns false, the loop never
    /// runs.
    /// </summary>
    public static void Run(Action<Exception> report, Func<MainLoop, bool> start)
    {
        var loop = new MainLoop(report);
        var thread = new Thread(() => loop.Loop(start)) { Name = "Spar UI", IsBackground = false };
        thread.Start();
        thread.Join();
    }

    /// <summary>
    /// A test process's one loop thread (plan section 5: GTK is initialised once, and every test window shares it): runs
    /// <paramref name="start"/> on a new background thread, then its main loop for the rest of the process, and returns
    /// once <paramref name="start"/> has; null when it returned false (the loop then never runs).
    /// </summary>
    public static MainLoop? StartShared(Action<Exception> report, Func<MainLoop, bool> start)
    {
        var loop = new MainLoop(report);
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => loop.Loop(ui =>
        {
            try
            {
                var ready = start(ui);
                started.TrySetResult(ready);
                return ready;
            }
            catch (Exception exception)
            {
                started.TrySetException(exception);
                throw;
            }
        })) { Name = "Spar UI (tests)", IsBackground = true };
        thread.Start();

        // The caller is the test host's own thread for the app, never the loop's, so waiting here blocks nothing else.
        return started.Task.GetAwaiter().GetResult() ? loop : null;
    }

    /// <summary>Runs <paramref name="work"/> on the loop, after what is already queued. Safe from any thread.</summary>
    public void Post(Action work, string name = PostedWork) => Enqueue(static state => ((Action)state!)(), work, name);

    public override void Post(SendOrPostCallback d, object? state) => Enqueue(d, state, AwaitContinuation);

    public override void Send(SendOrPostCallback d, object? state)
    {
        // A synchronous call from another thread would block it on this one; nothing in Spar needs it.
        if (!IsCurrent)
        {
            throw new NotSupportedException("Work is posted to the window's UI thread, never sent synchronously.");
        }

        d(state);
    }

    /// <summary>Ends the loop once the current callback returns. Safe from any thread.</summary>
    public void Quit()
    {
        if (_loop != 0)
        {
            GLib.g_main_loop_quit(_loop);
        }
    }

    /// <summary>Starts the stall probe on this thread (loop thread). It stops when the loop ends.</summary>
    public void UseProbe(IUiThreadWatch probe)
    {
        _probe = probe;
        probe.Start(work => Post(work, "stall probe"));
    }

    /// <summary>A Spar callback on this thread, named for the stall probe; dispose it when the callback ends.</summary>
    public CallbackScope Enter(string name) => _probe is { } probe ? new CallbackScope(probe, name) : default;

    /// <summary>Reports an exception that a native callback caught, so nothing unwinds into GLib.</summary>
    public void Report(Exception exception) => _report(exception);

    private void Loop(Func<MainLoop, bool> start)
    {
        _threadId = Environment.CurrentManagedThreadId;
        SetSynchronizationContext(this);
        var self = GCHandle.Alloc(this);
        try
        {
            _self = GCHandle.ToIntPtr(self);
            _loop = GLib.g_main_loop_new(0, 0);
            if (start(this))
            {
                GLib.g_main_loop_run(_loop);
            }
        }
        catch (Exception exception)
        {
            _report(exception);
        }
        finally
        {
            _probe?.Stop();
            if (_loop != 0)
            {
                GLib.g_main_loop_unref(_loop);
            }

            // Sources still queued never run: nothing iterates the default context once this thread ends.
            self.Free();
        }
    }

    private unsafe void Enqueue(SendOrPostCallback d, object? state, string name)
    {
        _queue.Enqueue((d, state, name));

        // One idle source per burst: the drain empties the queue, and the next post after it asks again.
        if (Interlocked.Exchange(ref _drainPosted, 1) == 0)
        {
            GLib.g_idle_add_full(GLib.PriorityDefault, &DrainSource, _self, 0);
        }
    }

    private void Drain()
    {
        Volatile.Write(ref _drainPosted, 0);
        while (_queue.TryDequeue(out var item))
        {
            try
            {
                using var scope = Enter(item.Name);
                item.Callback(item.State);
            }
            catch (Exception exception)
            {
                _report(exception);
            }
        }
    }

    [UnmanagedCallersOnly]
    private static int DrainSource(nint data)
    {
        try
        {
            if (GCHandle.FromIntPtr(data).Target is MainLoop loop)
            {
                loop.Drain();
            }
        }
        catch (Exception exception)
        {
            // Nothing may unwind into GLib; Drain reports its own work's failures, so this is the handle itself.
            Console.Error.WriteLine("spar fail: the UI thread's queue failed: " + exception.Message);
        }

        return GLib.SourceRemove;
    }
}
