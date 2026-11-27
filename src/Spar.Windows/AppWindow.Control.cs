using Spar.AppWindows;
using Spar.Hosting;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Spar.Windows;

/// <summary>
/// The window's <see cref="IAppWindow"/> operations (decision "M3d's decide-first questions", item 8), from any thread,
/// each posted to the UI thread. A test window stays offscreen and never activated: it minimizes and restores without
/// activation, and does not maximize, which would put it on the person's screen.
/// </summary>
internal sealed partial class AppWindow : IWindowControl
{
    /// <summary>Whether the window is the host's to change: made, not destroyed, not closing.</summary>
    private bool Controllable => !_window.IsNull && !_destroyed && _state != State.Closing;

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

    void IWindowControl.Focus() => OnUi("focus", Focus);

    void IWindowControl.Minimize() => OnUi("minimize", () =>
        PInvoke.ShowWindow(_window, _offscreen ? SHOW_WINDOW_CMD.SW_SHOWMINNOACTIVE : SHOW_WINDOW_CMD.SW_MINIMIZE));

    void IWindowControl.Maximize() => OnUi("maximize", () =>
    {
        if (SparTestSession.IsSupported && _offscreen)
        {
            _log.Step("maximize ignored: a test window stays offscreen");
            return;
        }

        PInvoke.ShowWindow(_window, SHOW_WINDOW_CMD.SW_MAXIMIZE);
    });

    void IWindowControl.Restore() => OnUi("restore", () =>
        PInvoke.ShowWindow(_window, _offscreen ? SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE : SHOW_WINDOW_CMD.SW_RESTORE));

    void IWindowControl.SetSize(int width, int height) => OnUi("set size", () =>
    {
        var bounds = WindowPlacement.OuterSize(_window, (width, height));
        PInvoke.SetWindowPos(
            _window, HWND.Null, 0, 0, bounds.Width, bounds.Height,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
    });

    void IWindowControl.SetTitle(string title) => OnUi("set title", () => PInvoke.SetWindowText(_window, title));

    void IWindowControl.SetZoom(double factor) => OnUi("set zoom", () => _view?.SetZoom(factor));

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

    /// <summary>The close request the app's code asked for (UI thread); a window already gone counts as closed.</summary>
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

    /// <summary>The window's title and the size of its page in CSS pixels, and whether it is minimized or maximized (UI thread).</summary>
    public (string Title, int Width, int Height, bool Minimized, bool Maximized, double Zoom) NativeState()
    {
        var title = WindowPlacement.Title(_window);
        var (width, height) = WindowPlacement.ClientSize(_window);
        return (title, width, height, PInvoke.IsIconic(_window), PInvoke.IsZoomed(_window), CurrentZoom());
    }

    private void OnUi(string what, Action work) => _ui?.Post(
        () =>
        {
            if (Controllable)
            {
                work();
            }
        },
        what);
}
