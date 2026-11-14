using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using Theme.Desktop.Interop;

[assembly: SupportedOSPlatform("windows10.0.17763")]

namespace Theme.Desktop;

/// <summary>
/// Spike host. One STA thread owns the window, the WebView2 environment and controller, and the message loop. Each
/// theme layer is behind a switch (<see cref="Options.Layers"/>), so a run measures exactly one combination.
/// </summary>
internal sealed class Host
{
    public const string Origin = "https://app.localhost";
    private const uint DrainMessage = User32.WmApp + 1;
    private const int Width = 960;
    private const int Height = 720;

    private static Host? s_current;

    private readonly Options _options;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();
    private readonly List<string> _marks = [];
    private readonly string _outDirectory;
    private readonly string _csp;
    private nint _window;
    private nint _brush;
    private FrameCapture? _capture;
    private WgcCapture? _wgc;
    private ICoreWebView2Environment? _environment;
    private ICoreWebView2Controller? _controller;
    private ICoreWebView2? _webView;
    private nint _controller2;
    private nint _profile;
    private string _choice;
    private string? _simulatedSystem;
    private string _theme;
    private string? _readyJson;
    private uint _browserProcessId;
    private bool _shown;
    private bool _erased;
    private bool _painted;
    private readonly TaskCompletionSource _uiReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _navigated = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Keep the managed handlers alive while registered.
    private WebResourceRequestedHandler? _resourceHandler;
    private WebMessageReceivedHandler? _messageHandler;
    private NavigationCompletedHandler? _navigationHandler;
    private NavigationStartingHandler? _navigationStartingHandler;
    private PermissionRequestedHandler? _permissionHandler;

    public Host(Options options)
    {
        _options = options;
        _outDirectory = Path.Combine(options.RepoRoot, "artifacts", "theme-startup", options.Label);
        if (Directory.Exists(_outDirectory)) Directory.Delete(_outDirectory, recursive: true);
        Directory.CreateDirectory(_outDirectory);
        _simulatedSystem = options.SystemTheme;
        _choice = options.Theme ?? ThemeStore.Load(options.AppDataDirectory) ?? "system";
        _theme = Effective();
        _csp = BuildCsp();
    }

    public int Run()
    {
        var exitCode = 0;
        var thread = new Thread(() => exitCode = RunOnSta()) { Name = "Spar UI" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return exitCode;
    }

    private int RunOnSta()
    {
        s_current = this;
        Mark($"start choice={_choice} system={SystemTheme()} effective={_theme} layers={string.Join(',', _options.Layers)} show={_options.Show}");
        CreateWindow();
        SynchronizationContext.SetSynchronizationContext(new UiContext(this));
        PostStart();
        Pump();

        Mark("message-loop-ended");
        WaitForBrowserExit();
        WriteTimeline();
        return 0;
    }

    private void PostStart() => Post(static async state => await ((Host)state!).StartAsync(), this);

    private static unsafe void Pump()
    {
        Message message;
        while (User32.GetMessage(&message, 0, 0, 0) > 0)
        {
            User32.TranslateMessage(&message);
            User32.DispatchMessage(&message);
        }
    }

    // ---- Theme ----

    private string SystemTheme()
    {
        if (_simulatedSystem is not null) return _simulatedSystem;
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0 ? "dark" : "light";
    }

    private string Effective() => _choice == "system" ? SystemTheme() : _choice;

    private static uint Background(string theme) => theme == "dark" ? FrameCapture.DarkBackground : FrameCapture.LightBackground;

    private static uint ColorRef(uint rgb) => ((rgb & 0xff) << 16) | (rgb & 0xff00) | (rgb >> 16);

    private bool Has(string layer) => _options.Layers.Contains(layer);

    private WebViewColor WebViewBackground() =>
        Has("transparent") ? default : WebViewColor.FromRgb(Background(_theme));

    /// <summary>Applies the effective theme to every layer that is on, in one go.</summary>
    private unsafe void ApplyTheme(string reason)
    {
        var theme = Effective();
        if (theme == _theme && reason != "start")
        {
            Mark($"theme-unchanged {theme} ({reason})");
            return;
        }

        _theme = theme;
        Mark($"theme-apply {theme} ({reason})");
        if (Has("titlebar"))
        {
            Dwm.SetBool(_window, Dwm.UseImmersiveDarkMode, theme == "dark");
        }

        if (Has("brush") || Has("erase"))
        {
            var old = _brush;
            _brush = Gdi32.CreateSolidBrush(ColorRef(Background(theme)));
            if (old != 0) Gdi32.DeleteObject(old);
            User32.InvalidateRect(_window, null, true);
        }

        if (_controller2 != 0)
        {
            Check(WebView2Raw.PutDefaultBackgroundColor(_controller2, WebViewBackground()), "put_DefaultBackgroundColor");
        }

        if (_profile != 0)
        {
            Check(WebView2Raw.PutPreferredColorScheme(_profile, theme == "dark" ? 2 : 1), "put_PreferredColorScheme");
        }

        Mark($"theme-applied {theme}");
    }

    private void OnSettingChange(string? area)
    {
        if (area != "ImmersiveColorSet") return;
        Mark($"WM_SETTINGCHANGE ImmersiveColorSet system={SystemTheme()} choice={_choice}");
        if (_choice == "system") ApplyTheme("system change");
    }

    /// <summary>Simulates a system theme change: an in-process override, then WM_SETTINGCHANGE sent to our own window only.</summary>
    private unsafe void SimulateSystemChange(string theme)
    {
        _simulatedSystem = theme;
        Mark($"simulate-system {theme}");
        fixed (char* area = "ImmersiveColorSet")
        {
            nuint result;
            User32.SendMessageTimeout(_window, User32.WmSettingChange, 0, (nint)area, User32.SmtoAbortIfHung, 1000, &result);
        }
    }

    private void OnSchemeFromPage(string scheme)
    {
        Mark($"page-set-scheme {scheme}");
        _choice = scheme;
        ThemeStore.Save(_options.AppDataDirectory, scheme);
        ApplyTheme("app choice");
    }

    // ---- Window ----

    private unsafe void CreateWindow()
    {
        if (_options.CaptureWith is "wgc" or "both")
        {
            _wgc = new WgcCapture(_clock, _outDirectory, TimeSpan.FromMilliseconds(_options.CaptureMs));
            _wgc.Start();
        }

        var instance = Kernel32.GetModuleHandle(null);
        if (Has("brush") || Has("erase"))
        {
            _brush = Gdi32.CreateSolidBrush(ColorRef(Background(_theme)));
        }

        fixed (char* className = "SparThemeSpike")
        fixed (char* title = "Theme startup spike")
        {
            var windowClass = new WindowClass
            {
                Size = (uint)sizeof(WindowClass),
                WindowProcedure = &WindowProcedure,
                Instance = instance,
                Cursor = User32.LoadCursor(0, 32512), // IDC_ARROW
                Background = Has("brush") ? _brush : 0,
                ClassName = className,
            };
            if (User32.RegisterClassEx(&windowClass) == 0)
            {
                throw new InvalidOperationException("RegisterClassEx failed: " + Marshal.GetLastPInvokeError());
            }

            // Offscreen, left of the whole virtual screen: DWM still composes it, and nothing is in the user's way.
            var x = User32.GetSystemMetrics(User32.SmXVirtualScreen) - Width - 2000;
            var y = User32.GetSystemMetrics(User32.SmYVirtualScreen) + 100;
            _window = User32.CreateWindowEx(0, className, title, User32.WsOverlappedWindow, x, y, Width, Height, 0, 0, instance, 0);
            if (_window == 0)
            {
                throw new InvalidOperationException("CreateWindowEx failed: " + Marshal.GetLastPInvokeError());
            }
        }

        Mark("window-created");
        if (Has("titlebar"))
        {
            Check(Dwm.SetBool(_window, Dwm.UseImmersiveDarkMode, _theme == "dark"), "DWMWA_USE_IMMERSIVE_DARK_MODE");
        }

        if (_options.Show == "cloak")
        {
            Check(Dwm.SetBool(_window, Dwm.Cloak, true), "DWMWA_CLOAK");
        }

        if (_options.CaptureWith is "print" or "both")
        {
            _capture = new FrameCapture(_window, _clock, _outDirectory, TimeSpan.FromMilliseconds(_options.CaptureMs));
            _capture.Start();
        }

        if (_wgc is not null)
        {
            // WGC needs a shown window: show it cloaked, attach, then let the show mode decide when to uncloak.
            Dwm.SetBool(_window, Dwm.Cloak, true);
            User32.ShowWindow(_window, User32.SwShowNoActivate);
            Mark("wgc " + _wgc.Attach(_window));
            if (_options.Show == "now")
            {
                Dwm.SetBool(_window, Dwm.Cloak, false);
                _shown = true;
                Mark("visible: uncloaked (immediate, after attaching WGC)");
            }
            else if (_options.Show == "ready")
            {
                User32.ShowWindow(_window, User32.SwHide);
                Dwm.SetBool(_window, Dwm.Cloak, false);
            }
        }

        if (_options.Show == "now")
        {
            Show("immediate");
        }
        else if (_options.Show == "cloak")
        {
            User32.ShowWindow(_window, User32.SwShowNoActivate);
            Mark("shown-cloaked");
        }
    }

    private void Show(string why)
    {
        if (_shown) return;
        _shown = true;
        if (_options.Show == "cloak")
        {
            Dwm.SetBool(_window, Dwm.Cloak, false);
            Mark($"visible: uncloaked ({why})");
            return;
        }

        if (Has("cloakpaint"))
        {
            // Shown while cloaked, painted synchronously, then uncloaked: the first composed frame has our colour.
            Dwm.SetBool(_window, Dwm.Cloak, true);
            User32.ShowWindow(_window, User32.SwShowNoActivate);
            User32.UpdateWindow(_window);
            Dwm.SetBool(_window, Dwm.Cloak, false);
            Mark($"visible: shown after a cloaked paint ({why})");
            return;
        }

        User32.ShowWindow(_window, User32.SwShowNoActivate);
        if (Has("update")) User32.UpdateWindow(_window);
        if (Has("revisible") && _controller is not null)
        {
            // The controller was created under a hidden parent: tell it again that it is visible.
            _controller.PutIsVisible(0);
            _controller.PutIsVisible(1);
            Mark("controller IsVisible false -> true");
        }
        Mark($"visible: shown ({why}){(Has("update") ? " + UpdateWindow" : "")}");
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam)
    {
        var host = s_current;
        try
        {
            if (message == User32.WmEraseBackground && host is not null && !host._erased)
            {
                host._erased = true;
                host.Mark("first WM_ERASEBKGND");
            }

            switch (message)
            {
                case DrainMessage:
                    host?.Drain();
                    return 0;
                case User32.WmPaint when host is not null && !host._painted:
                    host._painted = true;
                    host.Mark("first WM_PAINT");
                    break;
                case User32.WmEraseBackground when host is not null && host._brush != 0:
                    User32.GetClientRect(window, out var client);
                    User32.FillRect((nint)wParam, client, host._brush);
                    return 1;
                case User32.WmSize:
                    host?.ResizeController();
                    return 0;
                case User32.WmSettingChange:
                    host?.OnSettingChange(lParam == 0 ? null : Marshal.PtrToStringUni(lParam));
                    return 0;
                case User32.WmClose:
                    host?.Shutdown();
                    User32.DestroyWindow(window);
                    return 0;
                case User32.WmDestroy:
                    User32.PostQuitMessage(0);
                    return 0;
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Window procedure failure: " + exception);
        }

        return User32.DefWindowProc(window, message, wParam, lParam);
    }

    // ---- WebView2 ----

    private async Task StartAsync()
    {
        try
        {
            var profile = Path.Combine(_options.RepoRoot, "artifacts", "webview2-profiles", "theme-startup-" + _options.Profile);
            Directory.CreateDirectory(profile);
            if (Has("wv-env"))
            {
                // In-process only: the loader and the browser process it starts read it.
                Environment.SetEnvironmentVariable("WEBVIEW2_DEFAULT_BACKGROUND_COLOR", "0x" + (0xff000000u | Background(_theme)).ToString("X8", CultureInfo.InvariantCulture));
            }

            var environmentHandler = new EnvironmentCompletedHandler();
            Marshal.ThrowExceptionForHR(NativeLoader.CreateCoreWebView2EnvironmentWithOptions(null, profile, 0, environmentHandler));
            _environment = await environmentHandler.Task;
            Mark("environment");

            var controllerHandler = new ControllerCompletedHandler();
            if (Has("wv-options"))
            {
                var environment10 = WebView2Raw.Query(_environment, WebView2Raw.IidEnvironment10);
                var options = WebView2Raw.CreateControllerOptions(environment10);
                var options3 = WebView2Raw.Query(options, WebView2Raw.IidControllerOptions3);
                Check(WebView2Raw.PutOptionsDefaultBackgroundColor(options3, WebViewBackground()), "ControllerOptions3.put_DefaultBackgroundColor");
                WebView2Raw.CreateControllerWithOptions(environment10, _window, options, controllerHandler);
                Marshal.Release(options3);
                Marshal.Release(options);
                Marshal.Release(environment10);
                Mark("controller-requested (with options)");
            }
            else
            {
                _environment.CreateCoreWebView2Controller(_window, controllerHandler);
                Mark("controller-requested");
            }

            _controller = await controllerHandler.Task;
            Mark("controller");
            _controller2 = WebView2Raw.Query(_controller, WebView2Raw.IidController2);
            Check(WebView2Raw.GetDefaultBackgroundColor(_controller2, out var initial), "get_DefaultBackgroundColor");
            Mark($"controller-default-background #{initial.A:x2}{initial.R:x2}{initial.G:x2}{initial.B:x2}");
            if (Has("wv-put"))
            {
                Check(WebView2Raw.PutDefaultBackgroundColor(_controller2, WebViewBackground()), "put_DefaultBackgroundColor");
                Mark("put_DefaultBackgroundColor");
            }
            else if (!Has("wv-env") && !Has("wv-options"))
            {
                Marshal.Release(_controller2);
                _controller2 = 0; // No WebView colour layer: live changes leave it alone too.
            }

            _webView = _controller.GetCoreWebView2();
            _browserProcessId = _webView.GetBrowserProcessId();
            var webView13 = WebView2Raw.Query(_webView, WebView2Raw.IidWebView2_13);
            var profileHandle = WebView2Raw.GetProfile(webView13);
            Marshal.Release(webView13);
            Check(WebView2Raw.GetPreferredColorScheme(profileHandle, out var scheme), "get_PreferredColorScheme");
            Mark($"profile-preferred-color-scheme {scheme}");
            if (Has("scheme"))
            {
                _profile = profileHandle;
                Check(WebView2Raw.PutPreferredColorScheme(_profile, _theme == "dark" ? 2 : 1), "put_PreferredColorScheme");
                Mark("put_PreferredColorScheme");
            }
            else
            {
                Marshal.Release(profileHandle);
            }

            ResizeController();
            _resourceHandler = new WebResourceRequestedHandler(OnWebResourceRequested);
            _webView.AddWebResourceRequested(_resourceHandler);
            _webView.AddWebResourceRequestedFilter(Origin + "/*", 0);
            _messageHandler = new WebMessageReceivedHandler(OnWebMessageReceived);
            _webView.AddWebMessageReceived(_messageHandler);
            _navigationStartingHandler = new NavigationStartingHandler(() => Mark("navigation-starting"));
            _webView.AddNavigationStarting(_navigationStartingHandler);
            _navigationHandler = new NavigationCompletedHandler(() =>
            {
                Mark("navigation-completed");
                _navigated.TrySetResult();
            });
            _webView.AddNavigationCompleted(_navigationHandler);
            _permissionHandler = new PermissionRequestedHandler(args =>
            {
                args.PutState(2); // COREWEBVIEW2_PERMISSION_STATE_DENY: no prompt ever reaches a person.
                Mark($"permission-denied kind={args.GetPermissionKind()}");
            });
            _webView.AddPermissionRequested(_permissionHandler);

            if (_options.ReducedMotion)
            {
                var call = new DevToolsCallHandler("Emulation.setEmulatedMedia");
                _webView.CallDevToolsProtocolMethod("Emulation.setEmulatedMedia", """{"features":[{"name":"prefers-reduced-motion","value":"reduce"}]}""", call);
                await call.Task;
                Mark("emulated prefers-reduced-motion: reduce");
            }

            if (_options.NavigateDelayMs > 0)
            {
                await Task.Delay(_options.NavigateDelayMs);
            }

            var url = $"{Origin}/{_options.Page}{(_options.Query is { Length: > 0 } q ? "?" + q : "")}";
            _webView.Navigate(url);
            Mark("navigate " + url);

            var timeout = Task.Delay(_options.ShowTimeoutMs);
            if (_options.Show is "ready" or "cloak" && await Task.WhenAny(_uiReady.Task, timeout) == timeout)
            {
                Show("timeout");
            }

            if (await Task.WhenAny(_uiReady.Task, Task.Delay(15000)) != _uiReady.Task)
            {
                Mark("ui-ready-timeout (15 s)");
                Show("no ui-ready");
            }

            await Task.Delay(_options.AfterReadyMs);
            if (_options.Live)
            {
                await LiveSequenceAsync();
            }

            Close();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Host failure: " + exception);
            Mark("host-failure " + exception.GetType().Name + ": " + exception.Message);
            Close();
        }
    }

    /// <summary>System changes (simulated) and the app's own switches, while the app runs.</summary>
    private async Task LiveSequenceAsync()
    {
        var step = TimeSpan.FromMilliseconds(900);
        string Opposite() => SystemTheme() == "dark" ? "light" : "dark";
        SimulateSystemChange(Opposite());          // choice=system: follows
        await Task.Delay(step);
        _webView!.PostWebMessageAsString("set-scheme:" + Opposite()); // app picks the other theme (via the page)
        await Task.Delay(step);
        SimulateSystemChange(Opposite());          // choice fixed: ignored
        await Task.Delay(step);
        _webView.PostWebMessageAsString("set-scheme:system"); // back to the system's
        await Task.Delay(step);
        SimulateSystemChange(Opposite());          // follows again
        await Task.Delay(step);
    }

    private void OnWebMessageReceived(ICoreWebView2WebMessageReceivedEventArgs args)
    {
        if (args.TryGetWebMessageAsString(out var text) < 0 || text is null) return;
        using var json = JsonDocument.Parse(text);
        var root = json.RootElement;
        switch (root.GetProperty("t").GetString())
        {
            case "mark":
                Mark($"page:{root.GetProperty("n").GetString()} p={root.GetProperty("p").GetDouble():F1} {text}");
                break;
            case "scheme":
                OnSchemeFromPage(root.GetProperty("v").GetString() ?? "system");
                break;
            case "ready":
                _readyJson = text;
                Mark("ui-ready");
                Console.WriteLine("SPAR_READY " + text);
                if (_options.Show is "ready" or "cloak") Show("ui-ready");
                _uiReady.TrySetResult();
                break;
        }
    }

    private void OnWebResourceRequested(ICoreWebView2WebResourceRequestedEventArgs args)
    {
        var path = new Uri(args.GetRequest().GetUri()).AbsolutePath;
        var file = Path.GetFullPath(Path.Combine(_options.WebDirectory, path.TrimStart('/')));
        if (!file.StartsWith(_options.WebDirectory, StringComparison.OrdinalIgnoreCase) || !File.Exists(file))
        {
            args.PutResponse(Respond([], 404, "Not Found", "text/plain", null));
            return;
        }

        var bytes = File.ReadAllBytes(file);
        var isHtml = file.EndsWith(".html", StringComparison.Ordinal);
        var response = Respond(bytes, 200, "OK", ContentType(file), isHtml && !_options.NoCsp ? _csp : null);
        Mark($"serve {path}{(isHtml ? "" : "")}");
        var asset = path.StartsWith("/assets/", StringComparison.Ordinal);
        var delay = isHtml ? _options.HtmlDelayMs
            : asset && file.EndsWith(".js", StringComparison.Ordinal) ? _options.BundleDelayMs
            : asset && file.EndsWith(".css", StringComparison.Ordinal) ? _options.CssDelayMs : 0;
        if (delay > 0)
        {
            var deferral = args.GetDeferral();
            _ = CompleteLaterAsync(args, response, deferral, delay, path);
            return;
        }

        args.PutResponse(response);
    }

    private async Task CompleteLaterAsync(ICoreWebView2WebResourceRequestedEventArgs args, ICoreWebView2WebResourceResponse response, ICoreWebView2Deferral deferral, int delay, string path)
    {
        await Task.Delay(delay);
        args.PutResponse(response);
        deferral.Complete();
        Mark($"delivered {path} (delayed {delay} ms)");
    }

    private unsafe ICoreWebView2WebResourceResponse Respond(byte[] content, int status, string reason, string contentType, string? csp)
    {
        nint stream;
        fixed (byte* data = content)
        {
            stream = NativeLoader.SHCreateMemStream(data, (uint)content.Length);
        }

        try
        {
            var headers = $"Content-Type: {contentType}\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff";
            if (csp is not null) headers += "\r\nContent-Security-Policy: " + csp;
            return _environment!.CreateWebResourceResponse(stream, status, reason, headers);
        }
        finally
        {
            Marshal.Release(stream);
        }
    }

    private string BuildCsp()
    {
        var styleSources = "'self'";
        var hashes = Path.Combine(_options.WebDirectory, "spar-csp.json");
        if (!_options.NoHash && File.Exists(hashes))
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(hashes));
            foreach (var source in json.RootElement.GetProperty("styleSrc").EnumerateArray())
            {
                styleSources += " " + source.GetString();
            }
        }

        return $"default-src 'none'; script-src 'self'; style-src {styleSources}; img-src 'self'; font-src 'self'; connect-src 'self'; " +
            "base-uri 'none'; form-action 'none'; frame-ancestors 'none'; object-src 'none'";
    }

    private static string ContentType(string path) => Path.GetExtension(path) switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" => "application/json",
        ".svg" => "image/svg+xml",
        _ => "application/octet-stream",
    };

    private void ResizeController()
    {
        if (_controller is null) return;
        User32.GetClientRect(_window, out var client);
        _controller.PutBounds(new Rect { Left = 0, Top = 0, Right = client.Right, Bottom = client.Bottom });
    }

    // ---- Shutdown and output ----

    private async void Close()
    {
        if (_capture is not null)
        {
            await _capture.StopAsync();
            Mark($"capture-stopped frames={_capture.Count}");
        }

        if (_wgc is not null)
        {
            await _wgc.StopAsync();
            Mark($"wgc-stopped frames={_wgc.Count}");
        }

        User32.PostMessage(_window, User32.WmClose, 0, 0);
    }

    private void Shutdown()
    {
        Mark("closing");
        if (_controller2 != 0) Marshal.Release(_controller2);
        if (_profile != 0) Marshal.Release(_profile);
        _controller2 = 0;
        _profile = 0;
        _controller?.Close();
        _controller = null;
        _webView = null;
        _environment = null;
    }

    private void WaitForBrowserExit()
    {
        if (_browserProcessId == 0) return;
        try
        {
            using var browser = Process.GetProcessById((int)_browserProcessId);
            Mark(browser.WaitForExit(TimeSpan.FromSeconds(15)) ? "browser-exited" : "browser-still-running-after-15s");
        }
        catch (ArgumentException)
        {
            Mark("browser-exited");
        }
    }

    private void WriteTimeline()
    {
        var builder = new StringBuilder();
        builder.Append("{\n\"label\":").Append(JsonString(_options.Label))
            .Append(",\n\"args\":").Append(JsonString(string.Join(' ', _options.Raw)))
            .Append(",\n\"csp\":").Append(JsonString(_csp))
            .Append(",\n\"ready\":").Append(_readyJson ?? "null")
            .Append(",\n\"marks\":[\n");
        lock (_marks)
        {
            builder.Append(string.Join(",\n", _marks));
        }

        builder.Append("\n],\n\"frames\":").Append(_capture?.FramesJson() ?? "[]")
            .Append(",\n\"framesWgc\":").Append(_wgc?.FramesJson() ?? "[]").Append("\n}\n");
        File.WriteAllText(Path.Combine(_outDirectory, "timeline.json"), builder.ToString());
        Console.WriteLine($"SPAR_TIMELINE {Path.Combine(_outDirectory, "timeline.json")}");
    }

    private static string JsonString(string value) => JsonSerializer.Serialize(value, TimelineJson.Default.String);

    private void Mark(string name)
    {
        var at = _clock.Elapsed.TotalMilliseconds;
        lock (_marks)
        {
            _marks.Add(string.Create(CultureInfo.InvariantCulture, $"{{\"at\":{at:F1},\"name\":{JsonString(name)}}}"));
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[{at,8:F1} ms] {name}"));
    }

    private void Check(int hresult, string what)
    {
        if (hresult < 0) Mark($"FAILED {what} 0x{hresult:X8}");
    }

    // ---- STA synchronization context ----

    private void Post(SendOrPostCallback callback, object? state)
    {
        _queue.Enqueue((callback, state));
        User32.PostMessage(_window, DrainMessage, 0, 0);
    }

    private void Drain()
    {
        while (_queue.TryDequeue(out var item))
        {
            item.Callback(item.State);
        }
    }

    private sealed class UiContext(Host host) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => host.Post(d, state);

        public override void Send(SendOrPostCallback d, object? state) => d(state);
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(string))]
internal sealed partial class TimelineJson : System.Text.Json.Serialization.JsonSerializerContext;

/// <summary>The app's own colour scheme choice, saved in the app's data folder and read before the window exists.</summary>
internal static class ThemeStore
{
    public static string? Load(string directory)
    {
        var file = Path.Combine(directory, "color-scheme.txt");
        var value = File.Exists(file) ? File.ReadAllText(file).Trim() : null;
        return value is "light" or "dark" or "system" ? value : null;
    }

    public static void Save(string directory, string scheme)
    {
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "color-scheme.txt");
        File.WriteAllText(file + ".tmp", scheme);
        File.Move(file + ".tmp", file, overwrite: true);
    }
}
