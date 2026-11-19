using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Spar.Testing;

/// <summary>
/// A plain window of another thread, shown on the guest's screen, that a check clicks to take the foreground from the
/// app: "the first click on an inactive window" needs the app's window visible and not active. Only in the user session
/// of a guest Spar's runner started (<see cref="SandboxGuest.EnsureUserSession"/>); it never appears on a developer's
/// desktop. Its thread pumps its messages until <see cref="Dispose"/> closes it.
/// </summary>
[SupportedOSPlatform("windows10.0.17763")]
internal sealed unsafe class OtherWindow : IDisposable
{
    private const string ClassName = "SparSandboxOtherWindow";

    private readonly Thread _thread;
    private readonly TaskCompletionSource<nint> _created = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private nint _handle;

    /// <summary>The window's rectangle on the screen.</summary>
    public ScreenRect Bounds { get; }

    private OtherWindow(ScreenRect bounds)
    {
        Bounds = bounds;
        _thread = new Thread(Run) { IsBackground = true, Name = "Spar other window" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>Shows the window at <paramref name="bounds"/> and waits until it exists.</summary>
    public static OtherWindow Show(ScreenRect bounds)
    {
        SandboxGuest.EnsureUserSession("show a window on the screen");
        var window = new OtherWindow(bounds);
        window._handle = window._created.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        return window;
    }

    /// <summary>Whether this window is the foreground window now.</summary>
    public bool IsForeground => (nint)PInvoke.GetForegroundWindow().Value == _handle;

    public void Dispose()
    {
        if (_handle != 0)
        {
            PInvoke.PostMessage(new HWND((void*)_handle), PInvoke.WM_CLOSE, default, default);
        }

        _thread.Join(TimeSpan.FromSeconds(5));
    }

    private void Run()
    {
        try
        {
            var module = PInvoke.GetModuleHandle((char*)null);
            fixed (char* name = ClassName)
            {
                var windowClass = new WNDCLASSEXW
                {
                    cbSize = (uint)sizeof(WNDCLASSEXW),
                    lpfnWndProc = &Procedure,
                    hInstance = (HINSTANCE)module.Value,
                    lpszClassName = name,
                };

                // A second window in the same process finds the class registered: that is no failure.
                PInvoke.RegisterClassEx(windowClass);
            }

            var handle = PInvoke.CreateWindowEx(
                0,
                ClassName,
                "Spar other window",
                WINDOW_STYLE.WS_OVERLAPPEDWINDOW | WINDOW_STYLE.WS_VISIBLE,
                Bounds.Left,
                Bounds.Top,
                Bounds.Width,
                Bounds.Height,
                default,
                null,
                null,
                null);
            if (handle.IsNull)
            {
                throw new InvalidOperationException($"CreateWindowEx failed ({Marshal.GetLastPInvokeError()}).");
            }

            _created.TrySetResult((nint)handle.Value);

            MSG message;
            while (PInvoke.GetMessage(&message, default, 0, 0) > 0)
            {
                PInvoke.TranslateMessage(&message);
                PInvoke.DispatchMessage(&message);
            }
        }
        catch (Exception exception)
        {
            _created.TrySetException(exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT Procedure(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (message == PInvoke.WM_DESTROY)
        {
            PInvoke.PostQuitMessage(0);
            return default;
        }

        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }
}
