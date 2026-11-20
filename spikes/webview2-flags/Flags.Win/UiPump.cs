using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace WebView2Flags;

/// <summary>
/// The child's UI thread: one window and a message loop whose synchronization context runs continuations on it, so the
/// WebView2 calls stay on the STA that created the controller.
/// </summary>
internal sealed unsafe class UiPump : SynchronizationContext
{
    private readonly uint _threadId = Native.GetCurrentThreadId();
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();

    public override void Post(SendOrPostCallback d, object? state)
    {
        _queue.Enqueue((d, state));
        Native.PostThreadMessageW(_threadId, Native.WM_APP, 0, 0);
    }

    public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException();

    /// <summary>Pumps until <paramref name="until"/> completes.</summary>
    public void Run(Task until)
    {
        until.ContinueWith(_ => Native.PostThreadMessageW(_threadId, Native.WM_QUIT, 0, 0), TaskScheduler.Default);
        while (true)
        {
            Drain();
            var got = Native.GetMessageW(out var msg, 0, 0, 0);
            if (got <= 0)
            {
                break;
            }

            if (msg.hwnd == 0 && msg.message == Native.WM_APP)
            {
                continue;
            }

            Native.TranslateMessage(msg);
            Native.DispatchMessageW(msg);
        }

        Drain();
    }

    private void Drain()
    {
        while (_queue.TryDequeue(out var item))
        {
            item.Callback(item.State);
        }
    }

    /// <summary>A plain top-level window, shown without activation (it is on a desktop of its own anyway).</summary>
    public static nint CreateHostWindow(int width, int height)
    {
        fixed (char* name = "WebView2FlagsHost")
        {
            var wc = new Native.WNDCLASSW
            {
                lpfnWndProc = &WndProc,
                lpszClassName = name,
                hInstance = Marshal.GetHINSTANCE(typeof(UiPump).Module),
            };
            Native.RegisterClassW(wc);
        }

        var hwnd = Native.CreateWindowExW(Native.WS_EX_NOACTIVATE, "WebView2FlagsHost", "webview2-flags", Native.WS_OVERLAPPEDWINDOW, 0, 0, width, height, 0, 0, 0, 0);
        if (hwnd == 0)
        {
            throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastPInvokeError()}");
        }

        Native.ShowWindow(hwnd, Native.SW_SHOWNOACTIVATE);
        return hwnd;
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam) => Native.DefWindowProcW(hwnd, msg, wParam, lParam);
}
