#if SPIKE_ALL || SPIKE_PICKER
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace WinApis.App;

/// <summary>
/// Opens a FileOpenPicker owned by an offscreen window on an STA thread with a message loop. A watcher thread finds the
/// dialog (in any process: the picker may be brokered) by its owner window and closes it 1.5 s after it appears. If no
/// dialog is found within 5 s, the operation is cancelled. The pick is expected to complete with no file.
/// </summary>
internal static class PickerProbe
{
    private const uint ResumeMessage = PInvoke.WM_APP + 1;
    private const uint CancelMessage = PInvoke.WM_APP + 2;
    private static Action? s_pending;
    private static Action? s_cancel;
    private static HWND s_owner;

    public static string Run()
    {
        string? outcome = null;
        Exception? failure = null;
        var log = new List<string>();
        var thread = new Thread(() =>
        {
            var window = CreateOwnerWindow();
            s_owner = window;
            var watcher = new Thread(() => DismissDialog(window, log)) { IsBackground = true };
            try
            {
#if SPIKE_NO_WINRT
                outcome = "no WinRT in this build";
                PInvoke.PostMessage(window, ResumeMessage, default, default);
#else
                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                picker.FileTypeFilter.Add("*");
                InitializeWithWindow(picker, window);
                var opened = Stopwatch.StartNew();
                watcher.Start();
                var operation = picker.PickSingleFileAsync();
                Log(log, $"PickSingleFileAsync returned after {opened.ElapsedMilliseconds} ms, status {operation.Status}");
                s_cancel = operation.Cancel;

                // The completion arrives on another thread; it posts the result back to this STA thread.
                operation.Completed = (_, status) =>
                {
                    s_pending = () => outcome = $"completed {status} after {opened.ElapsedMilliseconds} ms, file={(status == Windows.Foundation.AsyncStatus.Completed ? operation.GetResults()?.Path ?? "null" : "n/a")}";
                    PInvoke.PostMessage(window, ResumeMessage, default, default);
                };
#endif
            }
            catch (Exception exception)
            {
                failure = exception;
                PInvoke.PostMessage(window, PInvoke.WM_CLOSE, default, default);
            }

            while (PInvoke.GetMessage(out var message, HWND.Null, 0, 0) > 0)
            {
                PInvoke.TranslateMessage(message);
                PInvoke.DispatchMessage(message);
            }

            if (watcher.IsAlive)
            {
                watcher.Join(TimeSpan.FromSeconds(6));
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        lock (log)
        {
            outcome = $"{outcome}; log: [{string.Join("; ", log)}]";
        }

        return failure is null ? outcome : throw failure;
    }

    private static void Log(List<string> log, string line)
    {
        lock (log)
        {
            log.Add(line);
        }
    }

#if !SPIKE_NO_WINRT
    /// <summary>
    /// IInitializeWithWindow on the picker's native object. CsWinRT 3.0 has no helper for this, so the interface comes
    /// from CsWin32 and the pointer from WindowsRuntimeMarshal.
    /// </summary>
    private static unsafe void InitializeWithWindow(object winrtObject, HWND window)
    {
        var iid = Windows.Win32.UI.Shell.IInitializeWithWindow.IID_Guid;
        var native = (Windows.Win32.UI.Shell.IInitializeWithWindow*)WindowsRuntime.InteropServices.WindowsRuntimeMarshal.ConvertToUnmanaged(winrtObject, in iid);
        try
        {
            native->Initialize(window);
        }
        finally
        {
            WindowsRuntime.InteropServices.WindowsRuntimeMarshal.Free(native);
        }
    }
#endif

    private static void DismissDialog(HWND owner, List<string> log)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(5))
        {
            var dialogs = FindOwnedWindows();
            if (dialogs.Count > 0)
            {
                var (dialog, description) = dialogs[0];
                Log(log, $"dialog {description} seen at {clock.ElapsedMilliseconds} ms");
                Thread.Sleep(1500);
                PInvoke.PostMessage(dialog, PInvoke.WM_CLOSE, default, default);
                Log(log, $"WM_CLOSE posted at {clock.ElapsedMilliseconds} ms");
                return;
            }

            Thread.Sleep(100);
        }

        Log(log, "no owned dialog within 5 s: cancelling the operation");
        PInvoke.PostMessage(owner, CancelMessage, default, default);
    }

    private static unsafe List<(HWND Window, string Description)> FindOwnedWindows()
    {
        var found = new List<(HWND, string)>();
        var handle = GCHandle.Alloc(found);
        try
        {
            PInvoke.EnumWindows(&CollectOwnedWindow, (LPARAM)GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }

        return found;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe BOOL CollectOwnedWindow(HWND window, LPARAM state)
    {
        if (!PInvoke.IsWindowVisible(window) || PInvoke.GetWindow(window, GET_WINDOW_CMD.GW_OWNER) != s_owner)
        {
            return true;
        }

        uint processId;
        PInvoke.GetWindowThreadProcessId(window, &processId);
        var name = stackalloc char[64];
        var length = PInvoke.GetClassName(window, name, 64);
        var where = processId == (uint)Environment.ProcessId ? "in-process" : $"in {ProcessName(processId)} (pid {processId})";
        ((List<(HWND, string)>)GCHandle.FromIntPtr(state).Target!).Add((window, $"class {new string(name, 0, length)} {where}"));
        return true;
    }

    private static string ProcessName(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return "exited process";
        }
    }

    private static unsafe HWND CreateOwnerWindow()
    {
        var instance = (HINSTANCE)(nint)PInvoke.GetModuleHandle((char*)null).Value;
        fixed (char* className = "SparCsWinRTSpike")
        fixed (char* title = "CsWinRT spike")
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WindowProcedure,
                hInstance = instance,
                lpszClassName = className,
            };
            PInvoke.RegisterClassEx(windowClass);

            // Shown but offscreen and not activated, as the D8 spike recommends for headless runs.
            var window = PInvoke.CreateWindowEx(0, className, title, WINDOW_STYLE.WS_OVERLAPPEDWINDOW, -32000, -32000, 640, 480, HWND.Null, HMENU.Null, instance, null);
            PInvoke.ShowWindow(window, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
            return window;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT WindowProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            switch (message)
            {
                case ResumeMessage:
                    Interlocked.Exchange(ref s_pending, null)?.Invoke();
                    PInvoke.DestroyWindow(window);
                    return default;
                case CancelMessage:
                    Interlocked.Exchange(ref s_cancel, null)?.Invoke();
                    return default;
                case PInvoke.WM_DESTROY:
                    PInvoke.PostQuitMessage(0);
                    return default;
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Window procedure failure: " + exception);
        }

        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }
}
#endif
