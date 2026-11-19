using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Spar.WindowAutomation.Linux.Native;

namespace Spar.WindowAutomation.Linux;

/// <summary>
/// The GTK thread: one dedicated thread runs <c>gtk_init_check</c> and a <c>GMainLoop</c>; work is posted to it through
/// one idle source per batch. Test code runs on pool threads and only posts short work here (never a wait).
/// </summary>
internal static class Ui
{
    private static readonly ConcurrentQueue<Action> s_queue = new();
    private static readonly TaskCompletionSource s_started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Thread? s_thread;
    private static nint s_loop;
    private static int s_scheduled;

    public static bool IsUiThread => Thread.CurrentThread == s_thread;

    public static SynchronizationContext Context { get; } = new GLibContext();

    public static void Start()
    {
        s_thread = new Thread(Run) { Name = "Spar UI", IsBackground = true };
        s_thread.Start();
        s_started.Task.GetAwaiter().GetResult();
    }

    /// <summary>Quits the loop after iterating the main context until WebKit's child processes have had time to exit.</summary>
    public static void Stop()
    {
        Post(() => GLib.g_main_loop_quit(s_loop));
        s_thread?.Join(TimeSpan.FromSeconds(10));
    }

    public static unsafe void Post(Action action)
    {
        s_queue.Enqueue(action);
        if (Interlocked.Exchange(ref s_scheduled, 1) == 0)
        {
            GLib.g_idle_add_full(0, &Drain, 0, 0);
        }
    }

    public static Task Invoke(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try
            {
                action();
                done.SetResult();
            }
            catch (Exception exception)
            {
                done.SetException(exception);
            }
        });
        return done.Task;
    }

    public static Task<T> Invoke<T>(Func<T> function)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try
            {
                done.SetResult(function());
            }
            catch (Exception exception)
            {
                done.SetException(exception);
            }
        });
        return done.Task;
    }

    /// <summary>Runs an async function whose awaits continue on the GTK thread (GLib context).</summary>
    public static Task<T> InvokeAsync<T>(Func<Task<T>> function)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(async () =>
        {
            try
            {
                done.SetResult(await function());
            }
            catch (Exception exception)
            {
                done.SetException(exception);
            }
        });
        return done.Task;
    }

    private static void Run()
    {
        SynchronizationContext.SetSynchronizationContext(Context);
        if (Gtk.gtk_init_check() == 0)
        {
            s_started.SetException(new InvalidOperationException("gtk_init_check failed (no display?)"));
            return;
        }

        s_loop = GLib.g_main_loop_new(0, 0);
        s_started.SetResult();
        GLib.g_main_loop_run(s_loop);

        // Keep iterating briefly so WebKit's child processes see their views go and exit.
        var until = Environment.TickCount64 + 300;
        while (Environment.TickCount64 < until)
        {
            GLib.g_main_context_iteration(0, 0);
            Thread.Sleep(5);
        }
    }

    [UnmanagedCallersOnly]
    private static int Drain(nint data)
    {
        Volatile.Write(ref s_scheduled, 0);
        while (s_queue.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("UI-THREAD-EXCEPTION " + exception);
            }
        }

        return 0;
    }

    private sealed class GLibContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => Ui.Post(() => d(state));

        public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException("Send would block");

        public override SynchronizationContext CreateCopy() => this;
    }
}

/// <summary>A GCHandle-backed user-data pointer for native callbacks.</summary>
internal static class Handle
{
    public static nint Of(object target) => GCHandle.ToIntPtr(GCHandle.Alloc(target));

    public static T Get<T>(nint data) where T : class => (T)GCHandle.FromIntPtr(data).Target!;

    public static void Free(nint data) => GCHandle.FromIntPtr(data).Free();
}
