using System.Collections.Concurrent;
using System.Runtime.Versioning;

namespace Spar.Windows;

/// <summary>
/// An STA thread with a Win32 message loop and a SynchronizationContext, so awaits inside posted work return to it.
/// Production has one per app run. A test process has one, shared by every window its tests open (WebView2 objects
/// belong to the thread that created them, and one thread can drive many windows).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class UiThread : SynchronizationContext
{
    private const uint WmDrain = Win32.WmApp + 1;
    private static readonly Lazy<UiThread> SharedThread = new(() => Start("Spar UI (tests)"));

    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private uint _threadId;

    private UiThread()
    {
    }

    /// <summary>The test process's UI thread, started on first use and kept until the process exits.</summary>
    public static UiThread Shared => SharedThread.Value;

    public Thread Thread { get; private set; } = null!;

    public static UiThread Start(string name)
    {
        var ui = new UiThread();
        ui.Thread = new Thread(ui.Loop) { Name = name, IsBackground = true };
        ui.Thread.SetApartmentState(ApartmentState.STA);
        ui.Thread.Start();
        ui._ready.Task.GetAwaiter().GetResult();
        return ui;
    }

    public override void Post(SendOrPostCallback d, object? state)
    {
        _queue.Enqueue((d, state));
        Win32.PostThreadMessageW(_threadId, WmDrain, 0, 0);
    }

    public override void Send(SendOrPostCallback d, object? state) =>
        throw new NotSupportedException("Never block on the UI thread.");

    /// <summary>Runs <paramref name="work"/> on this thread and completes with its result.</summary>
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

    public void Quit() => Win32.PostThreadMessageW(_threadId, Win32.WmQuit, 0, 0);

    private void Loop()
    {
        SetSynchronizationContext(this);
        _threadId = Win32.GetCurrentThreadId();

        // PostThreadMessage fails until the thread has a message queue; peeking creates it.
        Win32.PeekMessageW(out _, 0, Win32.WmUser, Win32.WmUser, Win32.PmNoRemove);
        _ready.SetResult();

        while (Win32.GetMessageW(out var message, 0, 0, 0) > 0)
        {
            if (message.Window == 0 && message.Message == WmDrain)
            {
                Drain();
                continue;
            }

            Win32.TranslateMessage(message);
            Win32.DispatchMessageW(message);
        }
    }

    private void Drain()
    {
        while (_queue.TryDequeue(out var item))
        {
            item.Callback(item.State);
        }
    }
}
