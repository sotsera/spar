using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using D8.App.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace D8.App;

/// <summary>Owns the STA window, the WebView2 controller and the message loop.</summary>
internal sealed class Host
{
    private static Host? s_current;

    private readonly Options _options;
    private readonly Stopwatch _clock;
    private HWND _window;
    private UiContext? _ui;
    private ICoreWebView2Controller? _controller;
    private ICoreWebView2? _webView;
    private NavigationCompletedHandler? _navigationHandler;
    private EventRegistrationToken _navigationToken;
    private TaskCompletionSource? _navigated;
    private uint _browserProcessId;
    private Func<Host, Task>? _scenario;

    private Host(Options options, Stopwatch clock)
    {
        _options = options;
        _clock = clock;
    }

    public Options Options => _options;

    public UiContext Ui => _ui ?? throw new InvalidOperationException("Window not created.");

    public ICoreWebView2 WebView => _webView ?? throw new InvalidOperationException("WebView not created.");

    public Dictionary<string, double> StartupMilliseconds { get; } = [];

    public int ExitCode { get; set; }

    public static int Run(Options options, Stopwatch clock, Func<Host, Task> scenario)
    {
        var host = new Host(options, clock);
        s_current = host;
        host._scenario = scenario;
        host.CreateWindow();
        SynchronizationContext.SetSynchronizationContext(host._ui);
        host._ui!.Post(static async state => await ((Host)state!).StartAsync(), host);

        while (PInvoke.GetMessage(out var message, HWND.Null, 0, 0) > 0)
        {
            PInvoke.TranslateMessage(message);
            PInvoke.DispatchMessage(message);
        }

        host.WaitForBrowserExit();
        return host.ExitCode;
    }

    public void Close() => PInvoke.PostMessage(_window, PInvoke.WM_CLOSE, default, default);

    private unsafe void CreateWindow()
    {
        var instance = (HINSTANCE)(nint)PInvoke.GetModuleHandle((char*)null).Value;
        fixed (char* className = "D8AutomationSpike")
        fixed (char* title = "D8 automation spike")
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WindowProcedure,
                hInstance = instance,
                hCursor = PInvoke.LoadCursor(HINSTANCE.Null, PInvoke.IDC_ARROW),
                lpszClassName = className,
            };
            if (PInvoke.RegisterClassEx(windowClass) == 0)
            {
                throw new InvalidOperationException("RegisterClassEx failed: " + Marshal.GetLastPInvokeError());
            }

            var (x, y) = _options.Window == WindowMode.Offscreen ? (-32000, -32000) : (100, 100);
            _window = PInvoke.CreateWindowEx(
                0, className, title, WINDOW_STYLE.WS_OVERLAPPEDWINDOW, x, y, 900, 640,
                HWND.Null, HMENU.Null, instance, null);
            if (_window.IsNull)
            {
                throw new InvalidOperationException("CreateWindowEx failed: " + Marshal.GetLastPInvokeError());
            }
        }

        _ui = new UiContext(_window);
        var show = _options.Window switch
        {
            WindowMode.Hidden => SHOW_WINDOW_CMD.SW_HIDE,
            WindowMode.Minimized => SHOW_WINDOW_CMD.SW_SHOWMINNOACTIVE,
            WindowMode.Offscreen => SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE,
            _ => SHOW_WINDOW_CMD.SW_SHOWNORMAL,
        };
        PInvoke.ShowWindow(_window, show);
    }

    private async Task StartAsync()
    {
        try
        {
            Directory.CreateDirectory(_options.ProfileDirectory);
            var environmentHandler = new EnvironmentCompletedHandler();
            var hr = WebView2Loader.CreateCoreWebView2EnvironmentWithOptions(
                null, _options.ProfileDirectory, 0, environmentHandler);
            Marshal.ThrowExceptionForHR(hr);
            var environment = await environmentHandler.Task;
            Mark("environment");

            var controllerHandler = new ControllerCompletedHandler();
            environment.CreateCoreWebView2Controller(WindowHandle, controllerHandler);
            _controller = await controllerHandler.Task;
            Mark("controller");

            _webView = _controller.GetCoreWebView2();
            _browserProcessId = _webView.GetBrowserProcessId();
            var settings = _webView.GetSettings();
            settings.PutAreDevToolsEnabled(_options.DevtoolsUiEnabled ? 1 : 0);
            ResizeController();
            if (_options.Window == WindowMode.ControllerHidden)
            {
                _controller.PutIsVisible(0);
            }

            _navigated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _navigationHandler = new NavigationCompletedHandler(() => _navigated.TrySetResult());
            _navigationToken = _webView.AddNavigationCompleted(_navigationHandler);
            _webView.NavigateToString(Page.Html);
            await _navigated.Task;
            Mark("navigated");

            await _scenario!(this);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Host failure: " + exception);
            ExitCode = 2;
            Close();
        }
    }

    private unsafe nint WindowHandle => (nint)_window.Value;

    private void Mark(string name)
    {
        StartupMilliseconds[name] = _clock.Elapsed.TotalMilliseconds;
        Console.WriteLine($"[{_clock.Elapsed.TotalMilliseconds,8:F1} ms] {name}");
    }

    private void ResizeController()
    {
        if (_controller is null)
        {
            return;
        }

        PInvoke.GetClientRect(_window, out var client);
        _controller.PutBounds(new Rect { Left = 0, Top = 0, Right = client.right, Bottom = client.bottom });
    }

    private void Shutdown()
    {
        if (_webView is not null && _navigationHandler is not null)
        {
            _webView.RemoveNavigationCompleted(_navigationToken);
            _navigationHandler = null;
        }

        _controller?.Close();
        _controller = null;
        _webView = null;
    }

    private void WaitForBrowserExit()
    {
        if (_browserProcessId == 0)
        {
            return;
        }

        try
        {
            using var browser = Process.GetProcessById((int)_browserProcessId);
            var exited = browser.WaitForExit(TimeSpan.FromSeconds(10));
            Console.WriteLine(exited
                ? $"Browser process {_browserProcessId} exited."
                : $"Browser process {_browserProcessId} still running after 10 s.");
        }
        catch (ArgumentException)
        {
            Console.WriteLine($"Browser process {_browserProcessId} exited.");
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT WindowProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            var host = s_current;
            switch (message)
            {
                case UiContext.DrainMessage:
                    host?._ui?.Drain();
                    return default;
                case PInvoke.WM_SIZE:
                    host?.ResizeController();
                    return default;
                case PInvoke.WM_CLOSE:
                    host?.Shutdown();
                    PInvoke.DestroyWindow(window);
                    return default;
                case PInvoke.WM_DESTROY:
                    PInvoke.PostQuitMessage(0);
                    return default;
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Window procedure failure: " + exception);
        }

        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }
}
