using System.Runtime.InteropServices;
using Spar.Hosting;
using Spar.Windows.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Spar.Windows;

/// <summary>
/// What the WebView's controller must know of its window (plan M1b step 6, research review section 4), on the UI
/// thread. The controller is a child of the window, so Windows tells it none of this by itself:
/// <list type="bullet">
/// <item><b>Focus.</b> Keyboard focus that reaches the window itself goes on into the page (<c>WM_SETFOCUS</c> to
/// <c>MoveFocus</c>), or Tab and typing never reach it. Activation gives the window focus through <c>DefWindowProc</c>,
/// except while it is minimized: there the focus moves when the window is restored. A controller created after the
/// window already had focus takes it at once. Nothing is moved while the window is disabled (a modal dialog, the
/// hung-page dialog over it owns the focus) or not the active window, so the host never takes focus back from a dialog.
/// Tab past the page's last field (or Shift+Tab before its first) comes back into the page at its other end, so focus
/// is never left on the window itself.</item>
/// <item><b>Visibility.</b> The controller is shown only while the window is shown and not minimized: a hidden window's
/// page otherwise keeps rendering and running its timers at full rate. The page then sees <c>visibilitychange</c>.
/// M3's "close to tray" hides the window, which this follows like any hide; a tray state that kept the window shown
/// would add its own condition to <see cref="WindowShown"/>.</item>
/// <item><b>Position.</b> Every move of the window is passed on (<c>NotifyParentWindowPositionChanged</c>), so popups the
/// browser draws in windows of its own, such as a <c>&lt;select&gt;</c>'s list and IME candidates, open at the page.</item>
/// </list>
/// The heartbeat (<see cref="PageWatch"/>) does not ping a page while this keeps it hidden.
/// </summary>
internal sealed class ViewWindowState
{
    private readonly UiThread _ui;
    private readonly HostLog _log;
    private readonly HWND _window;
    private ICoreWebView2Controller? _controller;
    private bool _shown = true;
    private bool _minimized;
    private int _focusMoves;
    private int _tabsOut;
    private int _visibilityChanges;
    private int _moves;

    public ViewWindowState(UiThread ui, HostLog log, HWND window)
    {
        _ui = ui;
        _log = log;
        _window = window;
    }

    /// <summary>A new controller: it gets the window's visibility, and the focus when the window has it.</summary>
    public void Attach(ICoreWebView2Controller controller)
    {
        _controller = controller;
        _minimized = PInvoke.IsIconic(_window);
        _shown = WindowShown();
        if (!_shown)
        {
            controller.PutIsVisible(0);
        }

        FocusIfWindowHasIt();
    }

    /// <summary>The controller is closing or being replaced: nothing more is passed on to it.</summary>
    public void Detach() => _controller = null;

    /// <summary>At teardown: what was passed on during the run, in the log.</summary>
    public void LogSummary() =>
        _log.Info($"Window: {_focusMoves} focus moves, {_tabsOut} Tab past the page, {_visibilityChanges} visibility changes, {_moves} moves.");

    /// <summary><c>WM_SETFOCUS</c>: the window itself has the focus, so it goes on into the page.</summary>
    public void OnSetFocus()
    {
        if (PInvoke.IsWindowEnabled(_window) && !PInvoke.IsIconic(_window))
        {
            MoveFocus(WebView2Values.FocusProgrammatic);
        }
    }

    /// <summary>
    /// <c>WM_WINDOWPOSCHANGED</c>, before <c>DefWindowProc</c> sends <c>WM_SIZE</c> and <c>WM_MOVE</c>: shown, hidden,
    /// minimized, restored or moved. The controller is shown before the WebView gets its new size.
    /// </summary>
    public unsafe void OnPositionChanged(LPARAM lParam)
    {
        var position = (WINDOWPOS*)lParam.Value;
        var shown = WindowShown();
        if (shown != _shown && _controller is { } controller)
        {
            _shown = shown;
            _visibilityChanges++;
            controller.PutIsVisible(shown ? 1 : 0);
        }

        if ((position->flags & SET_WINDOW_POS_FLAGS.SWP_NOMOVE) == 0 && _controller is { } moved)
        {
            _moves++;
            moved.NotifyParentWindowPositionChanged();
        }

        var wasMinimized = _minimized;
        _minimized = PInvoke.IsIconic(_window);
        if (wasMinimized && !_minimized)
        {
            // Activated while minimized, the window got no focus; it gets it now that it is restored.
            FocusIfWindowHasIt();
        }
    }

    /// <summary>Tab or Shift+Tab is leaving the page: focus goes back in at the page's other end, after the event.</summary>
    public void OnMoveFocusRequested(nint argsPointer)
    {
        var args = Com.Borrow<ICoreWebView2MoveFocusRequestedEventArgs>(argsPointer);
        int reason;
        try
        {
            reason = args.GetReason();
            args.PutHandled(1);
        }
        finally
        {
            Com.Release(args);
        }

        _tabsOut++;
        _ui.Post(
            () =>
            {
                if (PInvoke.IsWindowEnabled(_window))
                {
                    MoveFocus(reason);
                }
            },
            "move focus");
    }

    /// <summary>Shown and not minimized; a hidden window, M3's tray state included, is not.</summary>
    private bool WindowShown() => PInvoke.IsWindowVisible(_window) && !PInvoke.IsIconic(_window);

    /// <summary>Moves the focus into the page only when the window is the active one, enabled, and its focus is on itself or nowhere.</summary>
    private void FocusIfWindowHasIt()
    {
        if (PInvoke.GetActiveWindow() != _window || !PInvoke.IsWindowEnabled(_window) || PInvoke.IsIconic(_window))
        {
            return;
        }

        var focus = PInvoke.GetFocus();
        if (focus.IsNull || focus == _window)
        {
            MoveFocus(WebView2Values.FocusProgrammatic);
        }
    }

    private void MoveFocus(int reason)
    {
        if (_controller is null)
        {
            return;
        }

        try
        {
            _controller.MoveFocus(reason);
            _focusMoves++;
        }
        catch (COMException exception)
        {
            _log.Info($"MoveFocus returned 0x{exception.HResult:X8}.");
        }
    }
}
