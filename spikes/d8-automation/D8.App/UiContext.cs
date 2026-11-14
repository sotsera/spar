using System.Collections.Concurrent;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace D8.App;

/// <summary>
/// Single-threaded synchronization context backed by the window's message loop. Posted work is queued and
/// drained when the window receives <see cref="DrainMessage"/>, so async continuations stay on the STA.
/// </summary>
internal sealed class UiContext : SynchronizationContext
{
    public const uint DrainMessage = PInvoke.WM_APP + 1;

    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();
    private readonly HWND _window;
    private readonly int _threadId;

    public UiContext(HWND window)
    {
        _window = window;
        _threadId = Environment.CurrentManagedThreadId;
    }

    public bool IsOnUiThread => Environment.CurrentManagedThreadId == _threadId;

    public override void Post(SendOrPostCallback d, object? state)
    {
        _queue.Enqueue((d, state));
        PInvoke.PostMessage(_window, DrainMessage, default, default);
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        if (!IsOnUiThread)
        {
            throw new NotSupportedException("Synchronous cross-thread Send is not supported.");
        }

        d(state);
    }

    public void Drain()
    {
        while (_queue.TryDequeue(out var item))
        {
            item.Callback(item.State);
        }
    }

    /// <summary>Runs <paramref name="work"/> on the UI thread and completes with its result.</summary>
    public Task<T> InvokeAsync<T>(Func<Task<T>> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(
            static async state =>
            {
                var (work, completion) = ((Func<Task<T>>, TaskCompletionSource<T>))state!;
                try
                {
                    completion.TrySetResult(await work());
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            },
            (work, completion));
        return completion.Task;
    }
}
