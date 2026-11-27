using System.Runtime.InteropServices;
using Spar.AppWindows;
using Spar.Linux.Interop;

namespace Spar.Linux;

/// <summary>
/// The window's remembered place (decision "M3d's decide-first questions", item 5), from <c>windows.json</c> through the
/// app's <see cref="PlacementStore"/>: its size and maximized state everywhere, and on X11 its position, which Wayland
/// leaves to the compositor. A modal opens centred on its opener on X11, where a window manager may place it anywhere
/// (decision "The window design, from the windows spike", item 4); another window of a kind with several opens offset
/// from the last one still open. A remembered position outside the screen is left to the window manager. What changes is
/// followed through the window's <c>default-width</c>, <c>default-height</c> and <c>maximized</c> properties, and the
/// position is read again when the window closes.
/// </summary>
internal sealed partial class AppWindow
{
    /// <summary>How far a new window of a kind with several opens from the last one, in pixels.</summary>
    private const int CascadeOffset = 32;

    /// <summary>The window's <c>notify::</c> handlers of its place, disconnected before it is destroyed.</summary>
    private readonly List<nuint> _placementHandlers = [];

    /// <summary>The page's zoom now, which the bridge's reader reads too.</summary>
    private double _zoomNow = 1;

    private PlacementStore? Placements => _windows?.Placements;

    private double ZoomNow => Volatile.Read(ref _zoomNow);

    /// <summary>The remembered size and maximized state, set before the window is shown (loop thread); the place, or null.</summary>
    private SavedPlacement? ApplySavedSize()
    {
        var saved = Placements?.Get(Options);
        var limits = Options.ZoomLimits;
        Volatile.Write(ref _zoomNow, limits.Enabled ? Math.Clamp(saved?.Zoom ?? 1, limits.Min, limits.Max) : 1);
        var (width, height) = saved is { } placement ? (placement.Width, placement.Height) : Options.InitialSize;
        Gtk.gtk_window_set_default_size(_window, width, height);
        if (saved is { Maximized: true })
        {
            Gtk.gtk_window_maximize(_window);
        }

        return saved;
    }

    /// <summary>
    /// Where the realized window goes on X11, before it is shown (loop thread): centred on its modal owner, at its
    /// remembered position, or offset from the last window of its kind. Nothing on Wayland.
    /// </summary>
    private void PlaceOnScreen(AppWindow? modalOwner, SavedPlacement? saved)
    {
        if (X11Of(_window) is not { } x11)
        {
            return;
        }

        Gtk.gtk_window_get_default_size(_window, out var width, out var height);
        (int X, int Y)? place = null;
        if (modalOwner is not null && modalOwner.Frame() is { } owner)
        {
            place = (owner.X + ((owner.Width - width) / 2), owner.Y + ((owner.Height - height) / 2));
        }
        else if (saved is { HasPosition: true } && OnScreen(x11, saved.Left!.Value, saved.Top!.Value))
        {
            place = (saved.Left!.Value, saved.Top!.Value);
        }
        else if (_opened is { } opened && LastOfKind(opened.Kind)?.Frame() is { } last)
        {
            place = (last.X + CascadeOffset, last.Y + CascadeOffset);
        }

        if (place is { } at)
        {
            X11.XMoveWindow(x11.Display, x11.Window, Math.Max(0, at.X), Math.Max(0, at.Y));
            X11.XFlush(x11.Display);
        }
    }

    /// <summary>Follows the window's size and maximized state into the store (loop thread).</summary>
    private unsafe void FollowPlacement()
    {
        if (Placements is null)
        {
            return;
        }

        foreach (var property in new[] { "notify::default-width", "notify::default-height", "notify::maximized" })
        {
            _placementHandlers.Add(GLib.g_signal_connect_data(
                _window, property, (delegate* unmanaged<nint, nint, nint, void>)&OnPlacementNotify, GCHandle.ToIntPtr(_self), 0, 0));
        }
    }

    /// <summary>The handlers go before the window is destroyed; its last position is read first (loop thread).</summary>
    private void StopFollowingPlacement()
    {
        if (_window == 0)
        {
            return;
        }

        PlacementChanged();
        foreach (var handler in _placementHandlers)
        {
            GLib.g_signal_handler_disconnect(_window, handler);
        }

        _placementHandlers.Clear();
    }

    /// <summary>The window's place, as its kind keeps it, goes to the store, which writes once the changes settle (loop thread).</summary>
    private void PlacementChanged()
    {
        if (Placements is not { } store || _window == 0)
        {
            return;
        }

        var zoom = ZoomNow;
        if (Gtk.gtk_window_is_maximized(_window) != 0)
        {
            var before = store.Get(Options);
            Gtk.gtk_window_get_default_size(_window, out var normalWidth, out var normalHeight);
            store.Update(Options, new SavedPlacement(before?.Left, before?.Top, before?.Width ?? normalWidth, before?.Height ?? normalHeight, true, zoom));
            return;
        }

        Gtk.gtk_window_get_default_size(_window, out var width, out var height);
        var frame = Frame();
        store.Update(Options, new SavedPlacement(frame?.X, frame?.Y, width, height, false, zoom));
    }

    /// <summary>The window of <paramref name="kind"/> opened last that is still open, other than this one.</summary>
    private AppWindow? LastOfKind(string kind) =>
        Family?.Others.LastOrDefault(other => other != this && other._opened?.Kind == kind && !other.IsClosing && other._window != 0);

    /// <summary>The window's place and size on the X11 root window; null on Wayland or before it is realized.</summary>
    private (int X, int Y, int Width, int Height)? Frame()
    {
        if (X11Of(_window) is not { } x11)
        {
            return null;
        }

        var root = X11.XDefaultRootWindow(x11.Display);
        if (X11.XTranslateCoordinates(x11.Display, x11.Window, root, 0, 0, out var x, out var y, out _) == 0)
        {
            return null;
        }

        Gtk.gtk_window_get_default_size(_window, out var width, out var height);
        return (x, y, width, height);
    }

    /// <summary>Whether (<paramref name="x"/>, <paramref name="y"/>) is on the X11 screen.</summary>
    private static unsafe bool OnScreen((nint Display, nuint Window) x11, int x, int y)
    {
        var attributes = stackalloc byte[256];
        var root = X11.XDefaultRootWindow(x11.Display);
        if (X11.XGetWindowAttributes(x11.Display, root, attributes) == 0)
        {
            return false;
        }

        // XWindowAttributes: x, y, width, height are the first four ints.
        var width = *(int*)(attributes + 8);
        var height = *(int*)(attributes + 12);
        return x >= 0 && y >= 0 && x < width - 32 && y < height - 32;
    }

    /// <summary>The Xlib display and window of a realized GTK window on X11; null on Wayland or before it is realized.</summary>
    private static (nint Display, nuint Window)? X11Of(nint window)
    {
        try
        {
            var surface = X11.gtk_native_get_surface(window);
            if (surface == 0 || X11.g_type_check_instance_is_a(surface, X11.gdk_x11_surface_get_type()) == 0)
            {
                return null;
            }

            return (X11.gdk_x11_display_get_xdisplay(X11.gdk_surface_get_display(surface)), X11.gdk_x11_surface_get_xid(surface));
        }
        catch (EntryPointNotFoundException)
        {
            // A GTK built without its X11 backend.
            return null;
        }
    }

    [UnmanagedCallersOnly]
    private static void OnPlacementNotify(nint window, nint property, nint data)
    {
        if (GCHandle.FromIntPtr(data).Target is not AppWindow owner || owner._state == State.Closing)
        {
            return;
        }

        try
        {
            owner.PlacementChanged();
        }
        catch (Exception exception)
        {
            owner.Report(exception);
        }
    }

    /// <summary>
    /// The page's zoom changed, by the user or by <c>zoom.set</c> (loop thread): it is remembered with the window's place,
    /// and the page hears it.
    /// </summary>
    private void OnZoomChanged(double factor)
    {
        Volatile.Write(ref _zoomNow, factor);
        if (_bridge?.Window is { } window)
        {
            window.Zoom = factor;
        }

        PlacementChanged();
        _bridge?.SendZoom(factor);
    }
}
