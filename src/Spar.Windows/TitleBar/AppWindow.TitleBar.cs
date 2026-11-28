using Spar.AppWindows;
using Spar.Windows.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Spar.Windows;

/// <summary>
/// A window whose page draws the title bar (<c>TitleBar(TitleBar.Custom)</c>; <c>docs/app-model.md</c> 6.9): the frame
/// (<see cref="PageFrame"/>), the caption window over the page's buttons (<see cref="CaptionSink"/>), the hit test from
/// the layout the page's SDK posts, the window's state and the buttons' look sent to the page, and UI Automation's
/// view of the window without the native title bar it no longer has (<see cref="WindowProvider"/>). WebView2 itself
/// moves the window from the page's drag areas (CSS <c>app-region</c>), with double-click and the system menu. UI thread.
/// </summary>
internal sealed partial class AppWindow
{
    /// <summary>How long after a new layout the host looks again for WebView2's drag-region windows, which it makes later.</summary>
    private const int DragRegionsSettleMilliseconds = 500;

    private TitleBarState? _titleBar;
    private CaptionSink? _sink;
    private WindowProvider? _automation;
    private bool _maximizedShown;
    private bool _rescanPending;

    /// <summary>Whether the page draws the title bar.</summary>
    private bool CustomFrame => Options.TitleBarKind == TitleBar.Custom;

    /// <summary>The title bar the window and its bridge share; null for the system's.</summary>
    private TitleBarState? TitleBarShared => CustomFrame
        ? _titleBar ??= new TitleBarState { LayoutChanged = () => _ui?.Post(UpdateCaption, "title bar layout") }
        : null;

    /// <summary>After the window exists, before it is shown: the frame applied and the caption window made.</summary>
    private void StartTitleBar(UiThread ui)
    {
        if (!CustomFrame)
        {
            return;
        }

        PageFrame.Apply(_window);
        _sink = CaptionSink.Create(ui, _window, HitTestScreen, ShowCaption);
        UpdateCaption();
    }

    /// <summary>The messages a page-drawn title bar handles; null for the others and for a window with the system's.</summary>
    private unsafe LRESULT? OnTitleBarMessage(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (!CustomFrame)
        {
            return null;
        }

        switch (message)
        {
            case PInvoke.WM_NCCALCSIZE:
                return PageFrame.CalcSize(window, wParam, lParam);
            case PInvoke.WM_NCHITTEST:
                // The left, right and bottom edges are the frame's; the rest is the client area the page draws.
                var frame = PInvoke.DefWindowProc(window, message, wParam, lParam);
                return frame.Value == PInvoke.HTCLIENT
                    ? new LRESULT((nint)HitTestScreen((short)(lParam.Value & 0xFFFF), (short)((lParam.Value >> 16) & 0xFFFF)))
                    : frame;
            case PInvoke.WM_GETOBJECT when (int)lParam.Value == Automation.RootObjectId:
                _automation ??= new WindowProvider((nint)window.Value, hidden: false);
                return new LRESULT(Automation.UiaReturnRawElementProvider((nint)window.Value, wParam.Value, lParam.Value, _automation));
            default:
                return null;
        }
    }

    /// <summary>The window was resized, maximized or restored: the caption window follows, and the page hears a new state.</summary>
    private void TitleBarResized()
    {
        if (!CustomFrame || _window.IsNull || PInvoke.IsIconic(_window))
        {
            return;
        }

        UpdateCaption();
        var maximized = PInvoke.IsZoomed(_window);
        if (maximized == _maximizedShown)
        {
            return;
        }

        _maximizedShown = maximized;
        if (TitleBarShared is { } titleBar)
        {
            titleBar.Maximized = maximized;
            SendToPage(WindowCommands.TitleBarEvent, TitleBarState.StatePayload(maximized));
        }
    }

    /// <summary>The window is going: UI Automation lets go of its provider.</summary>
    private unsafe void EndTitleBar(HWND window)
    {
        if (_automation is not null)
        {
            _ = Automation.UiaReturnRawElementProvider((nint)window.Value, 0, 0, null);
            _automation = null;
        }
    }

    /// <summary>The window's hit test of a screen point in its client area, from the page's last layout.</summary>
    private uint HitTestScreen(int x, int y)
    {
        var layout = _titleBar?.Layout ?? TitleBarLayout.Empty;
        return PageFrame.HitTest(_window, x, y, layout, _view?.ZoomFactor ?? 1, out _);
    }

    /// <summary>
    /// The caption window over the top band and the page's buttons now, and WebView2's drag-region windows out of the
    /// screen reader's view, again a little later once WebView2 has made the new ones.
    /// </summary>
    private void UpdateCaption()
    {
        if (_sink is not { } sink || _window.IsNull || _destroyed || !PInvoke.GetClientRect(_window, out var client))
        {
            return;
        }

        var layout = _titleBar?.Layout ?? TitleBarLayout.Empty;
        var scale = PageFrame.Scale(_window, _view?.ZoomFactor ?? 1);
        var band = PInvoke.IsZoomed(_window) ? 0 : PageFrame.Thickness(PInvoke.GetDpiForWindow(_window));
        sink.Update(client.right, client.bottom, band, layout.Buttons.Select(button => PageFrame.ToPixels(button, scale)));
        _ = DragRegionWindows.Hide(_window, sink.Handle);
        if (!_rescanPending)
        {
            _rescanPending = true;
            _ = HideDragRegionsLaterAsync();
        }
    }

    private async Task HideDragRegionsLaterAsync()
    {
        try
        {
            // On the UI thread's context: the continuation runs on the window's thread.
            await Task.Delay(DragRegionsSettleMilliseconds, _stopping.Token);
            _rescanPending = false;
            if (_sink is { } sink && !_window.IsNull && !_destroyed)
            {
                _ = DragRegionWindows.Hide(_window, sink.Handle);
            }
        }
        catch (OperationCanceledException)
        {
            // The window is closing.
        }
    }

    /// <summary>
    /// A window test's probe (<c>ISparTestWindow.TitleBarHitAsync</c>; called only from the test window, so trimmed from
    /// other builds): the hit code Windows would get at the page's point (<paramref name="x"/>, <paramref name="y"/>) in
    /// CSS pixels, from the caption window where its region has the point, from the window elsewhere.
    /// </summary>
    internal unsafe string TitleBarProbe(float x, float y)
    {
        var scale = PageFrame.Scale(_window, _view?.ZoomFactor ?? 1);
        var client = new System.Drawing.Point((int)Math.Round(x * scale), (int)Math.Round(y * scale));
        var screen = client;
        _ = PInvoke.ClientToScreen(_window, ref screen);
        var lParam = new LPARAM((nint)((screen.X & 0xFFFF) | ((screen.Y & 0xFFFF) << 16)));
        var target = _window;
        var owner = "window";
        if (_sink is { } sink)
        {
            var region = PInvoke.CreateRectRgn(0, 0, 0, 0);
            var inSink = PInvoke.GetWindowRgn(sink.Handle, region) != 0 && PInvoke.PtInRegion(region, client.X, client.Y);
            _ = PInvoke.DeleteObject(region);
            if (inSink)
            {
                target = sink.Handle;
                owner = "sink";
            }
        }

        var code = (uint)(nint)PInvoke.SendMessage(target, PInvoke.WM_NCHITTEST, default, lParam).Value;
        var name = code switch
        {
            PInvoke.HTCLIENT => "client",
            PInvoke.HTCAPTION => "caption",
            PInvoke.HTTOP => "top",
            PInvoke.HTTOPLEFT => "topLeft",
            PInvoke.HTTOPRIGHT => "topRight",
            PInvoke.HTMINBUTTON => "minimize",
            PInvoke.HTMAXBUTTON => "maximize",
            PInvoke.HTCLOSE => "close",
            _ => $"code {(int)code}",
        };
        return $"{owner}:{name}";
    }

    /// <summary>The caption button under the pointer and the one pressed, which the page draws.</summary>
    private void ShowCaption(TitleBarHit hover, TitleBarHit pressed) =>
        SendToPage(WindowCommands.CaptionEvent, TitleBarState.CaptionPayload(hover, pressed));

    /// <summary>An event for the window's current document, when it has one.</summary>
    private void SendToPage(string name, ReadOnlyMemory<byte> payload)
    {
        if (_bridge.Task.IsCompletedSuccessfully)
        {
            _bridge.Task.Result.SendEvent(name, payload);
        }
    }
}
