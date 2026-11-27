using System.Runtime.InteropServices;
using Spar.AppWindows;
using Spar.Hosting;
using Spar.Linux.Interop;

namespace Spar.Linux;

/// <summary>
/// The window's <see cref="IAppWindow"/> operations (decision "M3d's decide-first questions", item 8), from any thread,
/// each posted to the main loop. Minimizing and maximizing are requests to the window manager: without one (a bare X
/// server) the window stays as it is. A size is the page's, which GTK gives the view as the window's default size.
/// </summary>
internal sealed partial class AppWindow : IWindowControl
{
    /// <summary>Whether the window is the host's to change: made, not destroyed, not closing.</summary>
    private bool Controllable => _window != 0 && _state != State.Closing;

    /// <summary>The handle the app's C# code holds for this window gets this window's control.</summary>
    private void AttachHandle()
    {
        if (_opened is not null)
        {
            _opened.Handle.Attach(this);
        }
        else
        {
            _windows?.AttachMain(this);
        }
    }

    void IWindowControl.Focus() => OnLoop("focus", Focus);

    void IWindowControl.Minimize() => OnLoop("minimize", () => Gtk.gtk_window_minimize(_window));

    void IWindowControl.Maximize() => OnLoop("maximize", () => Gtk.gtk_window_maximize(_window));

    void IWindowControl.Restore() => OnLoop("restore", () =>
    {
        if (Minimized)
        {
            Gtk.gtk_window_unminimize(_window);
        }
        else if (Gtk.gtk_window_is_maximized(_window) != 0)
        {
            Gtk.gtk_window_unmaximize(_window);
        }
    });

    void IWindowControl.SetSize(int width, int height) => OnLoop("set size", () => Gtk.gtk_window_set_default_size(_window, width, height));

    void IWindowControl.SetTitle(string title) => OnLoop("set title", () => Gtk.gtk_window_set_title(_window, title));

    void IWindowControl.SetZoom(double factor) => OnLoop("set zoom", () => _view?.SetZoom(factor));

    Task<bool> IWindowControl.CloseAsync()
    {
        var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_ui is not { } ui)
        {
            closed.SetResult(false);
            return closed.Task;
        }

        ui.Post(() => _ = CloseFromAppAsync(closed), "close from the app");
        return closed.Task;
    }

    /// <summary>The window's title and its page's size in CSS pixels, and whether it is minimized or maximized (loop thread).</summary>
    public (string Title, int Width, int Height, bool Minimized, bool Maximized, double Zoom) NativeState()
    {
        if (_window == 0)
        {
            return ("", 0, 0, false, false, 1);
        }

        var title = Marshal.PtrToStringUTF8(Gtk.gtk_window_get_title(_window)) ?? "";
        var view = ViewHandle;
        var (width, height) = view == 0 ? (0, 0) : (Gtk.gtk_widget_get_width(view), Gtk.gtk_widget_get_height(view));
        return (title, width, height, Minimized, Gtk.gtk_window_is_maximized(_window) != 0, _view?.ZoomFactor ?? 1);
    }

    /// <summary>Whether the window's surface is minimized, as the window manager reported it (loop thread).</summary>
    private bool Minimized =>
        Gtk.gtk_native_get_surface(_window) is var surface and not 0 && (Gtk.gdk_toplevel_get_state(surface) & Gtk.ToplevelMinimized) != 0;

    /// <summary>The close request the app's code asked for (loop thread); a window already gone counts as closed.</summary>
    private async Task CloseFromAppAsync(TaskCompletionSource<bool> closed)
    {
        try
        {
            var report = await RequestCloseAsync(CloseReason.App, _automated, questions: null);
            closed.TrySetResult(report.Closed);
        }
        catch (Exception error)
        {
            closed.TrySetException(error);
        }
    }

    private void OnLoop(string what, Action work) => _ui?.Post(
        () =>
        {
            if (Controllable)
            {
                work();
            }
        },
        what);
}
