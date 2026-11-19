using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Spar.WindowAutomation;

/// <summary>
/// The test process's one UI thread (STA plus message loop), as the Window surface would have it: the test body runs
/// on other threads and every WebView2 call is posted here. A synchronization context drains posted work through a
/// message-only window, so awaits inside posted work continue on this thread.
/// </summary>
internal static class Ui
{
    private const uint DrainMessage = Native.WmApp + 1;
    private const string ClassName = "SparWindowAutomationSpike";

    private static UiContext? s_context;
    private static Thread? s_thread;
    private static int s_threadId;

    public static bool IsUiThread => Environment.CurrentManagedThreadId == s_threadId;

    public static void Start()
    {
        var ready = new ManualResetEventSlim();
        s_thread = new Thread(() =>
        {
            var window = CreateMessageWindow();
            s_context = new UiContext(window);
            s_threadId = Environment.CurrentManagedThreadId;
            SynchronizationContext.SetSynchronizationContext(s_context);
            ready.Set();
            while (Native.GetMessageW(out var message, 0, 0, 0) > 0)
            {
                Native.TranslateMessage(message);
                Native.DispatchMessageW(message);
            }
        }) { Name = "Spar UI", IsBackground = true };
        s_thread.SetApartmentState(ApartmentState.STA);
        s_thread.Start();
        ready.Wait();
    }

    public static void Stop()
    {
        s_context!.Post(_ => Native.PostQuitMessage(0), null);
        s_thread!.Join(TimeSpan.FromSeconds(10));
    }

    /// <summary>Runs <paramref name="work"/> on the UI thread; the returned task completes off it.</summary>
    public static Task<T> InvokeAsync<T>(Func<Task<T>> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        s_context!.Post(async _ =>
        {
            try
            {
                completion.TrySetResult(await work());
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }, null);
        return completion.Task;
    }

    public static Task InvokeAsync(Func<Task> work) => InvokeAsync(async () =>
    {
        await work();
        return true;
    });

    public static Task<T> Invoke<T>(Func<T> work) => InvokeAsync(() => Task.FromResult(work()));

    public static Task Invoke(Action work) => InvokeAsync(() =>
    {
        work();
        return Task.FromResult(true);
    });

    /// <summary>A top-level test window: shown offscreen, no taskbar button, never activated.</summary>
    public static unsafe nint CreateTestWindow(int width = 1024, int height = 768)
    {
        const uint WsExNoActivate = 0x08000000;
        const uint WsExToolWindow = 0x00000080;
        const uint WsOverlappedWindow = 0x00CF0000;
        const int SwShowNoActivate = 4;
        var window = Native.CreateWindowExW(WsExNoActivate | WsExToolWindow, ClassName, "Window automation spike", WsOverlappedWindow,
            -32000, -32000, width, height, 0, 0, Native.GetModuleHandleW(null), 0);
        if (window == 0)
        {
            throw new InvalidOperationException("CreateWindowEx failed: " + Marshal.GetLastPInvokeError());
        }

        Native.ShowWindow(window, SwShowNoActivate);
        return window;
    }

    private static unsafe nint CreateMessageWindow()
    {
        var instance = Native.GetModuleHandleW(null);
        fixed (char* className = ClassName)
        {
            var windowClass = new Native.WindowClass
            {
                Size = (uint)sizeof(Native.WindowClass),
                Procedure = &WindowProcedure,
                Instance = instance,
                ClassName = className,
            };
            if (Native.RegisterClassExW(windowClass) == 0)
            {
                throw new InvalidOperationException("RegisterClassEx failed: " + Marshal.GetLastPInvokeError());
            }
        }

        // HWND_MESSAGE (-3): a message-only window that carries the UI context's posted work.
        return Native.CreateWindowExW(0, ClassName, null, 0, 0, 0, 0, 0, -3, 0, instance, 0);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProcedure(nint window, uint message, nint wParam, nint lParam)
    {
        try
        {
            switch (message)
            {
                case DrainMessage:
                    s_context?.Drain();
                    return 0;
                case Native.WmClose:
                    // Test windows are closed by the spike only.
                    return 0;
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Window procedure failure: " + exception);
        }

        return Native.DefWindowProcW(window, message, wParam, lParam);
    }

    private sealed class UiContext(nint window) : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state)
        {
            _queue.Enqueue((d, state));
            Native.PostMessageW(window, DrainMessage, 0, 0);
        }

        public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException();

        public void Drain()
        {
            while (_queue.TryDequeue(out var item))
            {
                item.Callback(item.State);
            }
        }
    }
}
