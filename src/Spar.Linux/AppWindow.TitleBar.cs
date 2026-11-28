using System.Runtime.InteropServices;
using Spar.AppWindows;
using Spar.Linux.Interop;

namespace Spar.Linux;

/// <summary>
/// A window whose page draws the title bar (<c>TitleBar(TitleBar.Custom)</c>; <c>docs/app-model.md</c> 6.9). Where a
/// compositor draws GTK's client-side frame, GTK gets a hidden empty title bar, so it draws none and keeps its shadow
/// and the resize edges in it (a visible empty one got the theme's full title-bar height;
/// <c>docs/evidence/linux-integration</c>). Where none does (X11 without a compositing manager), GTK's frame has no
/// shadow and kept its resize edges over the page, taking the presses on a caption button in the window's corner, so
/// the window has no frame at all and the view resizes it from its own edges (<see cref="BrowserView.HostEdges"/>).
/// Either way the view moves the window from the page's drag areas (<c>BrowserView.TitleBar.cs</c>), and the page hears
/// the window's maximized state. Loop thread.
/// </summary>
internal sealed partial class AppWindow
{
    private TitleBarState? _titleBar;
    private nuint _maximizedHandler;
    private bool _hostEdges;

    /// <summary>Whether the page draws the title bar.</summary>
    private bool CustomFrame => Options.TitleBarKind == TitleBar.Custom;

    /// <summary>The title bar the window, its view and its bridge share; null for the system's.</summary>
    private TitleBarState? TitleBarShared => CustomFrame ? _titleBar ??= new TitleBarState() : null;

    /// <summary>Before the view and before the window is shown: its frame, and its maximized state followed.</summary>
    private unsafe void StartTitleBar()
    {
        if (!CustomFrame)
        {
            return;
        }

        var display = Gtk.gtk_widget_get_display(_window);
        if (display != 0 && Gtk.gdk_display_is_composited(display) != 0)
        {
            var empty = Gtk.gtk_box_new(0, 0);
            Gtk.gtk_widget_set_visible(empty, 0);
            Gtk.gtk_window_set_titlebar(_window, empty);
        }
        else
        {
            Gtk.gtk_window_set_decorated(_window, 0);
            _hostEdges = true;
        }

        _log.Info($"Title bar drawn by the page; {(_hostEdges ? "no frame, the view's own resize edges (no compositor)" : "GTK's frame without its title bar")}.");
        _maximizedHandler = GLib.g_signal_connect_data(
            _window, "notify::maximized", (delegate* unmanaged<nint, nint, nint, void>)&OnMaximizedNotify, GCHandle.ToIntPtr(_self), 0, 0);
    }

    /// <summary>Before the window is destroyed: its handler goes.</summary>
    private void StopTitleBar()
    {
        if (_maximizedHandler != 0 && _window != 0)
        {
            GLib.g_signal_handler_disconnect(_window, _maximizedHandler);
            _maximizedHandler = 0;
        }
    }

    /// <summary>
    /// A window test's probe (<c>ISparTestWindow.TitleBarHitAsync</c>; called only from the test window, so trimmed from
    /// other builds): what the view finds at the page's point (<paramref name="x"/>, <paramref name="y"/>) in CSS pixels,
    /// a resize edge of its own first.
    /// </summary>
    internal string TitleBarProbe(float x, float y)
    {
        if (_view is not { } view)
        {
            return "view:client";
        }

        var zoom = view.ZoomFactor;
        var edge = view.EdgeAt(x * zoom, y * zoom);
        if (edge >= 0)
        {
            string[] edges = ["topLeft", "top", "topRight", "left", "right", "bottomLeft", "bottom", "bottomRight"];
            return "view:" + edges[edge];
        }

        return "view:" + view.HitTest(x * zoom, y * zoom) switch
        {
            TitleBarHit.Drag => "drag",
            TitleBarHit.Minimize => "minimize",
            TitleBarHit.Maximize => "maximize",
            TitleBarHit.Close => "close",
            _ => "client",
        };
    }

    [UnmanagedCallersOnly]
    private static void OnMaximizedNotify(nint window, nint property, nint data)
    {
        if (GCHandle.FromIntPtr(data).Target is not AppWindow owner || owner._titleBar is not { } titleBar)
        {
            return;
        }

        var maximized = Gtk.gtk_window_is_maximized(window) != 0;
        if (maximized == titleBar.Maximized)
        {
            return;
        }

        titleBar.Maximized = maximized;
        owner._bridge?.SendEvent(WindowCommands.TitleBarEvent, TitleBarState.StatePayload(maximized));
    }
}
