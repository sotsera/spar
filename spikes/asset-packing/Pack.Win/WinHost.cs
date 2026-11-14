using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Spar.AssetPacking;
using Spar.AssetPacking.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

[assembly: SupportedOSPlatform("windows10.0")]

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
return WinHost.Run(args);

/// <summary>
/// Minimal WebView2 host: an offscreen window, one controller, and <c>WebResourceRequested</c> serving
/// <c>https://app.spar.local/</c> from the embedded pack. The page's <c>POST /_spar/ready</c> ends the run.
/// <c>--decode eager</c> decodes everything before the browser starts, <c>background</c> while it starts,
/// <c>lazy</c> on each first request (cached).
/// </summary>
internal sealed class WinHost
{
    private const string DefaultOrigin = "https://app.spar.local";

    private static WinHost? s_current;

    private readonly string[] _args;
    private readonly string _decode;
    private readonly string _origin;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<string, double> _marks = [];
    private Task<FrontendAssets>? _assets;
    private HWND _window;
    private UiContext? _ui;
    private ICoreWebView2Environment? _environment;
    private ICoreWebView2Controller? _controller;
    private ICoreWebView2? _webView;
    private WebResourceRequestedHandler? _resourceHandler;
    private NavigationCompletedHandler? _navigationHandler;
    private EventRegistrationToken _resourceToken;
    private EventRegistrationToken _navigationToken;
    private uint _browserProcessId;
    private long _closeRequestedAt;
    private double _assetMs;
    private int _requests;
    private string _requestLog = "";
    private string _probe = "";
    private int _exitCode;

    private WinHost(string[] args)
    {
        _args = args;
        _decode = Option("--decode") ?? "lazy";
        _origin = Option("--origin") ?? DefaultOrigin;
    }

    public static int Run(string[] args)
    {
        double processToMain;
        using (var self = Process.GetCurrentProcess())
        {
            processToMain = (DateTime.Now - self.StartTime).TotalMilliseconds;
        }

        var host = new WinHost(args);
        host._marks["process->main"] = processToMain;
        var thread = new Thread(host.RunOnSta) { Name = "Spar UI" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return host._exitCode;
    }

    private string? Option(string name)
    {
        var index = Array.IndexOf(_args, name);
        return index >= 0 && index + 1 < _args.Length ? _args[index + 1] : null;
    }

    private void Mark(string name, bool sinceClose = false) =>
        _marks[name] = sinceClose ? Stopwatch.GetElapsedTime(_closeRequestedAt).TotalMilliseconds : _clock.Elapsed.TotalMilliseconds;

    private void RunOnSta()
    {
        s_current = this;
        switch (_decode)
        {
            case "eager":
                var assets = FrontendAssets.Open(typeof(WinHost).Assembly);
                assets.DecodeAll();
                _assets = Task.FromResult(assets);
                Mark("assets-decoded");
                break;
            case "background":
                _assets = Task.Run(() =>
                {
                    var assets = FrontendAssets.Open(typeof(WinHost).Assembly);
                    assets.DecodeAll();
                    Mark("assets-decoded");
                    return assets;
                });
                break;
            default:
                _assets = Task.FromResult(FrontendAssets.Open(typeof(WinHost).Assembly));
                break;
        }

        CreateWindow();
        SynchronizationContext.SetSynchronizationContext(_ui);
        _ui!.Post(static async state => await ((WinHost)state!).StartAsync(), this);
        while (PInvoke.GetMessage(out var message, HWND.Null, 0, 0) > 0)
        {
            PInvoke.TranslateMessage(message);
            PInvoke.DispatchMessage(message);
        }

        WaitForBrowserExit();
        Report();
    }

    private unsafe void CreateWindow()
    {
        var instance = (HINSTANCE)(nint)PInvoke.GetModuleHandle((char*)null).Value;
        fixed (char* className = "SparAssetPacking")
        fixed (char* title = "Asset packing")
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

            // Offscreen and never activated: the run must not take focus from the user.
            _window = PInvoke.CreateWindowEx(0, className, title, WINDOW_STYLE.WS_OVERLAPPEDWINDOW, -32000, -32000, 960, 720, HWND.Null, HMENU.Null, instance, null);
            if (_window.IsNull)
            {
                throw new InvalidOperationException("CreateWindowEx failed: " + Marshal.GetLastPInvokeError());
            }
        }

        _ui = new UiContext(_window);
        PInvoke.ShowWindow(_window, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
        Mark("window");
    }

    private async Task StartAsync()
    {
        try
        {
            var environmentHandler = new EnvironmentCompletedHandler();
            Marshal.ThrowExceptionForHR(NativeLoader.CreateCoreWebView2EnvironmentWithOptions(null, ProfileDirectory(), 0, environmentHandler));
            _environment = await environmentHandler.Task;
            Mark("environment");

            var controllerHandler = new ControllerCompletedHandler();
            _environment.CreateCoreWebView2Controller(WindowHandle, controllerHandler);
            _controller = await controllerHandler.Task;
            Mark("controller");
            _webView = _controller.GetCoreWebView2();
            _browserProcessId = _webView.GetBrowserProcessId();
            PInvoke.GetClientRect(_window, out var client);
            _controller.PutBounds(new Rect { Left = 0, Top = 0, Right = client.right, Bottom = client.bottom });

            _resourceHandler = new WebResourceRequestedHandler(OnWebResourceRequested);
            _resourceToken = _webView.AddWebResourceRequested(_resourceHandler);
            _webView.AddWebResourceRequestedFilter(_origin + "/*", 0);
            _navigationHandler = new NavigationCompletedHandler(() => Mark("navigation-completed"));
            _navigationToken = _webView.AddNavigationCompleted(_navigationHandler);
            Mark("navigate");
            _webView.Navigate(_origin + "/" + (Option("--page") ?? ""));

            // A page that never signals (undecoded script, for instance) ends the run after 20 s.
            await Task.Delay(TimeSpan.FromSeconds(20));
            if (!_marks.ContainsKey("ui-ready") && _closeRequestedAt == 0)
            {
                _probe += " TIMEOUT(no ui-ready in 20 s)";
                Close();
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Host failure: " + exception);
            _exitCode = 2;
            Close();
        }
    }

    private void OnWebResourceRequested(ICoreWebView2WebResourceRequestedEventArgs args)
    {
        var started = Stopwatch.GetTimestamp();
        var path = new Uri(args.GetRequest().GetUri()).AbsolutePath;
        _requestLog += $" {path}@{_clock.Elapsed.TotalMilliseconds - _marks.GetValueOrDefault("navigate"):F0}";
        if (path.StartsWith("/_spar/probe/", StringComparison.Ordinal))
        {
            _probe += " " + Uri.UnescapeDataString(path["/_spar/probe/".Length..]);
            args.PutResponse(Respond([], 204, "No Content", "text/plain"));
            return;
        }

#if SPAR_ENCODING_PROBE
        if (EncodingProbe.Answer(path) is { } probe)
        {
            args.PutResponse(Respond(probe.Content, 200, "OK", probe.ContentType + (probe.Encoding is null ? "" : "\r\nContent-Encoding: " + probe.Encoding)));
            return;
        }
#endif

        if (path == "/_spar/ready")
        {
            Mark("ui-ready");
            args.PutResponse(Respond([], 204, "No Content", "text/plain"));
            Close();
            return;
        }

        if (path.StartsWith("/_spar/", StringComparison.Ordinal))
        {
            // No backend in this spike: the page's other calls fail and it still renders.
            args.PutResponse(Respond("{\"error\":\"no-backend\"}"u8, 404, "Not Found", "application/json"));
            return;
        }

        // Background decoding: the first request waits for it.
        var assets = _assets!.GetAwaiter().GetResult();
        _requests++;
        var key = path == "/" ? "index.html" : path[1..];
        if (_decode == "passthrough" && assets.Entries.FirstOrDefault(entry => entry.Path == key) is { Encoding: 1 } encoded)
        {
            // Serve the stored Brotli bytes and let the browser decode them (it does not, per the React spike).
            args.PutResponse(Respond(assets.Stored(encoded).Span, 200, "OK", encoded.ContentType + "\r\nContent-Encoding: br"));
            _assetMs += Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            return;
        }

        args.PutResponse(assets.TryGet(path == "/" ? "index.html" : path[1..], out var entry, out var content)
            ? Respond(content.Span, 200, "OK", entry.ContentType)
            : Respond("not found"u8, 404, "Not Found", "text/plain"));
        _assetMs += Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }

    private unsafe ICoreWebView2WebResourceResponse Respond(ReadOnlySpan<byte> content, int status, string reason, string contentType)
    {
        nint stream;
        fixed (byte* data = content)
        {
            // SHCreateMemStream copies the bytes; the response holds the only reference after this method.
            stream = NativeLoader.SHCreateMemStream(data, (uint)content.Length);
        }

        if (stream == 0)
        {
            throw new OutOfMemoryException("SHCreateMemStream failed.");
        }

        try
        {
            return _environment!.CreateWebResourceResponse(stream, status, reason, $"Content-Type: {contentType}\r\nCache-Control: no-store");
        }
        finally
        {
            Marshal.Release(stream);
        }
    }

    private void Close()
    {
        _closeRequestedAt = Stopwatch.GetTimestamp();
        PInvoke.PostMessage(_window, PInvoke.WM_CLOSE, default, default);
    }

    private void Shutdown()
    {
        if (_webView is not null)
        {
            if (_resourceHandler is not null) _webView.RemoveWebResourceRequested(_resourceToken);
            if (_navigationHandler is not null) _webView.RemoveNavigationCompleted(_navigationToken);
        }

        // Memory of this process (not the browser's) while everything is still alive.
        using (var self = Process.GetCurrentProcess())
        {
            _marks["host-private-kb"] = self.PrivateMemorySize64 / 1024;
            _marks["host-peak-ws-kb"] = self.PeakWorkingSet64 / 1024;
        }

        _resourceHandler = null;
        _navigationHandler = null;
        _controller?.Close();
        _controller = null;
        _webView = null;
        _environment = null;
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
            Mark(browser.WaitForExit(TimeSpan.FromSeconds(15)) ? "browser-exited" : "browser-still-running", sinceClose: true);
        }
        catch (ArgumentException)
        {
            Mark("browser-exited", sinceClose: true);
        }
    }

    private void Report()
    {
        var assets = _assets!.IsCompletedSuccessfully ? _assets.Result : null;
        double Get(string name) => _marks.GetValueOrDefault(name, double.NaN);
        Console.WriteLine(
            $"HOST format={FrontendAssets.Format} decode={_decode} exit={_exitCode} process->main={Get("process->main"):F1} " +
            $"assets-decoded={Get("assets-decoded"):F1} window={Get("window"):F1} environment={Get("environment"):F1} controller={Get("controller"):F1} " +
            $"navigate={Get("navigate"):F1} navigation-completed={Get("navigation-completed"):F1} ui-ready={Get("ui-ready"):F1} " +
            $"navigate->ready={Get("ui-ready") - Get("navigate"):F1} requests={_requests} asset-handler-ms={_assetMs:F2} cached={assets?.CachedBytes ?? 0} " +
            $"host-private-kb={Get("host-private-kb")} host-peak-ws-kb={Get("host-peak-ws-kb")} close->browser-exited={Get("browser-exited"):F0} " +
            $"requests-at:{_requestLog}{_probe}");
    }

    private string ProfileDirectory()
    {
        // Spike rule: profiles live under the repository's artifacts/webview2-profiles/, never %LOCALAPPDATA%.
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")))
            {
                return Path.Combine(directory.FullName, "artifacts", "webview2-profiles", Option("--profile") ?? "asset-packing");
            }
        }

        throw new InvalidOperationException("Run from inside the repository: the profile goes under artifacts/webview2-profiles/.");
    }

    private unsafe nint WindowHandle => (nint)_window.Value;

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
