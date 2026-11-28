using Spar.AppWindows;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Spar.Windows;

/// <summary>
/// The frame of a window whose page draws the title bar (plan 3, "Title bar drawn by the page";
/// <c>docs/evidence/windows-titlebar</c>): the window keeps <c>WS_OVERLAPPEDWINDOW</c>, so Snap, the system menu,
/// <c>Alt+Space</c>, <c>Win+arrows</c> and the rounded corners stay, and <c>WM_NCCALCSIZE</c> gives the caption and the
/// top border to the client area, where the page draws its bar; maximized, the client area is exactly the work area.
/// The left, right and bottom resize edges stay the frame's; the top edge is a band the host answers itself
/// (<see cref="HitTest"/>), from the page's layout, never asking the page. UI thread.
/// </summary>
internal static unsafe class PageFrame
{
    private const uint DefaultDpi = 96;

    /// <summary>The sizing frame at <paramref name="dpi"/>: the top resize band, and what a maximized window hides above the screen.</summary>
    public static int Thickness(uint dpi) =>
        PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CYSIZEFRAME, dpi)
        + PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXPADDEDBORDER, dpi);

    /// <summary>Applies the frame at once (before the first paint): Windows asks <c>WM_NCCALCSIZE</c> again.</summary>
    public static void Apply(HWND window) =>
        PInvoke.SetWindowPos(
            window,
            HWND.Null,
            0,
            0,
            0,
            0,
            SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED | SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE
            | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

    /// <summary>
    /// <c>WM_NCCALCSIZE</c>: the frame Windows computes, with the top edge put back where the window starts, so the
    /// caption is the page's; a maximized window, which Windows places a frame's thickness above the screen, starts
    /// its client area that much lower.
    /// </summary>
    public static LRESULT CalcSize(HWND window, WPARAM wParam, LPARAM lParam)
    {
        var client = wParam.Value != 0 ? &((NCCALCSIZE_PARAMS*)lParam.Value)->rgrc._0 : (RECT*)lParam.Value;
        var top = client->top;
        _ = PInvoke.DefWindowProc(window, PInvoke.WM_NCCALCSIZE, wParam, lParam);
        client->top = PInvoke.IsZoomed(window) ? top + Thickness(Dpi(window)) : top;
        return default;
    }

    /// <summary>
    /// The hit test of a point on the screen in the window's client area: the top resize band while the window is not
    /// maximized, then the page's caption buttons and drag areas from <paramref name="layout"/>, in CSS pixels scaled by
    /// the window's DPI and the page's <paramref name="zoom"/>; <c>HTCLIENT</c> for the rest.
    /// </summary>
    public static uint HitTest(HWND window, int screenX, int screenY, TitleBarLayout layout, double zoom, out TitleBarHit hit)
    {
        hit = TitleBarHit.Client;
        var point = new System.Drawing.Point(screenX, screenY);
        if (!PInvoke.ScreenToClient(window, ref point) || !PInvoke.GetClientRect(window, out var client))
        {
            return PInvoke.HTCLIENT;
        }

        var dpi = Dpi(window);
        var band = Thickness(dpi);
        if (!PInvoke.IsZoomed(window) && point.Y >= 0 && point.Y < band)
        {
            var corner = band * 2;
            return point.X < corner ? PInvoke.HTTOPLEFT : point.X >= client.right - corner ? PInvoke.HTTOPRIGHT : PInvoke.HTTOP;
        }

        var scale = dpi / (double)DefaultDpi * zoom;
        hit = layout.HitTest(point.X / scale, point.Y / scale);
        return Code(hit);
    }

    /// <summary>The hit code of a part of the page's title bar.</summary>
    public static uint Code(TitleBarHit hit) => hit switch
    {
        TitleBarHit.Minimize => PInvoke.HTMINBUTTON,
        TitleBarHit.Maximize => PInvoke.HTMAXBUTTON,
        TitleBarHit.Close => PInvoke.HTCLOSE,
        TitleBarHit.Drag => PInvoke.HTCAPTION,
        _ => PInvoke.HTCLIENT,
    };

    /// <summary>The part of the title bar a hit code names; <see cref="TitleBarHit.Client"/> for any other code.</summary>
    public static TitleBarHit ButtonOf(nuint code) => (uint)code switch
    {
        PInvoke.HTMINBUTTON => TitleBarHit.Minimize,
        PInvoke.HTMAXBUTTON => TitleBarHit.Maximize,
        PInvoke.HTCLOSE => TitleBarHit.Close,
        _ => TitleBarHit.Client,
    };

    /// <summary>A caption button's command: <c>SC_MINIMIZE</c>, <c>SC_MAXIMIZE</c> or <c>SC_RESTORE</c>, or <c>SC_CLOSE</c>.</summary>
    public static uint Command(HWND window, TitleBarHit button) => button switch
    {
        TitleBarHit.Minimize => PInvoke.SC_MINIMIZE,
        TitleBarHit.Maximize => PInvoke.IsZoomed(window) ? PInvoke.SC_RESTORE : PInvoke.SC_MAXIMIZE,
        _ => PInvoke.SC_CLOSE,
    };

    /// <summary>The button's rectangle in the client area's physical pixels, grown to whole pixels.</summary>
    public static RECT ToPixels(LayoutRect rect, double scale) => new()
    {
        left = (int)Math.Floor(rect.X * scale),
        top = (int)Math.Floor(rect.Y * scale),
        right = (int)Math.Ceiling((rect.X + rect.Width) * scale),
        bottom = (int)Math.Ceiling((rect.Y + rect.Height) * scale),
    };

    /// <summary>The scale from the page's CSS pixels to the window's physical ones.</summary>
    public static double Scale(HWND window, double zoom) => Dpi(window) / (double)DefaultDpi * zoom;

    private static uint Dpi(HWND window)
    {
        var dpi = PInvoke.GetDpiForWindow(window);
        return dpi == 0 ? DefaultDpi : dpi;
    }
}
