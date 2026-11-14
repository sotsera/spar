using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Checks.Desktop.Interop;
using static Checks.Desktop.Interop.Native;

namespace Checks.Desktop;

// One window on one STA thread: the frame, hit testing, the WebView (windowed or visual hosting) and the bridge.
internal sealed unsafe partial class Host
{
    private const string Origin = "https://app.localhost/";
    private const uint WM_APP_INVOKE = WM_APP + 1;
    private static Host? s_host;

    private readonly Options _o;
    private readonly Log _log;
    private readonly Queue<Action> _invokes = new();
    private nint _hwnd;
    private nint _sink;
    private nint _brush;
    private bool _dark;
    private uint _dpi = 96;

    // WebView: every pointer here holds one reference owned by this host.
    private nint _env;
    private nint _env10;
    private nint _controller;
    private nint _composition;
    private nint _composition4;
    private nint _webView;
    private nint _profile;
    private nint _dcompDevice;
    private nint _dcompTarget;
    private nint _dcompVisual;
    private nint _cursor;
    private long _exitedToken;
    private bool _closing;
    private bool _browserExited;
    private bool _tracking;
    private nuint _exitTimer;

    // Title-bar layout reported by the page, in client pixels. Hit testing reads only this cache.
    private int _barPx;
    private Rect[] _nodrag = [];
    private Rect _minRect;
    private Rect _maxRect;
    private Rect _closeRect;
    private int _hover;
    private int _pressed;

    private readonly List<double> _hitTestUs = [];
    private readonly List<double> _regionAtPointUs = [];
    private readonly Dictionary<int, int> _hitCounts = [];

    public Host(Options options, Log log)
    {
        _o = options;
        _log = log;
        _dark = options.Dark;
    }

    public nint Hwnd => _hwnd;
    public nint Sink => _sink;
    public nint WebView => _webView;
    public nint Composition4 => _composition4;
    public Options Options => _o;
    public Log Log => _log;
    public bool Ready { get; private set; }
    public double ReadyAtMs { get; private set; }
    public string? LastLayoutJson { get; private set; }
    public List<string> PageEvents { get; } = [];
    public int BarPx => _barPx;
    public Rect MinRect => _minRect;
    public Rect MaxRect => _maxRect;
    public Rect CloseRect => _closeRect;
    public Rect[] NoDrag => _nodrag;
    public IReadOnlyList<double> HitTestUs => _hitTestUs;
    public IReadOnlyList<double> RegionAtPointUs => _regionAtPointUs;
    public IReadOnlyDictionary<int, int> HitCounts => _hitCounts;
    public bool BrowserExited => _browserExited;
    public bool Destroyed { get; private set; }
    public event Action? OnReady;

    // Page layout in CSS pixels (the "css" cache), scaled by the window's DPI at hit-test time.
    private double _cssBar;
    private CssRect[] _cssNodrag = [];
    private CssRect _cssMin;
    private CssRect _cssMax;
    private CssRect _cssClose;
    private double _layoutDpr = 1;
    private double _dpiChangedAt = -1;
    private nint _ncProvider;
    private nint _accProps;

    public double LayoutDpr => _layoutDpr;
    public string LastHitSource { get; private set; } = "";
    public List<string> PermissionEvents { get; } = [];

    private bool Custom => _o.Frame == Frame.Custom;
    private bool Visual => _o.Hosting == Hosting.Visual;
    private bool CssCache => _o.Cache == "css";

    public int Run()
    {
        s_host = this;
        Check(OleInitialize(0), "OleInitialize");
        CreateWindow();
        CreateWebView();
        while (GetMessage(out var msg, 0, 0, 0) > 0)
        {
            if (msg.Hwnd == 0 && msg.Message == WM_TIMER && _exitTimer != 0 && (nuint)msg.WParam == _exitTimer)
            {
                _log.Mark("browser-exit-timeout");
                break;
            }

            TranslateMessage(msg);
            DispatchMessage(msg);
        }

        OleUninitialize();
        return 0;
    }

    // Runs an action on the window's thread, from any thread.
    public void Invoke(Action action)
    {
        lock (_invokes)
        {
            _invokes.Enqueue(action);
        }

        PostMessage(_hwnd, WM_APP_INVOKE, 0, 0);
    }

    public T InvokeSync<T>(Func<T> func, int timeoutMs = 5000)
    {
        var done = new ManualResetEventSlim();
        T result = default!;
        Exception? error = null;
        Invoke(() =>
        {
            try { result = func(); }
            catch (Exception e) { error = e; }
            finally { done.Set(); }
        });
        if (!done.Wait(timeoutMs))
        {
            throw new TimeoutException("UI thread did not run the call");
        }

        return error is null ? result : throw new InvalidOperationException("UI call failed", error);
    }

    // ---- Window ----

    private void CreateWindow()
    {
        var instance = GetModuleHandle(0);
        _brush = CreateSolidBrush(Rgb(_dark ? 0x1f1f1f : 0xffffff));
        fixed (char* className = "SparTitlebarSpike")
        fixed (char* sinkClass = "SparTitlebarSink")
        {
            var wc = new WndClassEx
            {
                Size = (uint)sizeof(WndClassEx),
                Style = 0x0002 | 0x0001, // CS_HREDRAW | CS_VREDRAW
                WndProc = &WndProc,
                Instance = instance,
                Cursor = LoadCursor(0, IDC_ARROW),
                Background = _brush,
                ClassName = className,
            };
            Check(RegisterClassEx(wc) == 0 ? -1 : 0, "RegisterClassEx");
            var sc = new WndClassEx
            {
                Size = (uint)sizeof(WndClassEx),
                WndProc = &SinkProc,
                Instance = instance,
                Cursor = LoadCursor(0, IDC_ARROW),
                ClassName = sinkClass,
            };
            Check(RegisterClassEx(sc) == 0 ? -1 : 0, "RegisterClassEx sink");
        }

        // The window is created at the primary monitor's DPI first; WM_DPICHANGED corrects it after placement.
        var (x, y) = InitialPosition();
        _log.Mark("create-window");
        // --no-caption-style: the earlier approach (WS_CAPTION removed), kept for comparison.
        var style = (_o.NoCaptionStyle ? WS_OVERLAPPEDWINDOW & ~WS_CAPTION | 0x00080000 : WS_OVERLAPPEDWINDOW) | WS_CLIPCHILDREN;
        style = _o.Style switch
        {
            "nosysmenu" => style & ~(WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX),
            "nominmax" => style & ~(WS_MINIMIZEBOX | WS_MAXIMIZEBOX),
            _ => style,
        };
        _hwnd = CreateWindowEx(0, "SparTitlebarSpike", "Title bar spike", style,
            x, y, 100, 100, 0, 0, instance, 0);
        Check(_hwnd == 0 ? Marshal.GetLastPInvokeError() | unchecked((int)0x80070000) : 0, "CreateWindowEx");
        _dpi = GetDpiForWindow(_hwnd);
        SetDarkAttribute();
        var w = Scale(_o.WidthDip);
        var h = Scale(_o.HeightDip);
        // SWP_FRAMECHANGED makes the custom WM_NCCALCSIZE apply before the first paint.
        SetWindowPos(_hwnd, 0, x, y, w, h, SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        ExtendFrame();
        var show = _o.Placement == "offscreen" ? SW_SHOWNOACTIVATE : SW_SHOWNORMAL;
        ShowWindow(_hwnd, show);
        _log.Mark("window-visible", ("dpi", _dpi), ("rect", WindowRect().ToString()), ("client", ClientRect().ToString()));
    }

    private (int X, int Y) InitialPosition()
    {
        if (_o.Placement == "offscreen")
        {
            // Left of the whole virtual screen: nothing shows on the user's monitors.
            var left = 0;
            EnumDisplayMonitors(0, 0, &MinLeft, (nint)(&left));
            return (left - Scale(_o.WidthDip) - 400, 100);
        }

        if (_o.Placement.Contains(','))
        {
            var p = _o.Placement.Split(',');
            return (int.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture),
                int.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture));
        }

        return (100, 80);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int MinLeft(nint monitor, nint hdc, Rect* rect, nint data)
    {
        var left = (int*)data;
        *left = Math.Min(*left, rect->Left);
        return 1;
    }

    private int Scale(int dip) => (int)Math.Round(dip * _dpi / 96.0);

    public Rect WindowRect()
    {
        GetWindowRect(_hwnd, out var r);
        return r;
    }

    public Rect ClientRect()
    {
        GetClientRect(_hwnd, out var r);
        return r;
    }

    public uint Dpi => _dpi;

    private void SetDarkAttribute()
    {
        var value = _dark ? 1 : 0;
        DwmSetWindowAttribute(_hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, &value, 4);
    }

    // Height of the top resize band inside the client area when the caption is removed (restored windows only).
    public int TopResizeBand => IsZoomed(_hwnd) != 0
        ? 0
        : GetSystemMetricsForDpi(SM_CYSIZEFRAME, _dpi) + GetSystemMetricsForDpi(SM_CXPADDEDBORDER, _dpi);

    private int CornerWidth => GetSystemMetricsForDpi(SM_CXSIZEFRAME, _dpi) + GetSystemMetricsForDpi(SM_CXPADDEDBORDER, _dpi);

    private void ExtendFrame()
    {
        if (!Custom)
        {
            return;
        }

        // System buttons: DWM draws them inside the client area over the extended frame. Page buttons: the default
        // margin is 0 (the shadow comes from the remaining non-client borders); --top-margin tries others.
        var top = _o.TopMargin >= 0 ? _o.TopMargin : _o.Buttons == Buttons.System ? CaptionHeightPx() : 0;
        var margins = new Margins { Top = top };
        var hr = DwmExtendFrameIntoClientArea(_hwnd, margins);
        _log.Mark("extend-frame", ("top", top), ("hr", hr));
    }

    // Caption height used for the DWM caption buttons: the system caption plus the top frame, at the window's DPI.
    public int CaptionHeightPx() =>
        GetSystemMetricsForDpi(SM_CYCAPTION, _dpi) + GetSystemMetricsForDpi(SM_CYSIZEFRAME, _dpi)
        + GetSystemMetricsForDpi(SM_CXPADDEDBORDER, _dpi);

    public Rect CaptionButtonBounds()
    {
        Rect r;
        DwmGetWindowAttribute(_hwnd, DWMWA_CAPTION_BUTTON_BOUNDS, &r, (uint)sizeof(Rect));
        return r;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        var host = s_host;
        try
        {
            if (host is not null)
            {
                if (host._hwnd == 0)
                {
                    host._hwnd = hwnd;
                }

                return host.OnMessage(hwnd, msg, wParam, lParam);
            }
        }
        catch (Exception e)
        {
            host?._log.Mark("wndproc-error", ("msg", (int)msg), ("error", e.ToString()));
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private nint OnMessage(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (Custom && _o.Buttons == Buttons.System && msg != WM_NCCALCSIZE)
        {
            // DWM handles hit testing and the mouse messages of its own caption buttons.
            var t0 = Stopwatch.GetTimestamp();
            if (DwmDefWindowProc(hwnd, msg, wParam, lParam, out var dwmResult) != 0)
            {
                if (msg == WM_NCHITTEST)
                {
                    RecordHit((int)dwmResult, t0);
                }

                return dwmResult;
            }
        }

        if (msg is WM_NCRBUTTONDOWN or WM_NCRBUTTONUP or 0x007B)
        {
            _log.Mark("right-button", ("msg", $"0x{msg:X}"), ("hit", (long)wParam));
        }

        if (msg is WM_NCLBUTTONDOWN or WM_NCLBUTTONUP or WM_NCLBUTTONDBLCLK or 0x0021 or WM_ACTIVATE or 0x0086 or WM_SYSCOMMAND || (msg == WM_NCMOUSEMOVE && IsButton((int)wParam) && _hover != (int)wParam))
        {
            // 0x0021 WM_MOUSEACTIVATE, 0x0086 WM_NCACTIVATE: the first-click investigation.
            _log.Mark("nc-msg", ("msg", $"0x{msg:X}"), ("wParam", (long)wParam), ("lParam", $"0x{(long)lParam:X}"), ("pressed", _pressed), ("hover", _hover),
                ("foreground", GetForegroundWindow() == _hwnd), ("capture", GetCapture() == hwnd));
        }

        switch (msg)
        {
            case WM_APP_INVOKE:
                RunInvokes();
                return 0;
            case WM_NCCALCSIZE when Custom && wParam != 0:
                return OnNcCalcSize(lParam);
            case WM_NCHITTEST when Custom:
                {
                    var t0 = Stopwatch.GetTimestamp();
                    var code = HitTest(LoWord(lParam), HiWord(lParam), out _);
                    if (code == HTNOWHERE)
                    {
                        code = (int)DefWindowProc(hwnd, msg, wParam, lParam);
                    }

                    RecordHit(code, t0);
                    return code;
                }

            case WM_ACTIVATE:
                ExtendFrame();
                PostState(active: LoWord(wParam) != 0);
                break;
            case WM_ERASEBKGND:
                PaintBackground(wParam);
                return 1;
            case WM_PAINT:
                {
                    var hdc = BeginPaint(hwnd, out var ps);
                    PaintBackground(hdc);
                    EndPaint(hwnd, ps);
                    return 0;
                }

            case WM_SIZE:
                UpdateBounds();
                PostState(active: GetForegroundWindow() == _hwnd);
                break;
            case WM_DPICHANGED:
                {
                    var oldDpi = _dpi;
                    _dpi = (uint)HiWord(wParam);
                    var r = *(Rect*)lParam;
                    SetWindowPos(hwnd, 0, r.Left, r.Top, r.Width, r.Height, SWP_NOZORDER | SWP_NOACTIVATE);
                    _dpiChangedAt = _log.Now;
                    _log.Mark("dpi-changed", ("from", oldDpi), ("dpi", _dpi), ("getDpiForWindow", GetDpiForWindow(hwnd)), ("rect", r.ToString()),
                        ("window", WindowRect().ToString()), ("client", ClientRect().ToString()), ("topBand", TopResizeBand));
                    ExtendFrame();
                    CheckCachesAfterDpiChange();
                    return 0;
                }

            case WM_GETTITLEBARINFOEX when _o.Tbi != "default":
                AnswerTitleBarInfo((TitleBarInfoEx*)lParam);
                return 0;
            case WM_GETOBJECT when _o.NcProvider != "off" && (int)lParam == -25: // UiaRootObjectId
                {
                    if (_ncProvider == 0)
                    {
                        var options = _o.NcProvider switch { "refuse" => 0x2 | 0x40, "nonclient" => 0x2 | 0x4, _ => 0x2 };
                        _ncProvider = Vt.Wrap<IRawElementProviderSimple>(new TopLevelProvider(hwnd, options));
                        _log.Mark("nc-provider", ("options", $"0x{options:X}"));
                    }

                    return UiaReturnRawElementProvider(hwnd, wParam, lParam, _ncProvider);
                }

            case WM_NCMOUSEMOVE when Custom && OwnsButtons:
                SetHover(IsButton((int)wParam) ? (int)wParam : 0);
                if (_hover != 0)
                {
                    var tme = new TrackMouseEventInfo { Size = (uint)sizeof(TrackMouseEventInfo), Flags = TME_LEAVE | TME_NONCLIENT, Hwnd = hwnd };
                    TrackMouseEvent(ref tme);
                    return 0;
                }

                break;
            case WM_NCMOUSELEAVE when Custom && OwnsButtons:
                // The press survives the leave: a click that activates the window raises WM_NCMOUSELEAVE between the
                // button's down and up, and clearing the press here lost every first click (windows-titlebar code).
                _log.Mark("nc-msg", ("msg", "0x2A2"), ("pressed", _pressed), ("hover", _hover));
                SetHover(0);
                break;
            case WM_NCLBUTTONDOWN or WM_NCLBUTTONDBLCLK when Custom && OwnsButtons && IsButton((int)wParam):
                // Own the press: DefWindowProc would run its legacy caption-button tracking.
                _pressed = (int)wParam;
                PostHover();
                return 0;
            case WM_NCLBUTTONUP when Custom && OwnsButtons && IsButton((int)wParam):
                {
                    var button = (int)wParam;
                    var pressed = _pressed;
                    _pressed = 0;
                    PostHover();
                    if (pressed == button)
                    {
                        RunCaptionCommand(button);
                    }

                    return 0;
                }

            case WM_SETCURSOR when Visual && LoWord(lParam) == HTCLIENT && _cursor != 0:
                SetCursor(_cursor);
                return 1;
            case >= WM_MOUSEMOVE and <= WM_MOUSEHWHEEL when Visual:
            case WM_MOUSELEAVE when Visual:
                ForwardMouse(msg, wParam, lParam);
                return 0;
            case WM_NCRBUTTONDOWN or WM_NCRBUTTONUP when Visual && _o.NcrForward && (int)wParam == HTCAPTION:
                {
                    // Documented for visual hosting: WebView2 shows the system menu for its drag regions.
                    var p = new Point { X = LoWord(lParam), Y = HiWord(lParam) };
                    ScreenToClient(hwnd, ref p);
                    if (_composition != 0)
                    {
                        Vt.SendMouseInput(_composition, (int)msg, 0, 0, p);
                    }

                    return 0;
                }

            case WM_NCRBUTTONUP when Visual && _o.NcrHost && (int)wParam == HTCAPTION:
                {
                    // The host shows the system menu itself and runs the chosen command.
                    var menu = GetSystemMenu(hwnd, 0);
                    var cmd = TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, LoWord(lParam), HiWord(lParam), 0, hwnd, 0);
                    _log.Mark("system-menu", ("cmd", cmd));
                    if (cmd != 0)
                    {
                        PostMessage(hwnd, WM_SYSCOMMAND, cmd, 0);
                    }

                    return 0;
                }

            case WM_SETFOCUS when _controller != 0:
                Vt.Call(_controller, 12, 0); // MoveFocus(PROGRAMMATIC)
                break;
            case WM_GETOBJECT when Visual && _o.UiaProvider && (int)lParam == -25 && _composition != 0: // UiaRootObjectId
                {
                    if (Vt.Get(_composition, 11, out var provider) >= 0 && provider != 0)
                    {
                        var simple = Vt.QueryInterface(provider, Iid.RawElementProviderSimple);
                        Vt.Release(ref provider);
                        if (simple != 0)
                        {
                            var r = UiaReturnRawElementProvider(hwnd, wParam, lParam, simple);
                            Vt.Release(ref simple);
                            return r;
                        }
                    }

                    break;
                }

            case WM_SYSCOMMAND:
                _log.Mark("syscommand", ("cmd", $"0x{(long)wParam & 0xFFF0:X4}"));
                break;
            case WM_ENTERSIZEMOVE:
                _log.Mark("enter-size-move");
                break;
            case WM_EXITSIZEMOVE:
                _log.Mark("exit-size-move", ("rect", WindowRect().ToString()));
                break;
            case WM_CLOSE:
                _log.Mark("close");
                BeginClose();
                return 0;
            case WM_DESTROY:
                if (_ncProvider != 0)
                {
                    UiaReturnRawElementProvider(hwnd, 0, 0, 0); // disconnects the server-side provider
                    Vt.Release(ref _ncProvider);
                }

                Vt.Release(ref _accProps);
                Destroyed = true;
                _log.Mark("destroyed");
                if (_browserExited || _env == 0)
                {
                    PostQuitMessage(0);
                }

                return 0;
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void RecordHit(int code, long t0)
    {
        var us = Stopwatch.GetElapsedTime(t0).TotalMicroseconds;
        _hitTestUs.Add(us);
        _hitCounts[code] = _hitCounts.GetValueOrDefault(code) + 1;
    }

    private void RunInvokes()
    {
        while (true)
        {
            Action action;
            lock (_invokes)
            {
                if (_invokes.Count == 0)
                {
                    return;
                }

                action = _invokes.Dequeue();
            }

            action();
        }
    }

    // Removes the caption and the top border; keeps the left, right and bottom resize borders as true non-client area.
    private nint OnNcCalcSize(nint lParam)
    {
        var p = (NcCalcSizeParams*)lParam;
        var originalTop = p->Rect0.Top;
        DefWindowProc(_hwnd, WM_NCCALCSIZE, 1, lParam);
        // Maximized windows extend past the monitor by the frame; keep the client inside the monitor's work area.
        p->Rect0.Top = originalTop + (IsZoomed(_hwnd) != 0 ? TopFrameWhenMaximized() : 0);
        return 0;
    }

    private int TopFrameWhenMaximized() =>
        GetSystemMetricsForDpi(SM_CYSIZEFRAME, _dpi) + GetSystemMetricsForDpi(SM_CXPADDEDBORDER, _dpi);

    // Hit testing reads only the cached layout and, for visual hosting, WebView2's own region lookup.
    public int HitTest(int screenX, int screenY, out string source)
    {
        var p = new Point { X = screenX, Y = screenY };
        ScreenToClient(_hwnd, ref p);
        var client = ClientRect();
        source = "outside";
        if (p.X < 0 || p.Y < 0 || p.X >= client.Right || p.Y >= client.Bottom)
        {
            return HTNOWHERE;
        }

        var band = TopResizeBand;
        if (p.Y < band)
        {
            source = "resize-band";
            var corner = CornerWidth;
            return p.X < corner ? HTTOPLEFT : p.X >= client.Right - corner ? HTTOPRIGHT : HTTOP;
        }

        // "css": the point is converted to CSS pixels with the window's current DPI; "px": the cache holds client
        // pixels computed from the page's devicePixelRatio when the layout arrived.
        var scale = _dpi / 96.0;
        var cx = (p.X + 0.5) / scale;
        var cy = (p.Y + 0.5) / scale;
        if (CssCache ? cy >= _cssBar : p.Y >= _barPx)
        {
            source = "below-bar";
            return HTCLIENT;
        }

        if (_o.Buttons == Buttons.Page)
        {
            source = "button-cache";
            if (CssCache ? _cssMin.Contains(cx, cy) : _minRect.Contains(p.X, p.Y))
            {
                return HTMINBUTTON;
            }

            if (CssCache ? _cssMax.Contains(cx, cy) : _maxRect.Contains(p.X, p.Y))
            {
                return HTMAXBUTTON;
            }

            if (CssCache ? _cssClose.Contains(cx, cy) : _closeRect.Contains(p.X, p.Y))
            {
                return HTCLOSE;
            }
        }
        else if (_o.DwmFix != "none")
        {
            // Windows' own buttons: DwmDefWindowProc answered nothing (it stops when maximized), so the host hit tests
            // the button rectangles itself.
            source = "dwm-fix-" + _o.DwmFix;
            var code = SystemButtonAt(screenX, screenY);
            if (code != 0)
            {
                return code;
            }
        }

        if (Visual && _composition4 != 0)
        {
            source = "webview2-region";
            var t0 = Stopwatch.GetTimestamp();
            var hr = Vt.NonClientRegionAtPoint(_composition4, p, out var kind);
            _regionAtPointUs.Add(Stopwatch.GetElapsedTime(t0).TotalMicroseconds);
            return hr < 0 ? HTCLIENT : kind == 2 ? HTCAPTION : HTCLIENT;
        }

        source = "nodrag-cache";
        if (CssCache)
        {
            foreach (var r in _cssNodrag)
            {
                if (r.Contains(cx, cy))
                {
                    return HTCLIENT;
                }
            }
        }
        else
        {
            foreach (var r in _nodrag)
            {
                if (r.Contains(p.X, p.Y))
                {
                    return HTCLIENT;
                }
            }
        }

        return HTCAPTION;
    }

    // Windows' own buttons: DWM tracks them while restored; the host takes over only when maximized, where
    // DwmDefWindowProc no longer reports them (taking over while restored broke every DWM button click).
    private bool OwnsButtons => _o.Buttons == Buttons.Page || (_o.DwmFix != "none" && IsZoomed(_hwnd) != 0);

    // Screen point to HTMINBUTTON/HTMAXBUTTON/HTCLOSE from Windows' own button rectangles, or 0.
    public int SystemButtonAt(int screenX, int screenY)
    {
        if (_o.DwmFix == "tbi")
        {
            var info = new TitleBarInfoEx { Size = (uint)sizeof(TitleBarInfoEx) };
            SendMessage(_hwnd, WM_GETTITLEBARINFOEX, 0, (nint)(&info));
            foreach (var (index, code) in new[] { (2, HTMINBUTTON), (3, HTMAXBUTTON), (5, HTCLOSE) })
            {
                var r = new Rect { Left = info.Rects[index * 4], Top = info.Rects[(index * 4) + 1], Right = info.Rects[(index * 4) + 2], Bottom = info.Rects[(index * 4) + 3] };
                if ((info.State[index] & STATE_SYSTEM_INVISIBLE) == 0 && r.Contains(screenX, screenY))
                {
                    return code;
                }
            }

            return 0;
        }

        // DWMWA_CAPTION_BUTTON_BOUNDS is in window coordinates; the three buttons share it equally.
        var b = CaptionButtonBounds();
        var w = WindowRect();
        var x = screenX - w.Left;
        var y = screenY - w.Top;
        if (b.Width <= 0 || !b.Contains(x, y))
        {
            return 0;
        }

        var third = (x - b.Left) * 3 / b.Width;
        return third == 0 ? HTMINBUTTON : third == 1 ? HTMAXBUTTON : HTCLOSE;
    }

    public string TitleBarInfoText()
    {
        var info = new TitleBarInfoEx { Size = (uint)sizeof(TitleBarInfoEx) };
        SendMessage(_hwnd, WM_GETTITLEBARINFOEX, 0, (nint)(&info));
        var text = new StringBuilder($"bar={info.TitleBar} ");
        for (var i = 0; i < 6; i++)
        {
            text.Append($"{i}:0x{info.State[i]:X}[{info.Rects[i * 4]},{info.Rects[(i * 4) + 1]},{info.Rects[(i * 4) + 2]},{info.Rects[(i * 4) + 3]}] ");
        }

        return text.ToString();
    }

    // --tbi invisible: every title-bar part reported invisible. --tbi page: the page's buttons in screen pixels.
    private void AnswerTitleBarInfo(TitleBarInfoEx* info)
    {
        var origin = new Point();
        ClientToScreen(_hwnd, ref origin);
        var scale = _dpi / 96.0;
        Rect Screen(CssRect r) => new()
        {
            Left = origin.X + (int)Math.Floor(r.Left * scale),
            Top = origin.Y + (int)Math.Floor(r.Top * scale),
            Right = origin.X + (int)Math.Ceiling(r.Right * scale),
            Bottom = origin.Y + (int)Math.Ceiling(r.Bottom * scale),
        };
        void Set(int index, uint state, Rect r)
        {
            info->State[index] = state;
            info->Rects[index * 4] = r.Left;
            info->Rects[(index * 4) + 1] = r.Top;
            info->Rects[(index * 4) + 2] = r.Right;
            info->Rects[(index * 4) + 3] = r.Bottom;
        }

        var client = ClientRect();
        info->TitleBar = new Rect { Left = origin.X, Top = origin.Y, Right = origin.X + client.Right, Bottom = origin.Y + (int)Math.Round(_cssBar * scale) };
        var hidden = STATE_SYSTEM_INVISIBLE | STATE_SYSTEM_OFFSCREEN;
        if (_o.Tbi == "invisible")
        {
            info->TitleBar = default;
            for (var i = 0; i < 6; i++)
            {
                Set(i, hidden, default);
            }
        }
        else
        {
            Set(0, 0, info->TitleBar);
            Set(1, hidden, default); // reserved
            Set(2, 0, Screen(_cssMin));
            Set(3, 0, Screen(_cssMax));
            Set(4, hidden, default); // help
            Set(5, 0, Screen(_cssClose));
        }

        _tbiAnswers++;
    }

    private int _tbiAnswers;

    public int TbiAnswers => _tbiAnswers;

    // Right after WM_DPICHANGED, before the page has sent a new layout: where do the button centres fall in each cache?
    private void CheckCachesAfterDpiChange()
    {
        if (_o.Buttons != Buttons.Page || _cssMax.Right <= 0)
        {
            return;
        }

        var scale = _dpi / 96.0;
        var report = new StringBuilder();
        foreach (var (name, css, px) in new[] { ("min", _cssMin, _minRect), ("max", _cssMax, _maxRect), ("close", _cssClose, _closeRect) })
        {
            // Where the page will draw the button at the new DPI: CSS layout is unchanged because the window keeps its
            // size in DIP.
            var x = (int)(((css.Left + css.Right) / 2) * scale);
            var y = (int)(((css.Top + css.Bottom) / 2) * scale);
            report.Append($"{name}: centre=({x},{y}) cssCache={(css.Contains((x + 0.5) / scale, (y + 0.5) / scale) ? "hit" : "miss")} pxCache={(px.Contains(x, y) ? "hit" : "miss")} pxRect={px}; ");
        }

        _log.Mark("dpi-cache-check", ("dpi", _dpi), ("layoutDpr", _layoutDpr), ("report", report.ToString()));
    }

    private static bool IsButton(int code) => code is HTMINBUTTON or HTMAXBUTTON or HTCLOSE;

    public void RunCaptionCommand(int button)
    {
        var command = button switch
        {
            HTMINBUTTON => SC_MINIMIZE,
            HTMAXBUTTON => IsZoomed(_hwnd) != 0 ? SC_RESTORE : SC_MAXIMIZE,
            _ => SC_CLOSE,
        };
        _log.Mark("caption-command", ("button", button));
        PostMessage(_hwnd, WM_SYSCOMMAND, (nint)command, 0);
    }

    private void SetHover(int button)
    {
        if (_hover != button)
        {
            _hover = button;
            PostHover();
        }
    }

    private void PostHover()
    {
        var name = _hover switch { HTMINBUTTON => "min", HTMAXBUTTON => "max", HTCLOSE => "close", _ => "" };
        PostJson($$"""{"type":"hover","button":"{{name}}","pressed":{{(_pressed != 0 ? "true" : "false")}}}""");
    }

    private void PostState(bool active) =>
        PostJson($$"""{"type":"state","active":{{(active ? "true" : "false")}},"maximized":{{(IsZoomed(_hwnd) != 0 ? "true" : "false")}}}""");

    private bool _painted;

    private void PaintBackground(nint hdc)
    {
        if (!_painted)
        {
            _painted = true;
            _log.Mark("first-paint");
        }

        var r = ClientRect();
        FillRect(hdc, r, _brush);
        if (Custom && _o.Buttons == Buttons.System)
        {
            // Alpha 0 under the DWM caption buttons, so the extended frame and its buttons show through.
            var b = CaptionButtonBounds();
            var top = new Rect { Left = b.Left, Top = 0, Right = r.Right, Bottom = CaptionHeightPx() };
            FillRect(hdc, top, GetStockObject(4)); // BLACK_BRUSH
        }
    }

    // ---- Sink window (windowed hosting): a host-owned child above the WebView for the top resize band and the
    // caption buttons, so those points reach the top-level window's hit test. ----

    private void CreateSink()
    {
        if (_o.Sink == SinkMode.None || Visual || !Custom)
        {
            return;
        }

        var instance = GetModuleHandle(0);
        // WS_EX_TRANSPARENT: the sink does not clip the WebView below it (WS_EX_NOREDIRECTIONBITMAP did: the buttons
        // under it disappeared), and it still receives hit tests.
        _sink = CreateWindowEx(_o.SinkStyle, "SparTitlebarSink", null, WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS,
            0, 0, 1, 1, _hwnd, 0, instance, 0);
        _log.Mark("sink-created", ("hwnd", (long)_sink), ("mode", _o.Sink.ToString()));
        UpdateSink();
    }

    private void UpdateSink()
    {
        if (_sink == 0)
        {
            return;
        }

        var client = ClientRect();
        var band = TopResizeBand;
        var region = CreateRectRgn(0, 0, client.Right, band);
        void Add(Rect r)
        {
            if (r.Width > 0 && r.Height > 0)
            {
                var part = CreateRectRgn(r.Left, r.Top, r.Right, r.Bottom);
                CombineRgn(region, region, part, 2); // RGN_OR
                DeleteObject(part);
            }
        }

        if (_o.Buttons == Buttons.Page)
        {
            var scale = _dpi / 96.0;
            Add(CssCache ? _cssMin.Scale(scale) : _minRect);
            Add(CssCache ? _cssMax.Scale(scale) : _maxRect);
            Add(CssCache ? _cssClose.Scale(scale) : _closeRect);
        }
        else
        {
            var b = CaptionButtonBounds();
            Add(new Rect { Left = b.Left, Top = 0, Right = client.Right, Bottom = b.Bottom });
        }

        // The sink covers the client area; its region limits it to the band and the buttons. HWND_TOP keeps it
        // above the WebView's child window.
        SetWindowPos(_sink, 0, 0, 0, client.Right, client.Bottom, SWP_NOACTIVATE);
        SetWindowRgn(_sink, region, 1);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint SinkProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        var host = s_host;
        try
        {
            if (host is not null)
            {
                return host.OnSinkMessage(hwnd, msg, wParam, lParam);
            }
        }
        catch (Exception e)
        {
            host?._log.Mark("sinkproc-error", ("error", e.ToString()));
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    public int SinkHitTests { get; private set; }

    private nint OnSinkMessage(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_NCHITTEST:
                SinkHitTests++;
                if (_o.Sink == SinkMode.Transparent)
                {
                    return HTTRANSPARENT;
                }

                {
                    var t0 = Stopwatch.GetTimestamp();
                    var code = HitTest(LoWord(lParam), HiWord(lParam), out _);
                    if (_o.Buttons == Buttons.System)
                    {
                        // DWM answers for its own buttons through the top-level window.
                        DwmDefWindowProc(_hwnd, msg, wParam, lParam, out var dwm);
                        code = dwm != 0 ? (int)dwm : code;
                    }

                    RecordHit(code, t0);
                    return code == HTCLIENT ? HTTRANSPARENT : code;
                }

            case WM_ERASEBKGND:
                return 1;
            case WM_GETOBJECT when _o.Annotate is "subclass" or "subclass-refuse" && (int)lParam == -25:
                if (_sinkProvider == 0)
                {
                    _sinkProvider = Vt.Wrap<IRawElementProviderSimple>(new HiddenPaneProvider(hwnd));
                }

                return UiaReturnRawElementProvider(hwnd, wParam, lParam, _sinkProvider);
            case WM_DESTROY when _sinkProvider != 0:
                UiaReturnRawElementProvider(hwnd, 0, 0, 0);
                Vt.Release(ref _sinkProvider);
                break;
            case WM_PAINT:
                {
                    BeginPaint(hwnd, out var ps);
                    EndPaint(hwnd, ps);
                    return 0;
                }

            case >= WM_NCMOUSEMOVE and <= 0x00A9: // WM_NCMOUSEMOVE .. WM_NCXBUTTONDBLCLK
            case WM_NCMOUSELEAVE:
                // Direct mode: the non-client messages belong to the top-level window.
                return SendMessage(_hwnd, msg, wParam, lParam);
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    // ---- WebView ----

    private string ProfileFolder()
    {
        var root = _o.ProfileRoot ?? Path.Combine(RepoRoot(), "artifacts", "webview2-profiles");
        return Path.GetFullPath(Path.Combine(root, "windows-sandbox-checks", _o.Profile));
    }

    public static string RepoRoot()
    {
        for (var dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (File.Exists(Path.Combine(dir, "global.json")) && Directory.Exists(Path.Combine(dir, "spikes")))
            {
                return dir;
            }
        }

        return Directory.GetCurrentDirectory();
    }

    private void CreateWebView()
    {
        var folder = ProfileFolder();
        if (_o.Fresh && Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        Directory.CreateDirectory(folder);
        _log.Mark("environment-start", ("profile", folder));
        var handler = Vt.Wrap<IEnvironmentCompletedHandler>(new EnvironmentCompleted(OnEnvironment));
        // Additional browser arguments go through the environment options object, never the environment variable.
        var arguments = _o.BrowserArguments;
        var options = arguments.Length > 0 ? Vt.Wrap<IEnvironmentOptions>(new EnvironmentOptions(arguments)) : 0;
        _log.Mark("environment-options", ("additionalBrowserArguments", arguments), ("browserFolder", _o.BrowserFolder));
        Check(Loader.CreateCoreWebView2EnvironmentWithOptions(_o.BrowserFolder, folder, options, handler), "CreateEnvironment");
        Marshal.Release(handler);
        if (options != 0)
        {
            Marshal.Release(options);
        }
    }

    private void OnEnvironment(int hr, nint env)
    {
        _log.Mark("environment-ready", ("hr", hr));
        Check(hr, "environment");
        Marshal.AddRef(env);
        _env = env;
        _env10 = Vt.QueryInterface(env, Iid.Environment10);
        Check(_env10 == 0 ? -1 : 0, "ICoreWebView2Environment10");
        Vt.GetString(env, 5, out var version);
        _log.Mark("runtime", ("version", version));

        var exited = Vt.Wrap<IBrowserProcessExitedHandler>(new BrowserProcessExited(OnBrowserExited));
        Vt.AddHandler(_env10, 12, exited, out _exitedToken);
        Marshal.Release(exited);

        // Transparent default background at creation (ControllerOptions3), so the window colour shows through.
        Check(Vt.Get(_env10, 20, out var options), "CreateCoreWebView2ControllerOptions");
        var options3 = Vt.QueryInterface(options, Iid.ControllerOptions3);
        if (options3 != 0)
        {
            Check(Vt.Call(options3, 10, 0u), "put_DefaultBackgroundColor");
            Vt.Release(ref options3);
        }

        if (Visual)
        {
            var done = Vt.Wrap<ICompositionControllerCompletedHandler>(new CompositionControllerCompleted(OnController));
            Check(Vt.Call(_env10, 22, _hwnd, options, done), "CreateCoreWebView2CompositionControllerWithOptions");
            Marshal.Release(done);
        }
        else
        {
            var done = Vt.Wrap<IControllerCompletedHandler>(new ControllerCompleted(OnController));
            Check(Vt.Call(_env10, 21, _hwnd, options, done), "CreateCoreWebView2ControllerWithOptions");
            Marshal.Release(done);
        }

        Vt.Release(ref options);
    }

    private void OnController(int hr, nint created)
    {
        _log.Mark("controller-ready", ("hr", hr), ("visual", Visual));
        Check(hr, "controller");
        if (_closing)
        {
            return;
        }

        if (Visual)
        {
            Marshal.AddRef(created);
            _composition = created;
            _controller = Vt.QueryInterface(created, Iid.Controller);
            _composition4 = Vt.QueryInterface(created, Iid.CompositionController4);
            _log.Mark("feature", ("ICoreWebView2CompositionController4", _composition4 != 0));
            SetUpComposition();
        }
        else
        {
            _controller = Vt.QueryInterface(created, Iid.Controller);
        }

        Check(Vt.Call(_controller, 4, 1), "put_IsVisible");
        UpdateBounds();
        Check(Vt.Get(_controller, 25, out _webView), "get_CoreWebView2");

        Check(Vt.Get(_webView, 3, out var settings), "get_Settings");
        var settings9 = Vt.QueryInterface(settings, Iid.Settings9);
        _log.Mark("feature", ("ICoreWebView2Settings9", settings9 != 0));
        if (settings9 != 0)
        {
            Check(Vt.Call(settings9, 38, _o.NonClientSupport ? 1 : 0), "put_IsNonClientRegionSupportEnabled");
            Vt.Call(settings9, 12, 0); // AreDevToolsEnabled
            Vt.Call(settings9, 24, 0); // AreBrowserAcceleratorKeysEnabled
            Vt.Release(ref settings9);
        }

        Vt.Release(ref settings);

        var wv13 = Vt.QueryInterface(_webView, Iid.WebView13);
        if (wv13 != 0)
        {
            Vt.Get(wv13, 105, out _profile);
            Vt.Release(ref wv13);
            ApplyColorScheme();
        }

        var wr = Vt.Wrap<IWebResourceRequestedHandler>(new WebResourceRequested(OnWebResourceRequested));
        Check(Vt.AddHandler(_webView, 55, wr, out _), "add_WebResourceRequested");
        Marshal.Release(wr);
        var wv22 = Vt.QueryInterface(_webView, Iid.WebView22);
        Check(wv22 == 0 ? -1 : 0, "ICoreWebView2_22");
        Check(Vt.AddFilterWithSourceKinds(wv22, Origin + "*", 0, 0xFFFFFFFF), "AddWebResourceRequestedFilterWithRequestSourceKinds");
        Vt.Release(ref wv22);

        var wm = Vt.Wrap<IWebMessageReceivedHandler>(new WebMessageReceived(OnWebMessage));
        Check(Vt.AddHandler(_webView, 34, wm, out _), "add_WebMessageReceived");
        Marshal.Release(wm);
        var nav = Vt.Wrap<INavigationCompletedHandler>(new NavigationCompleted((_, _) => _log.Mark("navigation-completed")));
        Vt.AddHandler(_webView, 15, nav, out _);
        Marshal.Release(nav);
        var permission = Vt.Wrap<IPermissionRequestedHandler>(new PermissionRequested(OnPermissionRequested));
        Check(Vt.AddHandler(_webView, 23, permission, out _), "add_PermissionRequested");
        Marshal.Release(permission);

        if (Visual)
        {
            var cursor = Vt.Wrap<ICursorChangedHandler>(new CursorChanged((sender, _) =>
            {
                Vt.Get(sender, 7, out _cursor);
                SetCursor(_cursor);
            }));
            Vt.AddHandler(_composition, 9, cursor, out _);
            Marshal.Release(cursor);
            if (_composition4 != 0)
            {
                var changed = Vt.Wrap<INonClientRegionChangedHandler>(new NonClientRegionChanged(OnRegionChanged));
                Vt.AddHandler(_composition4, 18, changed, out _);
                Marshal.Release(changed);
            }
        }

        CreateSink();
        var query = $"?buttons={(Custom ? _o.Buttons.ToString().ToLowerInvariant() : "none")}&bar={(Custom ? 1 : 0)}&h={_o.BarDip}"
            + (_o.CaptionDrag ? "&capdrag=1" : "") + $"&reserve={(int)Math.Ceiling(CaptionButtonBounds().Width * 96.0 / _dpi)}";
        _log.Mark("navigate", ("query", query));
        Check(Vt.PutString(_webView, 5, Origin + _o.Page + query), "Navigate");
    }

    public void Navigate(string page) => Check(Vt.PutString(_webView, 5, Origin + page), "Navigate");

    // ---- Permissions ----

    private static string KindName(int kind) => kind switch
    {
        0 => "unknown",
        1 => "microphone",
        2 => "camera",
        3 => "geolocation",
        4 => "notifications",
        5 => "other-sensors",
        6 => "clipboard-read",
        _ => kind.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    private static string StateName(int state) => state switch { 0 => "default", 1 => "allow", 2 => "deny", _ => state.ToString(System.Globalization.CultureInfo.InvariantCulture) };

    private void OnPermissionRequested(nint sender, nint args)
    {
        Vt.GetString(args, 3, out var uri);
        Vt.GetInt(args, 4, out var kind);
        Vt.GetInt(args, 5, out var userInitiated);
        Vt.GetInt(args, 6, out var state);
        var args3 = Vt.QueryInterface(args, Iid.PermissionArgs3);
        var saves = -1;
        if (args3 != 0)
        {
            Vt.GetInt(args3, 11, out saves);
        }

        var answer = _o.Permission switch { "allow" => 1, "deny" => 2, "default" => 0, _ => -1 };
        if (answer >= 0)
        {
            Check(Vt.Call(args, 7, answer), "put_State");
        }

        if (args3 != 0 && _o.Saves != "keep")
        {
            Check(Vt.Call(args3, 12, _o.Saves == "true" ? 1 : 0), "put_SavesInProfile");
        }

        var savesAfter = -1;
        if (args3 != 0)
        {
            Vt.GetInt(args3, 11, out savesAfter);
            Vt.Release(ref args3);
        }

        var line = $"kind={KindName(kind)} uri={uri} userInitiated={userInitiated != 0} initialState={StateName(state)} answer={(answer < 0 ? "none" : StateName(answer))} savesInProfile={saves}->{savesAfter}";
        lock (PermissionEvents)
        {
            PermissionEvents.Add(line);
        }

        _log.Mark("permission-requested", ("kind", KindName(kind)), ("uri", uri), ("userInitiated", userInitiated != 0),
            ("initialState", StateName(state)), ("answer", answer < 0 ? "none" : StateName(answer)), ("savesInProfile", saves), ("savesInProfileAfter", savesAfter));
    }

    private nint Profile4()
    {
        var p4 = _profile == 0 ? 0 : Vt.QueryInterface(_profile, Iid.Profile4);
        _log.Mark("feature", ("ICoreWebView2Profile4", p4 != 0));
        return p4;
    }

    // ICoreWebView2Profile4::GetNonDefaultPermissionSettings (slot 16).
    public void ListPermissions(Action<string> done)
    {
        var p4 = Profile4();
        if (p4 == 0)
        {
            done("no ICoreWebView2Profile4");
            return;
        }

        var handler = Vt.Wrap<IGetNonDefaultPermissionSettingsCompletedHandler>(new GetPermissionsCompleted((hr, view) =>
        {
            var items = new List<string>();
            if (hr >= 0 && view != 0)
            {
                Vt.GetInt(view, 4, out var count);
                for (uint i = 0; i < (uint)count; i++)
                {
                    nint setting = 0;
                    ((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)(*(void***)view)[3])(view, i, &setting);
                    if (setting == 0)
                    {
                        continue;
                    }

                    Vt.GetInt(setting, 3, out var kind);
                    Vt.GetString(setting, 4, out var origin);
                    Vt.GetInt(setting, 5, out var state);
                    items.Add($"{KindName(kind)}|{origin}|{StateName(state)}");
                    Vt.Release(ref setting);
                }
            }

            done($"hr=0x{hr:X8} count={items.Count} [{string.Join("; ", items)}]");
        }));
        var call = ((delegate* unmanaged[Stdcall]<nint, nint, int>)(*(void***)p4)[16])(p4, handler);
        Marshal.Release(handler);
        Vt.Release(ref p4);
        if (call < 0)
        {
            done($"call failed 0x{call:X8}");
        }
    }

    // ICoreWebView2Profile4::SetPermissionState(kind, origin, state, handler) (slot 15).
    public void SetPermission(int kind, string origin, int state, Action<string> done)
    {
        var p4 = Profile4();
        if (p4 == 0)
        {
            done("no ICoreWebView2Profile4");
            return;
        }

        var handler = Vt.Wrap<ISetPermissionStateCompletedHandler>(new SetPermissionStateCompleted(hr => done($"hr=0x{hr:X8}")));
        int call;
        fixed (char* o = origin)
        {
            call = ((delegate* unmanaged[Stdcall]<nint, int, char*, int, nint, int>)(*(void***)p4)[15])(p4, kind, o, state, handler);
        }

        Marshal.Release(handler);
        Vt.Release(ref p4);
        if (call < 0)
        {
            done($"call failed 0x{call:X8}");
        }
    }

    // ---- Accessibility: dynamic annotation (IAccPropServices) of WebView2's drag-region windows and the title bar ----

    public string Annotate()
    {
        if (_o.Annotate == "none")
        {
            return "off";
        }

        var done = new List<string>();
        if (_o.Annotate is "subclass" or "subclass-refuse")
        {
            // Subclass WebView2's drag-region windows (same process, UI thread) and answer their WM_GETOBJECT with a
            // provider that removes them from the control and content views. The sink answers in SinkProc.
            foreach (var child in Probe.ChildWindows(_hwnd))
            {
                var ex = (long)GetWindowLongPtr(child, -20);
                if (ClassNameOf(child) == "Chrome_WidgetWin_0" && (ex & 0x20) != 0 && !s_subclassed.ContainsKey(child))
                {
                    var previous = SetWindowLongPtr(child, -4, (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&PaneProc);
                    s_subclassed[child] = (previous, 0);
                    done.Add($"subclassed:{child:X}");
                }
            }

            return $"{done.Count} new, {s_subclassed.Count} total";
        }

        if (_accProps == 0)
        {
            Check(CoCreateInstance(Iid.AccPropServicesClsid, 0, 1, Iid.AccPropServices, out _accProps), "CoCreateInstance(AccPropServices)");
        }

        if (_o.Annotate is "panes" or "both")
        {
            foreach (var child in Probe.ChildWindows(_hwnd))
            {
                var cls = ClassNameOf(child);
                var ex = (long)GetWindowLongPtr(child, -20);
                // WebView2's drag-region windows (transparent Chrome_WidgetWin_0) and the host's sink.
                if ((cls == "Chrome_WidgetWin_0" && (ex & 0x20) != 0) || child == _sink)
                {
                    var hr1 = SetHwndBool(child, OBJID_CLIENT, Iid.IsControlElementProperty, false);
                    var hr2 = SetHwndBool(child, OBJID_CLIENT, Iid.IsContentElementProperty, false);
                    done.Add($"{cls}:{hr1:X}/{hr2:X}");
                }
            }
        }

        if (_o.Annotate is "titlebar" or "both")
        {
            var hr1 = SetHwndBool(_hwnd, OBJID_TITLEBAR, Iid.IsControlElementProperty, false);
            var hr2 = SetHwndBool(_hwnd, OBJID_TITLEBAR, Iid.IsContentElementProperty, false);
            done.Add($"titlebar:{hr1:X}/{hr2:X}");
        }

        return string.Join(" ", done);
    }

    private static readonly Dictionary<nint, (nint Previous, nint Provider)> s_subclassed = [];

    public int PaneQueries { get; private set; }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint PaneProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (!s_subclassed.TryGetValue(hwnd, out var entry))
        {
            return DefWindowProc(hwnd, msg, wParam, lParam);
        }

        if (msg == WM_GETOBJECT && (int)lParam == -25)
        {
            if (entry.Provider == 0)
            {
                entry.Provider = Vt.Wrap<IRawElementProviderSimple>(new HiddenPaneProvider(hwnd));
                s_subclassed[hwnd] = entry;
            }

            if (s_host is not null)
            {
                s_host.PaneQueries++;
            }

            return UiaReturnRawElementProvider(hwnd, wParam, lParam, entry.Provider);
        }

        if (msg == 0x0082) // WM_NCDESTROY: put the original procedure back and drop the provider
        {
            SetWindowLongPtr(hwnd, -4, entry.Previous);
            s_subclassed.Remove(hwnd);
            if (entry.Provider != 0)
            {
                UiaReturnRawElementProvider(hwnd, 0, 0, 0);
                Marshal.Release(entry.Provider);
            }
        }

        return CallWindowProc(entry.Previous, hwnd, msg, wParam, lParam);
    }

    private nint _sinkProvider;

    // IAccPropServices::SetHwndProp(hwnd, idObject, idChild, idProp, VARIANT) (slot 6), VT_BOOL.
    private int SetHwndBool(nint hwnd, int objectId, in Guid property, bool value)
    {
        var variant = new Variant { Type = 11, Value = (short)(value ? -1 : 0) };
        return ((delegate* unmanaged[Stdcall]<nint, nint, uint, uint, Guid, Variant, int>)(*(void***)_accProps)[6])(
            _accProps, hwnd, unchecked((uint)objectId), 0, property, variant);
    }

    private void SetUpComposition()
    {
        Check(DCompositionCreateDevice(0, Iid.DCompositionDevice, out _dcompDevice), "DCompositionCreateDevice");
        // IDCompositionDevice: 3 Commit, 6 CreateTargetForHwnd(hwnd, topmost, out target), 7 CreateVisual(out visual).
        nint target = 0, visual = 0;
        var hr = ((delegate* unmanaged[Stdcall]<nint, nint, int, nint*, int>)(*(void***)_dcompDevice)[6])(_dcompDevice, _hwnd, 1, &target);
        Check(hr, "CreateTargetForHwnd");
        hr = ((delegate* unmanaged[Stdcall]<nint, nint*, int>)(*(void***)_dcompDevice)[7])(_dcompDevice, &visual);
        Check(hr, "CreateVisual");
        _dcompTarget = target;
        _dcompVisual = visual;
        Check(Vt.Call(_dcompTarget, 3, _dcompVisual), "IDCompositionTarget.SetRoot");
        Check(Vt.Call(_composition, 4, _dcompVisual), "put_RootVisualTarget");
        Check(Vt.Call(_dcompDevice, 3), "IDCompositionDevice.Commit");
    }

    private void UpdateBounds()
    {
        if (_controller != 0)
        {
            Vt.PutRect(_controller, 6, ClientRect());
            if (_dcompDevice != 0)
            {
                Vt.Call(_dcompDevice, 3);
            }
        }

        UpdateSink();
    }

    private void ForwardMouse(uint msg, nint wParam, nint lParam)
    {
        if (_composition == 0)
        {
            return;
        }

        var p = new Point { X = LoWord(lParam), Y = HiWord(lParam) };
        uint data = 0;
        var keys = LoWord(wParam) & 0xFFFF;
        if (msg is WM_MOUSEWHEEL or WM_MOUSEHWHEEL)
        {
            ScreenToClient(_hwnd, ref p); // wheel messages carry screen coordinates
            data = (uint)(short)HiWord(wParam);
        }
        else if (msg is >= 0x020B and <= 0x020D)
        {
            data = (uint)HiWord(wParam); // XBUTTON1/2
        }

        if (msg == WM_MOUSEMOVE && !_tracking)
        {
            var tme = new TrackMouseEventInfo { Size = (uint)sizeof(TrackMouseEventInfo), Flags = TME_LEAVE, Hwnd = _hwnd };
            TrackMouseEvent(ref tme);
            _tracking = true;
        }
        else if (msg == WM_MOUSELEAVE)
        {
            _tracking = false;
        }

        if (msg is 0x0201 or 0x0204 or 0x0207 or 0x020B)
        {
            SetCapture(_hwnd);
        }
        else if (msg is 0x0202 or 0x0205 or 0x0208 or 0x020C)
        {
            ReleaseCapture();
        }

        Vt.SendMouseInput(_composition, (int)msg, keys, data, p);
    }

    private void OnRegionChanged(nint sender, nint args)
    {
        Vt.GetInt(args, 3, out var kind);
        var rects = new List<string>();
        nint collection = 0;
        var hr = ((delegate* unmanaged[Stdcall]<nint, int, nint*, int>)(*(void***)_composition4)[17])(_composition4, kind, &collection);
        if (hr >= 0 && collection != 0)
        {
            Vt.GetInt(collection, 3, out var count);
            for (uint i = 0; i < count; i++)
            {
                Vt.GetRect(collection, 4, i, out var r);
                rects.Add(r.ToString());
            }

            Vt.Release(ref collection);
        }

        _log.Mark("nonclient-region-changed", ("kind", kind), ("rects", string.Join(" ", rects)));
    }

    public void SetTheme(bool dark)
    {
        _dark = dark;
        var old = _brush;
        _brush = CreateSolidBrush(Rgb(dark ? 0x1f1f1f : 0xffffff));
        SetDarkAttribute();
        ApplyColorScheme();
        InvalidateRect(_hwnd, 0, 1);
        DeleteObject(old);
        _log.Mark("theme-applied", ("dark", dark));
    }

    private void ApplyColorScheme()
    {
        if (_profile != 0)
        {
            Vt.Call(_profile, 9, _dark ? 2 : 1);
        }
    }

    private void OnWebResourceRequested(nint sender, nint args)
    {
        Check(Vt.Get(args, 3, out var request), "get_Request");
        Vt.GetString(request, 3, out var uri);
        Vt.Release(ref request);
        var path = uri is not null && uri.StartsWith(Origin, StringComparison.Ordinal) ? uri[Origin.Length..] : "";
        var query = path.IndexOfAny(['?', '#']);
        path = query >= 0 ? path[..query] : path;
        var body = ReadPage(path);
        var status = body is null ? 404 : 200;
        body ??= "not found"u8.ToArray();
        var type = Path.GetExtension(path) switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            _ => "text/plain; charset=utf-8",
        };
        var headers = $"Content-Type: {type}\r\nCache-Control: no-store\r\n"
            + "Content-Security-Policy: default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self'; media-src 'self' blob: mediastream:";
        if (_o.PermissionsPolicyHeader is { } policy && type.StartsWith("text/html", StringComparison.Ordinal))
        {
            headers += "\r\nPermissions-Policy: " + policy;
        }
        nint stream;
        fixed (byte* b = body)
        {
            stream = SHCreateMemStream(b, (uint)body.Length);
        }

        Check(Vt.CreateResponse(_env, stream, status, status == 200 ? "OK" : "Not Found", headers, out var response), "CreateWebResourceResponse");
        Vt.Release(ref stream);
        Check(Vt.Call(args, 5, response), "put_Response");
        Vt.Release(ref response);
    }

    private static byte[]? ReadPage(string path)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("page/" + path);
        if (stream is null)
        {
            return null;
        }

        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private void OnWebMessage(nint sender, nint args)
    {
        Vt.GetString(args, 3, out var source);
        if (source is null || !source.StartsWith(Origin, StringComparison.Ordinal))
        {
            _log.Mark("message-rejected", ("source", source));
            return;
        }

        Vt.GetString(args, 4, out var json);
        if (json is null)
        {
            return;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var type = root.GetProperty("type").GetString();
        switch (type)
        {
            case "layout":
                ApplyLayout(root);
                LastLayoutJson = json;
                break;
            case "ready":
                if (!Ready)
                {
                    Ready = true;
                    ReadyAtMs = _log.Now;
                    _log.Mark("ui-ready", ("pageMs", root.GetProperty("t").GetDouble()), ("pageDark", root.GetProperty("dark").GetBoolean()));
                    PostState(active: GetForegroundWindow() == _hwnd);
                    OnReady?.Invoke();
                }

                break;
            case "command":
                var command = root.GetProperty("command").GetString();
                _log.Mark("page-command", ("command", command));
                RunCaptionCommand(command switch { "min" => HTMINBUTTON, "max" => HTMAXBUTTON, _ => HTCLOSE });
                break;
            default:
                lock (PageEvents)
                {
                    PageEvents.Add(json);
                }

                _log.Mark("page", ("json", json));
                break;
        }
    }

    private void ApplyLayout(JsonElement root)
    {
        // CSS pixels to client pixels. The page fills the client area, so the WebView origin is the client origin.
        var scale = root.GetProperty("dpr").GetDouble();
        Rect ToRect(JsonElement e) => new()
        {
            Left = (int)Math.Floor(e[0].GetDouble() * scale),
            Top = (int)Math.Floor(e[1].GetDouble() * scale),
            Right = (int)Math.Ceiling(e[2].GetDouble() * scale),
            Bottom = (int)Math.Ceiling(e[3].GetDouble() * scale),
        };
        _barPx = (int)Math.Round(root.GetProperty("barHeight").GetDouble() * scale);
        _nodrag = [.. root.GetProperty("nodrag").EnumerateArray().Select(ToRect)];
        _minRect = root.GetProperty("min").ValueKind == JsonValueKind.Array ? ToRect(root.GetProperty("min")) : default;
        _maxRect = root.GetProperty("max").ValueKind == JsonValueKind.Array ? ToRect(root.GetProperty("max")) : default;
        _closeRect = root.GetProperty("close").ValueKind == JsonValueKind.Array ? ToRect(root.GetProperty("close")) : default;
        static CssRect Css(JsonElement e) => e.ValueKind == JsonValueKind.Array
            ? new CssRect(e[0].GetDouble(), e[1].GetDouble(), e[2].GetDouble(), e[3].GetDouble())
            : default;
        _layoutDpr = scale;
        _cssBar = root.GetProperty("barHeight").GetDouble();
        _cssNodrag = [.. root.GetProperty("nodrag").EnumerateArray().Select(Css)];
        _cssMin = Css(root.GetProperty("min"));
        _cssMax = Css(root.GetProperty("max"));
        _cssClose = Css(root.GetProperty("close"));
        var sinceDpi = _dpiChangedAt >= 0 ? _log.Now - _dpiChangedAt : -1;
        _dpiChangedAt = -1;
        _log.Mark("layout", ("dpr", scale), ("windowDpi", _dpi), ("barCss", _cssBar), ("barPx", _barPx), ("maxCss", _cssMax.ToString()),
            ("maxPx", _maxRect.ToString()), ("msSinceDpiChange", sinceDpi), ("appRegion", root.GetProperty("appRegionValues").GetRawText()));
        UpdateSink();
        if (_o.Annotate != "none")
        {
            // WebView2 recreates its drag-region windows when the regions change, so annotate after every layout,
            // once the new windows exist.
            Invoke(() => _log.Mark("annotate", ("result", Annotate())));
        }
    }

    public void PostJson(string json)
    {
        if (_webView != 0 && !_closing)
        {
            Vt.PutString(_webView, 32, json);
        }
    }

    public void ExecuteScript(string script, Action<string?> done)
    {
        var handler = Vt.Wrap<IExecuteScriptCompletedHandler>(new ExecuteScriptCompleted((_, r) => done(r)));
        Vt.ExecuteScript(_webView, script, handler);
        Marshal.Release(handler);
    }

    // ---- Shutdown: close the controller on this thread, then wait for the browser process before quitting. ----

    public void BeginClose()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        if (_controller != 0)
        {
            Vt.Call(_controller, 24); // Close
        }

        Vt.Release(ref _profile);
        Vt.Release(ref _webView);
        Vt.Release(ref _composition4);
        Vt.Release(ref _controller);
        Vt.Release(ref _composition);
        Vt.Release(ref _dcompVisual);
        Vt.Release(ref _dcompTarget);
        Vt.Release(ref _dcompDevice);
        DestroyWindow(_hwnd);
        if (_env == 0)
        {
            PostQuitMessage(0);
            return;
        }

        // Bounded wait for BrowserProcessExited while the pump keeps running.
        _exitTimer = SetTimer(0, 0, 15000, 0); // thread timer: ends the loop if the browser never reports its exit
        Vt.Release(ref _env);
    }

    private void OnBrowserExited(nint environment, nint args)
    {
        _browserExited = true;
        _log.Mark("browser-exited");
        Vt.RemoveHandler(_env10, 13, _exitedToken);
        Vt.Release(ref _env10);
        if (Destroyed)
        {
            PostQuitMessage(0);
        }
    }

    private static void Check(int hr, string what) => Vt.Check(hr, what);
}
