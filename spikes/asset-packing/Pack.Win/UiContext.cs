using System.Collections.Concurrent;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Spar.AssetPacking;

/// <summary>
/// Single-threaded synchronization context backed by the window's message loop: posted work is queued and drained
/// when the window receives <see cref="DrainMessage"/>, so async continuations stay on the STA thread.
/// </summary>
internal sealed class UiContext(HWND window) : SynchronizationContext
{
    public const uint DrainMessage = PInvoke.WM_APP + 1;

    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();
    private readonly int _threadId = Environment.CurrentManagedThreadId;

    public override void Post(SendOrPostCallback d, object? state)
    {
        _queue.Enqueue((d, state));
        PInvoke.PostMessage(window, DrainMessage, default, default);
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        if (Environment.CurrentManagedThreadId != _threadId)
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
}
