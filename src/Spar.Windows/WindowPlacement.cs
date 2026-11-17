using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Spar.Windows;

/// <summary>
/// The main window's size and place from <c>spar.Window(w => w.Size(…).MinSize(…))</c>, in device-independent pixels
/// of the page: the process is per-monitor DPI aware (Spar's manifest), so each is scaled by the DPI of the monitor the
/// window is on, and the window follows a move to a monitor of another DPI (<c>WM_DPICHANGED</c>). Remembering the
/// placement between runs is a later step.
/// </summary>
internal static unsafe class WindowPlacement
{
    /// <summary>The main window's style: a normal resizable window with the system title bar.</summary>
    public const WINDOW_STYLE Style = WINDOW_STYLE.WS_OVERLAPPEDWINDOW;

    private const uint DefaultDpi = 96;

    /// <summary>The first start's bounds: a client area of <paramref name="size"/>, centred in the primary monitor's work area.</summary>
    public static RECT Initial((int Width, int Height) size)
    {
        var monitor = PInvoke.MonitorFromPoint(default, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTOPRIMARY);
        uint dpi;
        uint unused;
        if (PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, &dpi, &unused).Failed)
        {
            dpi = DefaultDpi;
        }

        var outer = new RECT { right = Scale(size.Width, dpi), bottom = Scale(size.Height, dpi) };
        PInvoke.AdjustWindowRectExForDpi(&outer, Style, false, 0, dpi);
        var width = outer.right - outer.left;
        var height = outer.bottom - outer.top;

        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        if (!PInvoke.GetMonitorInfo(monitor, &info))
        {
            return new RECT { left = 0, top = 0, right = width, bottom = height };
        }

        // A size larger than the screen is kept within it.
        var work = info.rcWork;
        width = Math.Min(width, work.right - work.left);
        height = Math.Min(height, work.bottom - work.top);
        var left = work.left + ((work.right - work.left - width) / 2);
        var top = work.top + ((work.bottom - work.top - height) / 2);
        return new RECT { left = left, top = top, right = left + width, bottom = top + height };
    }

    /// <summary><c>WM_GETMINMAXINFO</c>: the smallest window whose client area is <paramref name="minimum"/> at the window's DPI.</summary>
    public static void ApplyMinimum(HWND window, (int Width, int Height) minimum, LPARAM lParam)
    {
        if (minimum.Width <= 0 && minimum.Height <= 0)
        {
            return;
        }

        var dpi = PInvoke.GetDpiForWindow(window);
        if (dpi == 0)
        {
            dpi = DefaultDpi;
        }

        var outer = new RECT { right = Scale(minimum.Width, dpi), bottom = Scale(minimum.Height, dpi) };
        PInvoke.AdjustWindowRectExForDpi(&outer, Style, false, 0, dpi);
        var info = (MINMAXINFO*)lParam.Value;
        info->ptMinTrackSize.X = Math.Max(info->ptMinTrackSize.X, outer.right - outer.left);
        info->ptMinTrackSize.Y = Math.Max(info->ptMinTrackSize.Y, outer.bottom - outer.top);
    }

    /// <summary><c>WM_DPICHANGED</c>: the window takes the size and place Windows suggests for the new DPI.</summary>
    public static void OnDpiChanged(HWND window, LPARAM lParam)
    {
        var suggested = (RECT*)lParam.Value;
        PInvoke.SetWindowPos(
            window,
            HWND.Null,
            suggested->left,
            suggested->top,
            suggested->right - suggested->left,
            suggested->bottom - suggested->top,
            SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
    }

    private static int Scale(int value, uint dpi) => (int)Math.Round(value * (double)dpi / DefaultDpi);
}
