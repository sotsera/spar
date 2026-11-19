using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Spar.WindowTests;

/// <summary>
/// The test process's one UI thread (STA plus message loop), with a synchronization context that drains posted work
/// through a message-only window, so every await continues on this thread. Adapted from spikes/webview-profiles.
/// </summary>
internal static class Ui
{
    private const uint DrainMessage = PInvoke.WM_APP + 1;
    private const string ClassName = "SparWindowTestsSpike";

    private static UiContext? s_context;

    public static int Run(Func<Task<int>> main, int timeoutSeconds)
    {
        var exitCode = 0;
        var thread = new Thread(() =>
        {
            var window = CreateMessageWindow();
            s_context = new UiContext(window);
            SynchronizationContext.SetSynchronizationContext(s_context);
            s_context.Post(async _ =>
            {
                try
                {
                    exitCode = await main();
                }
                catch (Exception exception)
                {
                    Log.Write("FAILURE", exception.ToString().ReplaceLineEndings(" | "));
                    exitCode = 2;
                }

                PInvoke.PostQuitMessage(0);
            }, null);
            while (PInvoke.GetMessage(out var message, HWND.Null, 0, 0) > 0)
            {
                PInvoke.TranslateMessage(message);
                PInvoke.DispatchMessage(message);
            }
        }) { Name = "Spar UI", IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(timeoutSeconds)))
        {
            Log.Write("TIMEOUT", $"after={timeoutSeconds}s");
            return 3;
        }

        return exitCode;
    }

    /// <summary>A top-level test window: shown offscreen, no taskbar button, never activated.</summary>
    public static unsafe HWND CreateTestWindow(string title, int width = 800, int height = 600)
    {
        var instance = (HINSTANCE)(nint)PInvoke.GetModuleHandle((char*)null).Value;
        fixed (char* className = ClassName)
        fixed (char* text = title)
        {
            var window = PInvoke.CreateWindowEx(WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW, className, text,
                WINDOW_STYLE.WS_OVERLAPPEDWINDOW, -32000, -32000, width, height, HWND.Null, HMENU.Null, instance, null);
            if (window.IsNull)
            {
                throw new InvalidOperationException("CreateWindowEx failed: " + Marshal.GetLastPInvokeError());
            }

            PInvoke.ShowWindow(window, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
            return window;
        }
    }

    public static unsafe nint Foreground() => (nint)PInvoke.GetForegroundWindow().Value;

    private static unsafe HWND CreateMessageWindow()
    {
        var instance = (HINSTANCE)(nint)PInvoke.GetModuleHandle((char*)null).Value;
        fixed (char* className = ClassName)
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WindowProcedure,
                hInstance = instance,
                hCursor = PInvoke.LoadCursor(HINSTANCE.Null, PInvoke.IDC_ARROW),
                lpszClassName = className,
            };
            if (PInvoke.RegisterClassEx(windowClass) == 0)
            {
                throw new InvalidOperationException("RegisterClassEx failed: " + Marshal.GetLastPInvokeError());
            }

            // HWND_MESSAGE: a message-only window that carries the UI context's posted work.
            return PInvoke.CreateWindowEx(0, className, null, 0, 0, 0, 0, 0, new HWND(-3), HMENU.Null, instance, null);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT WindowProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            switch (message)
            {
                case DrainMessage:
                    s_context?.Drain();
                    return default;
                case PInvoke.WM_CLOSE:
                    // Test windows are closed by the spike only.
                    return default;
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Window procedure failure: " + exception);
        }

        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }

    private sealed class UiContext(HWND window) : SynchronizationContext
    {
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
}
