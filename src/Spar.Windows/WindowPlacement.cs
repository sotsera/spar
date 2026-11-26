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

    /// <summary>
    /// A test window's extended style (plan section 5): no taskbar button, and never activated, so it never takes the
    /// focus from the person at the desktop.
    /// </summary>
    public const WINDOW_EX_STYLE TestExtendedStyle = WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE;

    /// <summary>The variable <c>spar dev</c> sets on a restart, read by Development builds only (<see cref="Kept"/>).</summary>
    public const string KeptVariable = "SPAR_DEV_WINDOW";

    private const uint DefaultDpi = 96;

    /// <summary>Where a test window is shown: far outside every monitor, still shown, so the page renders and takes input.</summary>
    private const int OffscreenCorner = -32_000;

    /// <summary><paramref name="bounds"/>'s size, moved offscreen.</summary>
    public static RECT Offscreen(RECT bounds) => new()
    {
        left = OffscreenCorner,
        top = OffscreenCorner,
        right = OffscreenCorner + bounds.right - bounds.left,
        bottom = OffscreenCorner + bounds.bottom - bounds.top,
    };

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

    /// <summary>
    /// The place and size <c>spar dev</c> kept from the previous run of a Development build, in
    /// <see cref="KeptVariable"/> as <c>left,top,width,height</c> in physical pixels, then <c>,maximized</c> for a maximized
    /// window, whose normal place the four numbers are (decision "M2b's decide-first questions", item 4); null when it
    /// is not set, not four numbers, too small, or on no monitor now.
    /// </summary>
    public static (RECT Bounds, bool Maximized)? Kept()
    {
        var text = Environment.GetEnvironmentVariable(KeptVariable);
        if (text?.Split(',') is not { Length: 4 or 5 } parts || (parts.Length == 5 && parts[4] != "maximized"))
        {
            return null;
        }

        var maximized = parts.Length == 5;

        var numbers = new int[4];
        for (var index = 0; index < 4; index++)
        {
            if (!int.TryParse(parts[index], System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out numbers[index]))
            {
                return null;
            }
        }

        if (numbers[2] < 100 || numbers[3] < 100)
        {
            return null;
        }

        var bounds = new RECT { left = numbers[0], top = numbers[1], right = numbers[0] + numbers[2], bottom = numbers[1] + numbers[3] };
        return PInvoke.MonitorFromRect(&bounds, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONULL).IsNull ? null : (bounds, maximized);
    }

    /// <summary>
    /// Makes the hidden <paramref name="window"/> maximized without activating it, for <c>spar dev</c>'s restart of a
    /// maximized window (spike <c>spikes/maximized-noactivate</c>): every show command that maximizes also activates, so
    /// the normal place is recorded first (<c>SetWindowPlacement</c> with <c>SW_HIDE</c>, in workspace coordinates),
    /// then the window is marked <c>WS_MAXIMIZE</c> and sized as Windows maximizes it (its monitor's work area plus the
    /// sizing frame at its DPI). The caller shows it with <c>SW_SHOWNA</c>; Restore returns to <paramref name="normal"/>.
    /// </summary>
    public static void MaximizeHidden(HWND window, RECT normal)
    {
        var monitor = PInvoke.MonitorFromWindow(window, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        if (!PInvoke.GetMonitorInfo(monitor, &info))
        {
            return;
        }

        // The placement's rectangle is in workspace coordinates, which start at the work area's corner.
        var offsetX = info.rcWork.left - info.rcMonitor.left;
        var offsetY = info.rcWork.top - info.rcMonitor.top;
        var placement = new WINDOWPLACEMENT
        {
            length = (uint)sizeof(WINDOWPLACEMENT),
            showCmd = SHOW_WINDOW_CMD.SW_HIDE,
            rcNormalPosition = new RECT { left = normal.left - offsetX, top = normal.top - offsetY, right = normal.right - offsetX, bottom = normal.bottom - offsetY },
        };
        PInvoke.SetWindowPlacement(window, &placement);

        var style = PInvoke.GetWindowLong(window, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
        PInvoke.SetWindowLong(window, WINDOW_LONG_PTR_INDEX.GWL_STYLE, style | (int)WINDOW_STYLE.WS_MAXIMIZE);

        var dpi = PInvoke.GetDpiForWindow(window);
        var frame = PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXSIZEFRAME, dpi) + PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXPADDEDBORDER, dpi);
        var work = info.rcWork;
        PInvoke.SetWindowPos(
            window, default, work.left - frame, work.top - frame, work.right - work.left + (2 * frame), work.bottom - work.top + (2 * frame),
            SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED);
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
