using System.Runtime.InteropServices;
using Spar.Linux.Interop;

namespace Spar.Linux;

/// <summary>
/// The page's zoom (decision "The window design, from the windows spike", item 3): WebKitGTK zooms on neither Ctrl+wheel
/// nor Ctrl+plus, so the host does, with controllers of its own on the view in the capture phase: Ctrl+wheel and
/// Ctrl+plus or minus step through the levels a browser uses, Ctrl+0 resets to 1, always within the window's limits.
/// The keys go on to the page, as Chromium's do; the zooming wheel does not, so the page does not scroll too. It starts at the remembered factor and reports each change
/// (<see cref="ZoomChanged"/>), from which the window saves it and tells its page. Loop thread.
/// </summary>
internal sealed partial class BrowserView
{
    /// <summary>The zoom levels a step goes through, as browsers have them.</summary>
    private static readonly double[] ZoomLevels = [0.25, 0.33, 0.5, 0.67, 0.75, 0.8, 0.9, 1, 1.1, 1.25, 1.5, 1.75, 2, 2.5, 3, 4, 5];

    private const uint ControlMask = 1 << 2;

    /// <summary>The time of the last key that zoomed, so WebKit's hand-back of it zooms no more.</summary>
    private uint _zoomKeyTime;

    /// <summary>The window's zoom: whether it is on, its limits, and the factor it starts at.</summary>
    public (bool Enabled, double Min, double Max, double Start) Zoom { get; init; } = (true, 0.25, 5, 1);

    /// <summary>The page's zoom changed, to a factor within the limits.</summary>
    public Action<double>? ZoomChanged { get; init; }

    /// <summary>The page's zoom now; the factor it will start at before the view exists.</summary>
    public double ZoomFactor => _view == 0 ? Math.Clamp(Zoom.Start, Zoom.Min, Zoom.Max) : WebKit.webkit_web_view_get_zoom_level(_view);

    /// <summary>Sets the page's zoom, kept within the limits; nothing while zoom is off.</summary>
    public void SetZoom(double factor)
    {
        if (_view != 0 && Zoom.Enabled)
        {
            WebKit.webkit_web_view_set_zoom_level(_view, Math.Clamp(factor, Zoom.Min, Zoom.Max));
        }
    }

    /// <summary>The new view's zoom: the start factor, its changes followed, and the keys and the wheel when it is on.</summary>
    private unsafe void StartZoom(nint self)
    {
        if (!Zoom.Enabled)
        {
            return;
        }

        var start = Math.Clamp(Zoom.Start, Zoom.Min, Zoom.Max);
        if (start != 1)
        {
            WebKit.webkit_web_view_set_zoom_level(_view, start);
        }

        _ = GLib.g_signal_connect_data(_view, "notify::zoom-level", (delegate* unmanaged<nint, nint, nint, void>)&OnZoomLevel, self, 0, 0);

        var keys = Gtk.gtk_event_controller_key_new();
        Gtk.gtk_event_controller_set_propagation_phase(keys, 1);
        _ = GLib.g_signal_connect_data(keys, "key-pressed", (delegate* unmanaged<nint, uint, uint, uint, nint, int>)&OnZoomKey, self, 0, 0);
        Gtk.gtk_widget_add_controller(_view, keys);

        // Vertical and discrete: one step per notch of the wheel.
        var wheel = Gtk.gtk_event_controller_scroll_new(1 | 4);
        Gtk.gtk_event_controller_set_propagation_phase(wheel, 1);
        _ = GLib.g_signal_connect_data(wheel, "scroll", (delegate* unmanaged<nint, double, double, nint, int>)&OnZoomWheel, self, 0, 0);
        Gtk.gtk_widget_add_controller(_view, wheel);
    }

    /// <summary>The next level up (<paramref name="direction"/> 1) or down (-1) from the factor now, within the limits.</summary>
    internal static double Step(double factor, int direction, double min, double max)
    {
        var next = direction > 0
            ? ZoomLevels.FirstOrDefault(level => level > factor + 0.001, ZoomLevels[^1])
            : ZoomLevels.LastOrDefault(level => level < factor - 0.001, ZoomLevels[0]);
        return Math.Clamp(next, min, max);
    }

    [UnmanagedCallersOnly]
    private static int OnZoomKey(nint controller, uint keyval, uint keycode, uint state, nint data)
    {
        if ((state & ControlMask) == 0 || GCHandle.FromIntPtr(data).Target is not BrowserView browser || browser._view == 0)
        {
            return 0;
        }

        // WebKit hands a key the page did not use back to GTK, which brings it here once more, with the same time.
        var time = Gtk.gtk_event_controller_get_current_event_time(controller);
        if (time != 0 && time == browser._zoomKeyTime)
        {
            return 0;
        }

        browser._zoomKeyTime = time;

        // GDK_KEY_plus, equal, KP_Add; minus, KP_Subtract; 0, KP_0.
        double? next = keyval switch
        {
            0x2b or 0x3d or 0xffab => Step(browser.ZoomFactor, 1, browser.Zoom.Min, browser.Zoom.Max),
            0x2d or 0xffad => Step(browser.ZoomFactor, -1, browser.Zoom.Min, browser.Zoom.Max),
            0x30 or 0xffb0 => Math.Clamp(1, browser.Zoom.Min, browser.Zoom.Max),
            _ => null,
        };
        if (next is not { } factor)
        {
            return 0;
        }

        browser.SetZoom(factor);
        // The key goes on to the page, as Chromium's does: WebKitGTK does nothing more with it.
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int OnZoomWheel(nint controller, double dx, double dy, nint data)
    {
        if ((Gtk.gtk_event_controller_get_current_event_state(controller) & ControlMask) == 0
            || dy == 0
            || GCHandle.FromIntPtr(data).Target is not BrowserView browser
            || browser._view == 0)
        {
            return 0;
        }

        // Wheel up (dy below 0) zooms in, as browsers do.
        browser.SetZoom(Step(browser.ZoomFactor, dy < 0 ? 1 : -1, browser.Zoom.Min, browser.Zoom.Max));
        return 1;
    }

    [UnmanagedCallersOnly]
    private static void OnZoomLevel(nint view, nint property, nint data)
    {
        if (GCHandle.FromIntPtr(data).Target is not BrowserView browser || browser._view == 0)
        {
            return;
        }

        try
        {
            browser.ZoomChanged?.Invoke(WebKit.webkit_web_view_get_zoom_level(view));
        }
        catch (Exception exception)
        {
            browser._log.Error("The zoom's change could not be handled", exception);
        }
    }
}
