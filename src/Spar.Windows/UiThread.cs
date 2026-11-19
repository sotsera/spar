using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Spar.Hosting;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Spar.Windows.Interop;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Spar.Windows;

/// <summary>What a window created on the UI thread does with its messages; null lets Windows' default run.</summary>
internal interface IWindowMessages
{
    LRESULT? OnMessage(HWND window, uint message, WPARAM wParam, LPARAM lParam);
}

/// <summary>
/// The window's UI thread (plan section 3, "Threading"): one STA that owns every window, WebView2 object and their
/// cleanup, and keeps pumping messages until the app's window is gone. It is also the synchronization context of its
/// own thread, so an <c>await</c> there continues there; work posted from other threads goes through a message-only
/// window, one message per burst. Nothing slow runs on it: callers post short work only. Each piece of work runs in a
/// named <see cref="Enter"/> scope, so the stall probe (when the build has one) can name the callback that stalled it.
/// </summary>
internal sealed unsafe class UiThread : SynchronizationContext
{
    private const uint DrainMessage = PInvoke.WM_APP + 1;
    private const string ClassName = "Spar.Window";

    /// <summary>The name of posted work that gives none, and of <c>await</c> continuations.</summary>
    private const string PostedWork = "posted work";

    private const string AwaitContinuation = "await continuation";

    /// <summary><c>GWLP_USERDATA</c>.</summary>
    private const int UserData = -21;

    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State, string Name)> _queue = new();
    private readonly Action<Exception> _report;
    private readonly uint _background;
    private int _threadId;
    private int _drainPosted;
    private HWND _queueWindow;
    private HBRUSH _brush;
    private IUiThreadWatch? _probe;

    private UiThread(uint background, Action<Exception> report)
    {
        _background = background;
        _report = report;
    }

    /// <summary>
    /// Runs <paramref name="start"/> on a new STA thread with a message loop, and returns when the loop ends (after
    /// <see cref="PInvoke.PostQuitMessage"/>). <paramref name="background"/> is the class brush colour (<c>0x00BBGGRR</c>),
    /// shown by every window of the thread until something paints it.
    /// </summary>
    public static void Run(uint background, Action<Exception> report, Action<UiThread> start)
    {
        var ui = new UiThread(background, report);
        var thread = new Thread(() => ui.Loop(start)) { Name = "Spar UI", IsBackground = false };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    /// <summary>
    /// Starts a UI thread that keeps pumping for the rest of the process, and returns it once it pumps: the one UI
    /// thread of a test process, shared by every window the process opens (plan section 5, "Rules for Spar's own
    /// code"). It is a background thread, so it never keeps the process alive.
    /// </summary>
    public static UiThread StartShared(uint background, Action<Exception> report)
    {
        var ui = new UiThread(background, report);
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() => ui.Loop(_ => ready.Set())) { Name = "Spar UI (tests)", IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return ui;
    }

    /// <summary>Whether the caller is on this UI thread.</summary>
    public bool IsCurrent => Environment.CurrentManagedThreadId == _threadId;

    /// <summary>
    /// Runs <paramref name="work"/> on the UI thread, after what is already queued. Safe from any thread.
    /// <paramref name="name"/> names it for the stall probe.
    /// </summary>
    public void Post(Action work, string name = PostedWork) => Enqueue(static state => ((Action)state!)(), work, name);

    public override void Post(SendOrPostCallback d, object? state) => Enqueue(d, state, AwaitContinuation);

    /// <summary>
    /// Starts the stall probe on this thread (UI thread, before the first window). The probe's own work goes through
    /// the same queue as everything else, which is what it measures; it stops when the thread's loop ends.
    /// </summary>
    public void UseProbe(IUiThreadWatch probe)
    {
        _probe = probe;
        probe.Start(work => Post(work, "stall probe"));
    }

    /// <summary>
    /// A Spar callback on this thread, named for the stall probe; dispose it when the callback ends. Without a probe
    /// (a Production build that did not keep it) it does nothing.
    /// </summary>
    public CallbackScope Enter(string name) => _probe is { } probe ? new CallbackScope(probe, name) : default;

    private void Enqueue(SendOrPostCallback d, object? state, string name)
    {
        _queue.Enqueue((d, state, name));

        // One message per burst: the drain empties the queue, and the next post after it asks again.
        if (Interlocked.Exchange(ref _drainPosted, 1) == 0)
        {
            PInvoke.PostMessage(_queueWindow, DrainMessage, default, default);
        }
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        // A synchronous call from another thread would block it on this one; nothing in Spar needs it.
        if (!IsCurrent)
        {
            throw new NotSupportedException("Work is posted to the window's UI thread, never sent synchronously.");
        }

        d(state);
    }

    /// <summary>
    /// Creates a top-level window of this thread's class at <paramref name="bounds"/> (physical pixels; null lets Windows
    /// place it); <paramref name="messages"/> receives its messages. The window is not shown yet.
    /// </summary>
    public HWND CreateWindow(string title, WINDOW_STYLE style, RECT? bounds, IWindowMessages messages, WINDOW_EX_STYLE extendedStyle = 0)
    {
        var handle = GCHandle.Alloc(messages);
        fixed (char* className = ClassName)
        fixed (char* text = title)
        {
            var window = PInvoke.CreateWindowEx(
                extendedStyle, className, text, style,
                bounds?.left ?? PInvoke.CW_USEDEFAULT,
                bounds?.top ?? PInvoke.CW_USEDEFAULT,
                bounds is { } width ? width.right - width.left : PInvoke.CW_USEDEFAULT,
                bounds is { } height ? height.bottom - height.top : PInvoke.CW_USEDEFAULT,
                HWND.Null, HMENU.Null, Instance(), (void*)GCHandle.ToIntPtr(handle));
            if (window.IsNull)
            {
                var error = Marshal.GetLastPInvokeError();
                handle.Free();
                throw new InvalidOperationException($"CreateWindowEx failed with error {error}.");
            }

            return window;
        }
    }

    private static HINSTANCE Instance() => (HINSTANCE)(nint)PInvoke.GetModuleHandle((char*)null).Value;

    private void Loop(Action<UiThread> start)
    {
        _threadId = Environment.CurrentManagedThreadId;
        SetSynchronizationContext(this);
        try
        {
            Register();
            _queueWindow = CreateQueueWindow();
            start(this);
            while (PInvoke.GetMessage(out var message, HWND.Null, 0, 0) > 0)
            {
                PInvoke.TranslateMessage(message);
                PInvoke.DispatchMessage(message);
            }
        }
        catch (Exception exception)
        {
            _report(exception);
        }
        finally
        {
            _probe?.Stop();
            Unregister();
        }
    }

    private void Register()
    {
        _brush = PInvoke.CreateSolidBrush(new COLORREF(_background));
        fixed (char* className = ClassName)
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WindowProcedure,
                hInstance = Instance(),
                hCursor = PInvoke.LoadCursor(HINSTANCE.Null, PInvoke.IDC_ARROW),
                hbrBackground = _brush,
                lpszClassName = className,
            };
            if (PInvoke.RegisterClassEx(windowClass) == 0)
            {
                throw new InvalidOperationException($"RegisterClassEx failed with error {Marshal.GetLastPInvokeError()}.");
            }
        }
    }

    private void Unregister()
    {
        if (!_queueWindow.IsNull)
        {
            PInvoke.DestroyWindow(_queueWindow);
            _queueWindow = HWND.Null;
        }

        fixed (char* className = ClassName)
        {
            PInvoke.UnregisterClass(className, Instance());
        }

        if (!_brush.IsNull)
        {
            PInvoke.DeleteObject(_brush);
            _brush = HBRUSH.Null;
        }
    }

    /// <summary>A message-only window that carries posted work, so the queue works before and after the app's window.</summary>
    private HWND CreateQueueWindow()
    {
        var handle = GCHandle.Alloc(new QueueMessages(this));
        fixed (char* className = ClassName)
        {
            var window = PInvoke.CreateWindowEx(
                0, className, null, 0, 0, 0, 0, 0, new HWND(-3), HMENU.Null, Instance(), (void*)GCHandle.ToIntPtr(handle));
            if (window.IsNull)
            {
                handle.Free();
                throw new InvalidOperationException($"CreateWindowEx (message-only) failed with error {Marshal.GetLastPInvokeError()}.");
            }

            return window;
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

    /// <summary>
    /// The one window procedure of the thread's windows. Each window's <see cref="IWindowMessages"/> is kept in its
    /// user data from <c>WM_NCCREATE</c> to <c>WM_NCDESTROY</c>, so no static state maps windows to objects.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT WindowProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            if (message == PInvoke.WM_NCCREATE)
            {
                var create = (CREATESTRUCTW*)lParam.Value;
                Native.SetWindowLongPtr((nint)window.Value, UserData, (nint)create->lpCreateParams);
            }

            var stored = Native.GetWindowLongPtr((nint)window.Value, UserData);
            if (stored != 0 && GCHandle.FromIntPtr(stored).Target is IWindowMessages messages)
            {
                if (message == PInvoke.WM_NCDESTROY)
                {
                    Native.SetWindowLongPtr((nint)window.Value, UserData, 0);
                    GCHandle.FromIntPtr(stored).Free();
                    _ = messages.OnMessage(window, message, wParam, lParam);
                }
                else if (messages.OnMessage(window, message, wParam, lParam) is { } handled)
                {
                    return handled;
                }
            }
        }
        catch (Exception exception)
        {
            // Nothing may unwind into Windows; the owner logs it.
            ReportFrom(window, exception);
        }

        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }

    private static void ReportFrom(HWND window, Exception exception)
    {
        var stored = Native.GetWindowLongPtr((nint)window.Value, UserData);
        if (stored != 0 && GCHandle.FromIntPtr(stored).Target is QueueMessages queue)
        {
            queue.Ui._report(exception);
            return;
        }

        if (stored != 0 && GCHandle.FromIntPtr(stored).Target is IReportsErrors owner)
        {
            owner.Report(exception);
        }
    }

    /// <summary>The message-only window's messages: the drain of posted work.</summary>
    private sealed class QueueMessages(UiThread ui) : IWindowMessages
    {
        public UiThread Ui { get; } = ui;

        public LRESULT? OnMessage(HWND window, uint message, WPARAM wParam, LPARAM lParam)
        {
            if (message != DrainMessage)
            {
                return null;
            }

            Ui.Drain();
            return default(LRESULT);
        }
    }
}

/// <summary>A window's owner that logs what its message handling threw.</summary>
internal interface IReportsErrors
{
    void Report(Exception exception);
}
