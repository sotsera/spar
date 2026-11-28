using Spar.AppWindows;
using Spar.Windows.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Spar.Windows;

/// <summary>
/// The host's window above the WebView, over the top resize band and the page's caption buttons only (its region), so
/// those points reach the host's hit test: WebView2's drag-region windows turn every code but the resize ones into
/// <c>HTCAPTION</c>, and <c>HTTRANSPARENT</c> passes only to windows of the same thread (pitfalls, "Custom title
/// bar"). Returning <c>HTMAXBUTTON</c> here gives Windows 11's Snap Layouts on the page's maximize button.
/// <c>WS_EX_TRANSPARENT</c> keeps the WebView under it visible; it paints nothing.
/// <para>
/// It tracks the caption buttons itself, so <c>DefWindowProc</c> never runs its own button tracking: a press takes
/// the mouse capture, survives the <c>WM_NCMOUSELEAVE</c> an inactive window's activation sends between press and
/// release (pitfalls), and a release on the pressed button posts its <c>WM_SYSCOMMAND</c> to the window. The page
/// hears the hover and the press (<see cref="TitleBarState.CaptionPayload"/>) to draw them, since the pointer never
/// reaches it there. Every other non-client message, the resize band's included, goes to the window. UI thread.
/// </para>
/// </summary>
internal sealed unsafe class CaptionSink : IWindowMessages
{
    private readonly HWND _parent;
    private readonly Func<int, int, uint> _hitTest;
    private readonly Action<TitleBarHit, TitleBarHit> _show;
    private HWND _window;
    private WindowProvider? _automation;
    private TitleBarHit _hover;
    private TitleBarHit _pressed;
    private TitleBarHit _shownHover;
    private TitleBarHit _shownPressed;
    private bool _tracking;

    /// <param name="hitTest">The window's hit test of a point on the screen.</param>
    /// <param name="show">The caption button the pointer is over, and the one pressed, when either changes.</param>
    private CaptionSink(HWND parent, Func<int, int, uint> hitTest, Action<TitleBarHit, TitleBarHit> show)
    {
        _parent = parent;
        _hitTest = hitTest;
        _show = show;
    }

    /// <summary>The sink's window, a child of <paramref name="parent"/> with an empty region until the first update.</summary>
    public static CaptionSink Create(UiThread ui, HWND parent, Func<int, int, uint> hitTest, Action<TitleBarHit, TitleBarHit> show)
    {
        var sink = new CaptionSink(parent, hitTest, show);
        sink._window = ui.CreateWindow(
            "",
            WINDOW_STYLE.WS_CHILD | WINDOW_STYLE.WS_VISIBLE | WINDOW_STYLE.WS_CLIPSIBLINGS,
            new RECT(),
            sink,
            WINDOW_EX_STYLE.WS_EX_TRANSPARENT | WINDOW_EX_STYLE.WS_EX_NOACTIVATE,
            parent);
        return sink;
    }

    public HWND Handle => _window;

    /// <summary>
    /// Covers the client area of <paramref name="width"/> × <paramref name="height"/>, above every other child, with a
    /// region of the top band <paramref name="band"/> pixels high (0 when maximized) and the buttons.
    /// </summary>
    public void Update(int width, int height, int band, IEnumerable<RECT> buttons)
    {
        if (_window.IsNull)
        {
            return;
        }

        var region = PInvoke.CreateRectRgn(0, 0, band > 0 ? width : 0, band);
        foreach (var button in buttons)
        {
            if (button.right > button.left && button.bottom > button.top)
            {
                var part = PInvoke.CreateRectRgn(button.left, button.top, button.right, button.bottom);
                _ = PInvoke.CombineRgn(region, region, part, RGN_COMBINE_MODE.RGN_OR);
                _ = PInvoke.DeleteObject(part);
            }
        }

        PInvoke.SetWindowPos(_window, HWND.HWND_TOP, 0, 0, width, height, SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

        // The window owns the region from here on.
        _ = PInvoke.SetWindowRgn(_window, region, true);
    }

    public LRESULT? OnMessage(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        switch (message)
        {
            case PInvoke.WM_NCHITTEST:
                var code = _hitTest(LowWord(lParam), HighWord(lParam));
                return new LRESULT(code == PInvoke.HTCLIENT ? PInvoke.HTTRANSPARENT : (nint)code);
            case PInvoke.WM_ERASEBKGND:
                return new LRESULT(1);
            case PInvoke.WM_PAINT:
                _ = PInvoke.BeginPaint(window, out var paint);
                _ = PInvoke.EndPaint(window, paint);
                return default(LRESULT);
            case PInvoke.WM_GETOBJECT when (int)lParam.Value == Automation.RootObjectId:
                // Out of the screen reader's view: the page's buttons below it are the ones it reads.
                _automation ??= new WindowProvider((nint)window.Value, hidden: true);
                return new LRESULT(Automation.UiaReturnRawElementProvider((nint)window.Value, wParam.Value, lParam.Value, _automation));
            case PInvoke.WM_NCMOUSEMOVE:
                Track(window);
                if (PageFrame.ButtonOf(wParam.Value) is var over and not TitleBarHit.Client)
                {
                    Hover(_pressed == TitleBarHit.Client || _pressed == over ? over : TitleBarHit.Client);
                    return default(LRESULT);
                }

                Hover(TitleBarHit.Client);
                return Forward(message, wParam, lParam);
            case PInvoke.WM_NCMOUSELEAVE:
                // The press is kept: activating an inactive window sends this between the press and the release.
                _tracking = false;
                Hover(TitleBarHit.Client);
                return Forward(message, wParam, lParam);
            case PInvoke.WM_NCLBUTTONDOWN or PInvoke.WM_NCLBUTTONDBLCLK when PageFrame.ButtonOf(wParam.Value) is var down and not TitleBarHit.Client:
                _pressed = down;
                _hover = down;
                _ = PInvoke.SetCapture(window);
                Show();
                return default(LRESULT);
            case PInvoke.WM_NCLBUTTONUP when PageFrame.ButtonOf(wParam.Value) is var up and not TitleBarHit.Client:
                Release(up);
                return default(LRESULT);
            case PInvoke.WM_MOUSEMOVE when _pressed != TitleBarHit.Client:
                // Captured: over the pressed button it shows pressed, anywhere else not.
                Hover(ButtonAt(window, lParam) == _pressed ? _pressed : TitleBarHit.Client);
                return default(LRESULT);
            case PInvoke.WM_LBUTTONUP when _pressed != TitleBarHit.Client:
                Release(ButtonAt(window, lParam));
                return default(LRESULT);
            case PInvoke.WM_CAPTURECHANGED:
                _pressed = TitleBarHit.Client;
                Show();
                return default(LRESULT);
            case >= PInvoke.WM_NCMOUSEMOVE and <= 0x00A9:
                // The rest of the non-client mouse (the resize band, right clicks) is the window's own.
                return Forward(message, wParam, lParam);
            case PInvoke.WM_DESTROY when _automation is not null:
                _ = Automation.UiaReturnRawElementProvider((nint)window.Value, 0, 0, null);
                _automation = null;
                return null;
            default:
                return null;
        }
    }

    /// <summary>The release of the left button: the pressed button's command when it is released over it.</summary>
    private void Release(TitleBarHit at)
    {
        var pressed = _pressed;
        _pressed = TitleBarHit.Client;
        if (PInvoke.GetCapture() == _window)
        {
            _ = PInvoke.ReleaseCapture();
        }

        // The capture ended the leave tracking: asked again, it reports at once a pointer already outside.
        _tracking = false;
        Track(_window);

        _hover = at;
        Show();
        if (pressed != TitleBarHit.Client && pressed == at)
        {
            _ = PInvoke.PostMessage(_parent, PInvoke.WM_SYSCOMMAND, new WPARAM(PageFrame.Command(_parent, pressed)), default);
        }
    }

    /// <summary>The caption button at a point of the sink's client area (the window's own, since the sink covers it).</summary>
    private TitleBarHit ButtonAt(HWND window, LPARAM lParam)
    {
        var point = new System.Drawing.Point(LowWord(lParam), HighWord(lParam));
        _ = PInvoke.ClientToScreen(window, ref point);
        return PageFrame.ButtonOf(_hitTest(point.X, point.Y));
    }

    /// <summary>Asks for <c>WM_NCMOUSELEAVE</c> once the pointer leaves the sink's non-client parts.</summary>
    private void Track(HWND window)
    {
        if (_tracking)
        {
            return;
        }

        var track = new TRACKMOUSEEVENT
        {
            cbSize = (uint)sizeof(TRACKMOUSEEVENT),
            dwFlags = TRACKMOUSEEVENT_FLAGS.TME_LEAVE | TRACKMOUSEEVENT_FLAGS.TME_NONCLIENT,
            hwndTrack = window,
        };
        _tracking = PInvoke.TrackMouseEvent(ref track);
    }

    private void Hover(TitleBarHit hover)
    {
        _hover = hover;
        Show();
    }

    /// <summary>Tells the page the buttons' look when it changed.</summary>
    private void Show()
    {
        if (_hover == _shownHover && _pressed == _shownPressed)
        {
            return;
        }

        _shownHover = _hover;
        _shownPressed = _pressed;
        _show(_hover, _pressed);
    }

    private LRESULT Forward(uint message, WPARAM wParam, LPARAM lParam) => PInvoke.SendMessage(_parent, message, wParam, lParam);

    private static int LowWord(LPARAM lParam) => (short)(lParam.Value & 0xFFFF);

    private static int HighWord(LPARAM lParam) => (short)((lParam.Value >> 16) & 0xFFFF);
}
