using System.Runtime.InteropServices;
using Spar.AppWindows;
using Spar.Linux.Interop;

namespace Spar.Linux;

/// <summary>
/// A page-drawn title bar (<c>TitleBar(TitleBar.Custom)</c>; <c>docs/app-model.md</c> 6.9): WebKitGTK ignores CSS
/// <c>app-region</c> (pitfalls), so the host moves the window from the page's drag areas itself, hit-testing the layout
/// the page's SDK posts, never asking the page. A legacy controller on the view in the capture phase sees each press,
/// motion and release before WebKit, and lets them go on (a gesture there never saw the motion: WebKit's own gestures
/// claim the press, which denies the others; pitfalls):
/// <list type="bullet">
/// <item>a press of the primary button in a drag area moves the window once the pointer has gone 4 pixels with it held
/// (starting on the press lost the double click on GTK 4; pitfalls), through <c>gdk_toplevel_begin_move</c>, after which
/// every controller of the view is reset, since the window manager takes the release (pitfalls, web processes
/// outliving the window);</item>
/// <item>a second press there within the double-click time maximizes or restores the window;</item>
/// <item>a secondary press there asks the window manager for the window's menu;</item>
/// <item>where no compositor draws GTK's frame (<see cref="HostEdges"/>), a press on the view's edge resizes the window,
/// which has no frame then.</item>
/// </list>
/// The caption buttons are the page's own, clicked as any button. Loop thread.
/// </summary>
internal sealed partial class BrowserView
{
    /// <summary>How far the pointer moves with the button down before the window moves, in pixels.</summary>
    private const double MoveThreshold = 4;

    /// <summary>GTK's default double-click time and distance, which a second press in a drag area is checked against.</summary>
    private const uint DoubleClickMilliseconds = 400;

    private const double DoubleClickDistance = 5;

    /// <summary>The width of the host's resize edges, and of their corners along each side, in pixels.</summary>
    private const double EdgeWidth = 6;

    private const double CornerLength = 16;

    /// <summary>The press in a drag area that may become a move: in the view's and the surface's coordinates.</summary>
    private Press? _press;

    /// <summary>The last press in a drag area, which a second one makes a double click.</summary>
    private (uint Time, double X, double Y)? _lastPress;

    /// <summary>The page-drawn title bar's layout and state; null for a window with the system's title bar.</summary>
    public TitleBarState? TitleBar { get; init; }

    /// <summary>
    /// Whether the host resizes the window from the view's edges: a window whose page draws the title bar on a display
    /// without a compositor, where GTK's frame has no shadow to hold its resize edges and kept them over the page,
    /// taking the presses on a caption button in the window's corner (measured in Spar's Linux test display).
    /// </summary>
    public bool HostEdges { get; init; }

    /// <summary>A window move or resize the title bar started, for the log.</summary>
    public Action<string>? FrameAction { get; init; }

    /// <summary>What the point (<paramref name="x"/>, <paramref name="y"/>) of the view is in the page's title bar.</summary>
    internal TitleBarHit HitTest(double x, double y)
    {
        var zoom = ZoomFactor;
        return TitleBar is { } titleBar ? titleBar.Layout.HitTest(x / zoom, y / zoom) : TitleBarHit.Client;
    }

    /// <summary>The host's resize edge at the view's point (<c>GdkSurfaceEdge</c>), or -1 for none.</summary>
    internal int EdgeAt(double x, double y)
    {
        if (!HostEdges || _view == 0)
        {
            return -1;
        }

        var native = Gtk.gtk_widget_get_native(_view);
        if (native == 0 || Gtk.gtk_window_is_maximized(native) != 0)
        {
            return -1;
        }

        var width = (double)Gtk.gtk_widget_get_width(_view);
        var height = (double)Gtk.gtk_widget_get_height(_view);
        var atTopOrBottom = y < EdgeWidth || y >= height - EdgeWidth;
        var atLeftOrRight = x < EdgeWidth || x >= width - EdgeWidth;
        var west = x < EdgeWidth || (x < CornerLength && atTopOrBottom);
        var east = x >= width - EdgeWidth || (x >= width - CornerLength && atTopOrBottom);
        var north = y < EdgeWidth || (y < CornerLength && atLeftOrRight);
        var south = y >= height - EdgeWidth || (y >= height - CornerLength && atLeftOrRight);
        return (north, south, west, east) switch
        {
            (true, _, true, _) => 0,
            (true, _, _, true) => 2,
            (true, _, _, _) => 1,
            (_, true, true, _) => 5,
            (_, true, _, true) => 7,
            (_, true, _, _) => 6,
            (_, _, true, _) => 3,
            (_, _, _, true) => 4,
            _ => -1,
        };
    }

    private unsafe void StartTitleBar(nint self)
    {
        if (TitleBar is null)
        {
            return;
        }

        var frame = Gtk.gtk_event_controller_legacy_new();
        Gtk.gtk_event_controller_set_propagation_phase(frame, 1);
        _ = GLib.g_signal_connect_data(frame, "event", (delegate* unmanaged<nint, nint, nint, int>)&OnFrameEvent, self, 0, 0);
        Gtk.gtk_widget_add_controller(_view, frame);
    }

    /// <summary>One event on its way to WebKit; true stops it there (a press that resizes the window).</summary>
    private bool HandleFrameEvent(nint gdkEvent)
    {
        var type = Gtk.gdk_event_get_event_type(gdkEvent);
        if (type == Gtk.ButtonRelease)
        {
            _press = null;
            return false;
        }

        if (type is not (Gtk.ButtonPress or Gtk.MotionNotify) || !TryPosition(gdkEvent, out var point))
        {
            return false;
        }

        if (type == Gtk.MotionNotify)
        {
            MoveIfDragged(gdkEvent, point);
            return false;
        }

        var button = Gtk.gdk_button_event_get_button(gdkEvent);
        if (button == 1 && EdgeAt(point.ViewX, point.ViewY) is var edge and >= 0)
        {
            Resize(gdkEvent, edge, point);
            return true;
        }

        if (HitTest(point.ViewX, point.ViewY) != TitleBarHit.Drag)
        {
            _press = null;
            return false;
        }

        if (button == 3)
        {
            _ = Gtk.gdk_toplevel_show_window_menu(point.Surface, gdkEvent);
        }
        else if (button == 1)
        {
            Pressed(gdkEvent, point);
        }

        return false;
    }

    /// <summary>A primary press in a drag area: the second of a double click flips the maximized state, the first may become a move.</summary>
    private void Pressed(nint gdkEvent, Point point)
    {
        var time = Gtk.gdk_event_get_time(gdkEvent);
        if (_lastPress is { } last
            && time - last.Time <= DoubleClickMilliseconds
            && Math.Abs(point.ViewX - last.X) <= DoubleClickDistance
            && Math.Abs(point.ViewY - last.Y) <= DoubleClickDistance)
        {
            _lastPress = null;
            _press = null;
            ToggleMaximized();
            return;
        }

        _lastPress = (time, point.ViewX, point.ViewY);
        _press = new Press(point, Gtk.gdk_event_get_device(gdkEvent));
    }

    /// <summary>The pointer moved: past the threshold with the press held, the window moves from where it was pressed.</summary>
    private void MoveIfDragged(nint gdkEvent, Point point)
    {
        if (_press is not { } press)
        {
            return;
        }

        var dx = point.ViewX - press.At.ViewX;
        var dy = point.ViewY - press.At.ViewY;
        if (Math.Sqrt((dx * dx) + (dy * dy)) < MoveThreshold)
        {
            return;
        }

        _press = null;
        _lastPress = null;
        Gtk.gdk_toplevel_begin_move(press.At.Surface, press.Device, 1, press.At.SurfaceX, press.At.SurfaceY, Gtk.gdk_event_get_time(gdkEvent));
        ResetControllers();
        FrameAction?.Invoke(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"move from {press.At.SurfaceX:0.#},{press.At.SurfaceY:0.#}"));
    }

    private void Resize(nint gdkEvent, int edge, Point point)
    {
        _press = null;
        _lastPress = null;
        Gtk.gdk_toplevel_begin_resize(
            point.Surface, edge, Gtk.gdk_event_get_device(gdkEvent), 1, point.SurfaceX, point.SurfaceY, Gtk.gdk_event_get_time(gdkEvent));
        ResetControllers();
        FrameAction?.Invoke(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"resize from edge {edge}"));
    }

    /// <summary>The event's point in the surface's coordinates and in the view's, and its surface.</summary>
    private unsafe bool TryPosition(nint gdkEvent, out Point point)
    {
        point = default;
        var native = Gtk.gtk_widget_get_native(_view);
        var surface = native == 0 ? 0 : Gtk.gtk_native_get_surface(native);
        double x;
        double y;
        if (surface == 0 || Gtk.gdk_event_get_position(gdkEvent, &x, &y) == 0)
        {
            return false;
        }

        // The surface holds the client-side shadow; the native's coordinates start inside it.
        double shadowX;
        double shadowY;
        Gtk.gtk_native_get_surface_transform(native, &shadowX, &shadowY);
        var inNative = stackalloc float[] { (float)(x - shadowX), (float)(y - shadowY) };
        var inView = stackalloc float[2];
        if (Gtk.gtk_widget_compute_point(native, _view, inNative, inView) == 0)
        {
            return false;
        }

        point = new Point(surface, x, y, inView[0], inView[1]);
        return true;
    }

    /// <summary>
    /// Every controller of the view lets go of the press, whose release the window manager takes: a GTK 4 gesture holds
    /// its widget while pressed, which kept the web processes alive after the window closed (pitfalls).
    /// </summary>
    private void ResetControllers()
    {
        var controllers = Gtk.gtk_widget_observe_controllers(_view);
        try
        {
            var count = GLib.g_list_model_get_n_items(controllers);
            for (uint index = 0; index < count; index++)
            {
                var controller = GLib.g_list_model_get_item(controllers, index);
                if (controller != 0)
                {
                    Gtk.gtk_event_controller_reset(controller);
                    GLib.g_object_unref(controller);
                }
            }
        }
        finally
        {
            GLib.g_object_unref(controllers);
        }
    }

    /// <summary>The window's maximized state flipped, as a double click on a title bar does.</summary>
    private void ToggleMaximized()
    {
        var native = Gtk.gtk_widget_get_native(_view);
        if (native == 0)
        {
            return;
        }

        var maximized = Gtk.gtk_window_is_maximized(native) != 0;
        if (maximized)
        {
            Gtk.gtk_window_unmaximize(native);
        }
        else
        {
            Gtk.gtk_window_maximize(native);
        }

        FrameAction?.Invoke(maximized ? "restore on a double click" : "maximize on a double click");
    }

    [UnmanagedCallersOnly]
    private static int OnFrameEvent(nint controller, nint gdkEvent, nint data)
    {
        if (GCHandle.FromIntPtr(data).Target is not BrowserView browser || browser._view == 0)
        {
            return 0;
        }

        try
        {
            return browser.HandleFrameEvent(gdkEvent) ? 1 : 0;
        }
        catch (Exception exception)
        {
            browser._log.Error("The title bar's input could not be handled", exception);
            return 0;
        }
    }

    /// <summary>A point of an event: its surface, the point in the surface's coordinates, and in the view's.</summary>
    private readonly record struct Point(nint Surface, double SurfaceX, double SurfaceY, double ViewX, double ViewY);

    /// <summary>A press that may become a move: where, and the device that pressed.</summary>
    private readonly record struct Press(Point At, nint Device);
}
