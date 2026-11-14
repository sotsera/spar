using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Csp.Shared;
using Csp.Windows.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Csp.Windows;

/// <summary>
/// Spike WebView2 host: serves the app origin through WebResourceRequested with a header profile, injects the boot
/// script, locks navigation to the app origin, blocks new windows, denies permissions, and performs trusted clicks
/// (in-process CDP) when the page asks for them.
/// </summary>
internal sealed class DesktopHost
{
    public const string AppOrigin = "https://app.spar.invalid";

    private static DesktopHost? s_current;

    private readonly HostOptions _options;
    private readonly string _origin;
    private readonly AppRoutes _routes;
    private readonly List<object> _handlers = [];
    private HWND _window;
    private UiContext? _ui;
    private ICoreWebView2Environment? _environment;
    private ICoreWebView2Controller? _controller;
    private ICoreWebView2? _webView;
    private uint _browserProcessId;
    private bool _closing;

    private DesktopHost(HostOptions options)
    {
        _options = options;
        _origin = options.DevUrl is { } dev ? new Uri(dev).GetLeftPart(UriPartial.Authority) : AppOrigin;
        var external = $"http://127.0.0.1:{options.ExternalPort}";
        var profile = HeaderProfile.Load(options, _origin, external);
        Log.Write($"HOST windows-webview2 origin={_origin} profile={profile.Name} csp={profile.Csp ?? "-"} meta={profile.Meta ?? "-"} navlock={options.NavigationLock} inject={options.Inject}");
        _routes = new AppRoutes(options.Roots, profile, options.Tamper, OnPageMessage);
    }

    public static int Run(HostOptions options)
    {
        var exitCode = 0;
        using var external = new MiniHttp(options.ExternalPort, ExternalServer.Create(options.Label));
        var thread = new Thread(() => exitCode = new DesktopHost(options).RunOnSta()) { Name = "Spar UI" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return exitCode;
    }

    private int RunOnSta()
    {
        s_current = this;
        CreateWindow();
        SynchronizationContext.SetSynchronizationContext(_ui);
        _ui!.Post(static async state => await ((DesktopHost)state!).StartAsync(), this);
        while (PInvoke.GetMessage(out var message, HWND.Null, 0, 0) > 0)
        {
            PInvoke.TranslateMessage(message);
            PInvoke.DispatchMessage(message);
        }

        WaitForBrowserExit();
        return 0;
    }

    private unsafe void CreateWindow()
    {
        var instance = (HINSTANCE)(nint)PInvoke.GetModuleHandle((char*)null).Value;
        fixed (char* className = "SparCspSpike")
        fixed (char* title = "Csp Spike")
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WindowProcedure,
                hInstance = instance,
                hCursor = PInvoke.LoadCursor(HINSTANCE.Null, PInvoke.IDC_ARROW),
                lpszClassName = className,
            };
            PInvoke.RegisterClassEx(windowClass);
            var (x, y) = _options.Offscreen ? (-32000, -32000) : (120, 120);
            _window = PInvoke.CreateWindowEx(0, className, title, WINDOW_STYLE.WS_OVERLAPPEDWINDOW, x, y, 1000, 760, HWND.Null, HMENU.Null, instance, null);
        }

        _ui = new UiContext(_window);
        PInvoke.ShowWindow(_window, _options.Offscreen ? SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE : SHOW_WINDOW_CMD.SW_SHOWNORMAL);
    }

    private async Task StartAsync()
    {
        try
        {
            var profile = HostOptions.ProfileDirectory("csp-navigation-desktop");
            Directory.CreateDirectory(profile);
            var environmentHandler = new EnvironmentCompletedHandler();
            Marshal.ThrowExceptionForHR(NativeLoader.CreateCoreWebView2EnvironmentWithOptions(null, profile, 0, environmentHandler));
            _environment = await environmentHandler.Task;
            var controllerHandler = new ControllerCompletedHandler();
            _environment.CreateCoreWebView2Controller(WindowHandle, controllerHandler);
            _controller = await controllerHandler.Task;
            _webView = _controller.GetCoreWebView2();
            _browserProcessId = _webView.GetBrowserProcessId();
            Resize();

            if (_options.Inject)
            {
                var added = new ScriptAddedHandler();
                _webView.AddScriptToExecuteOnDocumentCreated(Injected.Script, added);
                Log.Write("INJECT_ADDED id=" + await added.Task);
            }

            var resources = new WebResourceRequestedHandler(OnWebResourceRequested);
            _handlers.Add(resources);
            _webView.AddWebResourceRequested(resources);
            _webView.AddWebResourceRequestedFilter(_options.DevUrl is null ? AppOrigin + "/*" : _origin + "/_spar/*", 0);
            if (_options.Has("--intercept-foreign-documents"))
            {
                // Every document request (top level and frames): answered here, so a cancelled navigation never reaches the network.
                _webView.AddWebResourceRequestedFilter("*", 1); // COREWEBVIEW2_WEB_RESOURCE_CONTEXT_DOCUMENT
            }

            var top = new NavigationStartingHandler(args => OnNavigationStarting(args, frame: false));
            var frame = new NavigationStartingHandler(args => OnNavigationStarting(args, frame: true));
            var completed = new NavigationCompletedHandler(OnNavigationCompleted);
            var windows = new NewWindowRequestedHandler(OnNewWindowRequested);
            var permissions = new PermissionRequestedHandler(OnPermissionRequested);
            var messages = new WebMessageReceivedHandler(OnWebMessageReceived);
            _handlers.AddRange([top, frame, completed, windows, permissions, messages]);
            _webView.AddNavigationStarting(top);
            _webView.AddFrameNavigationStarting(frame);
            _webView.AddNavigationCompleted(completed);
            _webView.AddNewWindowRequested(windows);
            _webView.AddPermissionRequested(permissions);
            _webView.AddWebMessageReceived(messages);

            var start = _options.StartUrl ?? (_options.DevUrl is { } dev ? new Uri(new Uri(dev), _options.Page).ToString() : AppOrigin + _options.Page);
            Log.Write("NAVIGATE " + start);
            _webView.Navigate(start);
            _ = TimeoutAsync();
        }
        catch (Exception exception)
        {
            Log.Write("HOST_FAILURE " + exception);
            Close();
        }
    }

    private async Task TimeoutAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        if (!_closing)
        {
            Log.Write("TIMEOUT");
            Close();
        }
    }

    // ---- Navigation lock ----

    private bool IsAppUri(string uri, bool frame) =>
        uri == _origin || uri.StartsWith(_origin + "/", StringComparison.Ordinal)
        || (frame && (uri == "about:blank" || uri == "about:srcdoc"));

    private void OnNavigationStarting(ICoreWebView2NavigationStartingEventArgs args, bool frame)
    {
        var uri = args.GetUri();
        var kind = frame ? "FRAME" : "TOP";
        var detail = $"{kind} user={args.GetIsUserInitiated()} redirected={args.GetIsRedirected()} {uri}";
        if (_options.NavigationLock && !IsAppUri(uri, frame))
        {
            args.PutCancel(1);
            Log.Write("NAV_BLOCKED " + detail);
            return;
        }

        Log.Write("NAV_ALLOWED " + detail);
    }

    private void OnNavigationCompleted(ICoreWebView2NavigationCompletedEventArgs args) =>
        Log.Write($"NAV_COMPLETED success={args.GetIsSuccess()} status={args.GetWebErrorStatus()} source={_webView?.GetSource()}");

    private void OnNewWindowRequested(ICoreWebView2NewWindowRequestedEventArgs args)
    {
        // Always handled: no WebView2 popup window is ever created, even with the lock off (it would show on the desktop).
        args.PutHandled(1);
        Log.Write($"NEW_WINDOW_BLOCKED user={args.GetIsUserInitiated()} {args.GetUri()}");
    }

    private void OnPermissionRequested(ICoreWebView2PermissionRequestedEventArgs args)
    {
        args.PutState(2); // COREWEBVIEW2_PERMISSION_STATE_DENY
        Log.Write($"PERMISSION_DENIED kind={PermissionName(args.GetPermissionKind())} user={args.GetIsUserInitiated()} {args.GetUri()}");
    }

    private static string PermissionName(int kind) => kind switch
    {
        1 => "microphone",
        2 => "camera",
        3 => "geolocation",
        4 => "notifications",
        5 => "other-sensors",
        6 => "clipboard-read",
        _ => "kind" + kind.ToString(CultureInfo.InvariantCulture),
    };

    private void OnWebMessageReceived(ICoreWebView2WebMessageReceivedEventArgs args)
    {
        args.TryGetWebMessageAsString(out var message);
        Log.Write($"NATIVE source={args.GetSource()} {message}");
    }

    // ---- The app origin ----

    private void OnWebResourceRequested(ICoreWebView2WebResourceRequestedEventArgs args)
    {
        var request = args.GetRequest();
        var uri = new Uri(request.GetUri());
        if (!IsAppUri(uri.GetLeftPart(UriPartial.Authority), frame: false))
        {
            Log.Write($"FOREIGN_DOCUMENT_INTERCEPTED context={args.GetResourceContext()} {uri.AbsoluteUri}");
            args.PutResponse(Respond("blocked"u8.ToArray(), 403, "Forbidden", "Content-Type: text/plain"));
            return;
        }

        var headers = request.GetHeaders();
        string? Header(string name) => headers.Contains(name) != 0 ? headers.GetHeader(name) : null;
        var method = request.GetMethod();
        var body = ReadAll(request.GetContent());
        if (!uri.AbsolutePath.StartsWith("/_spar/log", StringComparison.Ordinal))
        {
            Log.Write($"SERVE {method} {uri.PathAndQuery} referer={Header("Referer") ?? "-"} dest={Header("Sec-Fetch-Dest") ?? "-"}");
        }

        var response = _routes.Handle(method, uri.PathAndQuery, body, Header);
        var head = new StringBuilder();
        head.Append("Content-Type: ").Append(response.ContentType);
        foreach (var (name, value) in response.Headers)
        {
            head.Append("\r\n").Append(name).Append(": ").Append(value);
        }

        args.PutResponse(Respond(response.Body, response.Status, response.Reason, head.ToString()));
    }

    private void OnPageMessage(JsonElement message)
    {
        var kind = message.TryGetProperty("kind", out var k) ? k.GetString() : null;
        switch (kind)
        {
            case "click":
                var button = message.GetProperty("button").GetString()!;
                var x = message.GetProperty("x").GetInt32();
                var y = message.GetProperty("y").GetInt32();
                var modifiers = message.GetProperty("modifiers").GetInt32();
                _ui!.Post(static async state =>
                {
                    var (host, button, x, y, modifiers) = ((DesktopHost, string, int, int, int))state!;
                    await host.ClickAsync(button, x, y, modifiers);
                }, (this, button, x, y, modifiers));
                break;
            case "open-external":
                var url = message.GetProperty("url").GetString()!;
                _ui!.Post(static state => ((DesktopHost)((object[])state!)[0]).OpenExternal((string)((object[])state!)[1]), new object[] { this, url });
                break;
            case "done":
                _ui!.Post(static async state =>
                {
                    var host = (DesktopHost)state!;
                    await Task.Delay(int.Parse(host._options.Value("--linger") ?? "300", CultureInfo.InvariantCulture));
                    host.Close();
                }, this);
                break;
        }
    }

    /// <summary>A trusted click through in-process CDP (Input.dispatchMouseEvent), as user input would arrive.</summary>
    private async Task ClickAsync(string button, int x, int y, int modifiers)
    {
        var buttons = button == "middle" ? 4 : 1;
        var position = $"\"x\":{x},\"y\":{y},\"modifiers\":{modifiers}";
        await Cdp("Input.dispatchMouseEvent", $"{{\"type\":\"mouseMoved\",{position}}}");
        await Cdp("Input.dispatchMouseEvent", $"{{\"type\":\"mousePressed\",{position},\"button\":\"{button}\",\"buttons\":{buttons},\"clickCount\":1}}");
        await Cdp("Input.dispatchMouseEvent", $"{{\"type\":\"mouseReleased\",{position},\"button\":\"{button}\",\"buttons\":0,\"clickCount\":1}}");
        Log.Write($"CLICKED {button} at {x},{y} modifiers={modifiers}");
    }

    private async Task<string> Cdp(string method, string parameters)
    {
        var handler = new DevToolsCallHandler(method);
        _webView!.CallDevToolsProtocolMethod(method, parameters, handler);
        return await handler.Task;
    }

    /// <summary>The explicit "open in the system browser" path: http(s) only, through ShellExecuteEx (stubbed unless --real-open).</summary>
    private unsafe void OpenExternal(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            Log.Write("OPEN_EXTERNAL_REFUSED " + url);
            return;
        }

        if (!_options.RealOpen)
        {
            Log.Write("OPEN_EXTERNAL_STUB " + uri.AbsoluteUri);
            return;
        }

        var timer = Stopwatch.StartNew();
        fixed (char* verb = "open")
        fixed (char* file = uri.AbsoluteUri)
        {
            var info = new SHELLEXECUTEINFOW
            {
                cbSize = (uint)sizeof(SHELLEXECUTEINFOW),
                fMask = PInvoke.SEE_MASK_NOASYNC | PInvoke.SEE_MASK_FLAG_NO_UI,
                hwnd = _window,
                lpVerb = verb,
                lpFile = file,
                nShow = (int)SHOW_WINDOW_CMD.SW_SHOWNORMAL,
            };
            var ok = PInvoke.ShellExecuteEx(ref info);
            Log.Write($"OPEN_EXTERNAL_SHELL ok={(bool)ok} error={(ok ? 0 : Marshal.GetLastPInvokeError())} hInstApp={(nint)info.hInstApp.Value} {timer.Elapsed.TotalMilliseconds:F1}ms {uri.AbsoluteUri}");
        }
    }

    // ---- Helpers ----

    private unsafe ICoreWebView2WebResourceResponse Respond(byte[] content, int status, string reason, string headers)
    {
        nint stream;
        fixed (byte* data = content)
        {
            stream = NativeLoader.SHCreateMemStream(data, (uint)content.Length);
        }

        try
        {
            return _environment!.CreateWebResourceResponse(stream, status, reason, headers);
        }
        finally
        {
            Marshal.Release(stream);
        }
    }

    private static unsafe byte[] ReadAll(ISequentialStream? stream)
    {
        if (stream is null)
        {
            return [];
        }

        using var result = new MemoryStream();
        var chunk = new byte[64 * 1024];
        while (true)
        {
            uint read;
            fixed (byte* data = chunk)
            {
                Marshal.ThrowExceptionForHR(stream.Read((nint)data, (uint)chunk.Length, (nint)(&read)));
            }

            if (read == 0)
            {
                return result.ToArray();
            }

            result.Write(chunk, 0, (int)read);
        }
    }

    private unsafe nint WindowHandle => (nint)_window.Value;

    private void Resize()
    {
        if (_controller is null)
        {
            return;
        }

        PInvoke.GetClientRect(_window, out var client);
        _controller.PutBounds(new Rect { Left = 0, Top = 0, Right = client.right, Bottom = client.bottom });
    }

    private void Close()
    {
        _closing = true;
        PInvoke.PostMessage(_window, PInvoke.WM_CLOSE, default, default);
    }

    private void Shutdown()
    {
        _closing = true;
        _controller?.Close();
        _controller = null;
        _webView = null;
        _environment = null;
        _handlers.Clear();
    }

    private void WaitForBrowserExit()
    {
        if (_browserProcessId == 0)
        {
            return;
        }

        var timer = Stopwatch.StartNew();
        try
        {
            using var browser = Process.GetProcessById((int)_browserProcessId);
            var exited = browser.WaitForExit(TimeSpan.FromSeconds(15));
            Log.Write($"BROWSER_EXIT exited={exited} after={timer.ElapsedMilliseconds}ms");
        }
        catch (ArgumentException)
        {
            Log.Write($"BROWSER_EXIT exited=True after={timer.ElapsedMilliseconds}ms");
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
                    host?.Resize();
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
            Log.Write("WINDOW_PROCEDURE_FAILURE " + exception);
        }

        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }
}
