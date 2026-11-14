using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Spar.DesktopOrigin;
using Spar.DesktopOrigin.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

[assembly: SupportedOSPlatform("windows10.0")]

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
return Host.Run(args);

/// <summary>
/// Spike host: an offscreen, never-activated window with one WebView2 controller. <c>WebResourceRequested</c> serves
/// the embedded <c>www/</c> files on <c>--origin</c> (default <c>https://app.localhost</c>) and logs every request it
/// sees. Paths under <c>/unanswered/</c> are deliberately left unanswered, so the default behaviour shows.
/// The page reports through <c>POST /_spar/result</c> and ends the run with <c>/_spar/done</c> (or the React bundle's
/// <c>POST /_spar/ready</c>). Every permission request is answered by the host: nothing is ever left to a prompt.
/// </summary>
internal sealed class Host
{
    private const int PermissionAllow = 1;
    private const int PermissionDeny = 2;

    private static Host? s_current;

    private readonly string[] _args;
    private readonly string _origin;
    private readonly string? _proxy;
    private readonly string _grant;
    private readonly string? _csp;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<string, double> _marks = [];
    private readonly HttpClient _http = new(new SocketsHttpHandler { UseProxy = false, AutomaticDecompression = System.Net.DecompressionMethods.None });
    private HWND _window;
    private UiContext? _ui;
    private ICoreWebView2Environment? _environment;
    private ICoreWebView2Controller? _controller;
    private ICoreWebView2? _webView;
    private WebResourceRequestedHandler? _resourceHandler;
    private NavigationCompletedHandler? _navigationHandler;
    private PermissionRequestedHandler? _permissionHandler;
    private EventRegistrationToken _resourceToken;
    private EventRegistrationToken _navigationToken;
    private EventRegistrationToken _permissionToken;
    private uint _browserProcessId;
    private long _closeRequestedAt;
    private int _exitCode;
    private bool _closing;

    private Host(string[] args)
    {
        _args = args;
        _origin = Option("--origin") ?? "https://app.localhost";
        _proxy = Option("--proxy");
        _grant = Option("--grant") ?? "";
        _csp = Option("--csp");
    }

    public static int Run(string[] args)
    {
        var host = new Host(args);
        var thread = new Thread(host.RunOnSta) { Name = "Spar UI" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return host._exitCode;
    }

    private static long Epoch => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private double SinceNavigate => _clock.Elapsed.TotalMilliseconds - _marks.GetValueOrDefault("navigate");

    private string? Option(string name)
    {
        var index = Array.IndexOf(_args, name);
        return index >= 0 && index + 1 < _args.Length ? _args[index + 1] : null;
    }

    private void Mark(string name, bool sinceClose = false) =>
        _marks[name] = sinceClose ? Stopwatch.GetElapsedTime(_closeRequestedAt).TotalMilliseconds : _clock.Elapsed.TotalMilliseconds;

    private static void Log(string line)
    {
        Console.WriteLine(line);
        Console.Out.Flush();
    }

    private void RunOnSta()
    {
        s_current = this;
        if (Option("--browser-args") is { } browserArguments)
        {
            // Diagnostics only (net log). This process's own environment block is read by the WebView2 loader.
            Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", browserArguments);
        }

        CreateWindow();
        SynchronizationContext.SetSynchronizationContext(_ui);
        _ui!.Post(static async state => await ((Host)state!).StartAsync(), this);
        while (PInvoke.GetMessage(out var message, HWND.Null, 0, 0) > 0)
        {
            PInvoke.TranslateMessage(message);
            PInvoke.DispatchMessage(message);
        }

        WaitForBrowserExit();
        Log($"HOST exit={_exitCode} version={Assembly.GetExecutingAssembly().GetName().Version} origin={_origin} " +
            $"close->browser-exited={_marks.GetValueOrDefault("browser-exited", double.NaN):F0}");
    }

    private unsafe void CreateWindow()
    {
        var instance = (HINSTANCE)(nint)PInvoke.GetModuleHandle((char*)null).Value;
        fixed (char* className = "SparDesktopOrigin")
        fixed (char* title = "Desktop origin spike")
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

            // Offscreen and never activated: runs must not take focus from the user or show anything.
            _window = PInvoke.CreateWindowEx(WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW, className, title,
                WINDOW_STYLE.WS_OVERLAPPEDWINDOW, -32000, -32000, 960, 720, HWND.Null, HMENU.Null, instance, null);
            if (_window.IsNull)
            {
                throw new InvalidOperationException("CreateWindowEx failed: " + Marshal.GetLastPInvokeError());
            }
        }

        _ui = new UiContext(_window);
        PInvoke.ShowWindow(_window, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
    }

    private async Task StartAsync()
    {
        try
        {
            var environmentHandler = new EnvironmentCompletedHandler();
            Marshal.ThrowExceptionForHR(NativeLoader.CreateCoreWebView2EnvironmentWithOptions(null, ProfileDirectory(), 0, environmentHandler));
            _environment = await environmentHandler.Task;
            Log("RUNTIME " + _environment.GetBrowserVersionString());

            var controllerHandler = new ControllerCompletedHandler();
            _environment.CreateCoreWebView2Controller(WindowHandle, controllerHandler);
            _controller = await controllerHandler.Task;
            _webView = _controller.GetCoreWebView2();
            _browserProcessId = _webView.GetBrowserProcessId();
            PInvoke.GetClientRect(_window, out var client);
            _controller.PutBounds(new Rect { Left = 0, Top = 0, Right = client.right, Bottom = client.bottom });

            // No JavaScript dialog may reach a person: with the default dialogs off and no ScriptDialogOpening
            // handler, alert/confirm/beforeunload are dismissed.
            _webView.GetSettings().PutAreDefaultScriptDialogsEnabled(0);

            _permissionHandler = new PermissionRequestedHandler(OnPermissionRequested);
            _permissionToken = _webView.AddPermissionRequested(_permissionHandler);
            _resourceHandler = new WebResourceRequestedHandler(OnWebResourceRequested);
            _resourceToken = _webView.AddWebResourceRequested(_resourceHandler);
            var filterMode = Option("--filter") ?? "legacy";
            if (filterMode == "sourcekinds")
            {
                // ICoreWebView2_22: also documents, shared workers and service workers (0xFFFFFFFF = all kinds).
                ((ICoreWebView2_22)_webView).AddWebResourceRequestedFilterWithRequestSourceKinds(_origin + "/*", 0, unchecked((int)0xFFFFFFFF));
            }
            else
            {
                _webView.AddWebResourceRequestedFilter(_origin + "/*", 0);
            }

            if (Option("--extra-filter") is { } extraFilter)
            {
                // Direct Vite loads: only the page's /_spar/ reports on the dev server's origin reach the host.
                _webView.AddWebResourceRequestedFilter(extraFilter, 0);
                Log($"FILTER extra uri={extraFilter}");
            }

            Log($"FILTER mode={filterMode} uri={_origin}/*");
            _navigationHandler = new NavigationCompletedHandler(() => Mark("navigation-completed"));
            _navigationToken = _webView.AddNavigationCompleted(_navigationHandler);
            var target = Option("--url") ?? _origin + "/" + (Option("--page") ?? "");
            Mark("navigate");
            Log($"NAVIGATE epoch={Epoch} url={target}");
            _webView.Navigate(target);

            var timeout = int.Parse(Option("--timeout") ?? "30", CultureInfo.InvariantCulture);
            var stopFile = Option("--stop-file");
            var deadline = Stopwatch.GetTimestamp() + (long)(timeout * (double)Stopwatch.Frequency);
            while (!_closing && Stopwatch.GetTimestamp() < deadline && !(stopFile is not null && File.Exists(stopFile)))
            {
                await Task.Delay(200);
            }

            if (!_closing && stopFile is not null && File.Exists(stopFile))
            {
                Log("STOP file found");
                Close();
            }

            if (!_closing)
            {
                Log($"TIMEOUT after {timeout} s");
                _exitCode = 3;
                Close();
            }
        }
        catch (Exception exception)
        {
            Log("HOST-FAILURE " + exception);
            _exitCode = 2;
            Close();
        }
    }

    private void OnPermissionRequested(ICoreWebView2PermissionRequestedEventArgs3 args)
    {
        var kind = args.GetPermissionKind();
        var uri = args.GetUri();
        var userInitiated = args.GetIsUserInitiated();

        // --grant takes a comma-separated list of permission kinds to allow (and save in the profile). Everything else
        // is denied without saving. Either way the request is settled here, so no prompt can appear.
        var allow = _grant.Split(',', StringSplitOptions.RemoveEmptyEntries).Contains(kind.ToString(CultureInfo.InvariantCulture));
        args.PutState(allow ? PermissionAllow : PermissionDeny);
        args.PutSavesInProfile(allow ? 1 : 0);
        Log($"PERMISSION epoch={Epoch} t={SinceNavigate:F0} kind={kind} uri={uri} user-initiated={userInitiated} -> {(allow ? "allow,saved" : "deny,not-saved")}");
    }

    private void OnWebResourceRequested(ICoreWebView2WebResourceRequestedEventArgs args)
    {
        var request = args.GetRequest();
        var uri = new Uri(request.GetUri());
        var method = request.GetMethod();
        var path = uri.AbsolutePath;
        var context = args.GetResourceContext();
        var sourceKind = args is ICoreWebView2WebResourceRequestedEventArgs2 args2 ? args2.GetRequestedSourceKind() : -1;
        var headers = request.GetHeaders();
        var cookie = headers.Contains("Cookie") != 0 ? headers.GetHeader("Cookie") : "";
        var prefix = $"REQ epoch={Epoch} t={SinceNavigate:F0} {method} {uri.PathAndQuery} ctx={context} src={sourceKind}" +
            (cookie.Length > 0 ? $" cookie=[{cookie}]" : "");

        if (path.StartsWith("/unanswered/", StringComparison.Ordinal))
        {
            // Deliberately no response: WebView2 lets the request continue to the network.
            Log(prefix + " -> UNANSWERED");
            return;
        }

        switch (path)
        {
            case "/_spar/result":
                Log("RESULT " + ReadBody(request));
                args.PutResponse(Respond([], 204, "No Content", "text/plain"));
                return;
            case "/_spar/done":
                Log(prefix + " -> done");
                args.PutResponse(Respond([], 204, "No Content", "text/plain"));
                Close();
                return;
            case "/_spar/ready":
                Mark("ui-ready");
                Log($"TIMING origin={_origin} host-navigate->ready={_marks["ui-ready"] - _marks["navigate"]:F1} " +
                    $"host-navigate->completed={_marks.GetValueOrDefault("navigation-completed", double.NaN) - _marks["navigate"]:F1} {uri.Query.TrimStart('?')}");
                args.PutResponse(Respond([], 204, "No Content", "text/plain"));
                if (Option("--stay") is null)
                {
                    Close();
                }

                return;
            case "/sse":
                Log(prefix + " -> 200 event-stream");
                args.PutResponse(Respond("retry: 600000\ndata: hello\n\n"u8, 200, "OK", "text/event-stream"));
                return;
        }

        if (_proxy is not null && !path.StartsWith("/_spar/", StringComparison.Ordinal))
        {
            Proxy(args, request, uri, prefix);
            return;
        }

        if (method == "POST" || path.StartsWith("/_spar/", StringComparison.Ordinal))
        {
            Log(prefix + " -> 204");
            args.PutResponse(Respond([], 204, "No Content", "text/plain"));
            return;
        }

        // Embedded names keep MSBuild's RecursiveDir separator: www/assets\index.js.
        var key = "www/" + (path.EndsWith('/') ? path + "index.html" : path)[1..].Replace('/', '\\');
        using var stream = typeof(Host).Assembly.GetManifestResourceStream(key);
        if (stream is null)
        {
            Log(prefix + " -> 404");
            args.PutResponse(Respond("not found"u8, 404, "Not Found", "text/plain"));
            return;
        }

        var content = new byte[stream.Length];
        stream.ReadExactly(content);
        Log(prefix + " -> 200");
        args.PutResponse(Respond(content, 200, "OK", ContentType(key), ExtraHeaders(key) + (_csp is null ? "" : "\r\nContent-Security-Policy: " + _csp)));
    }

    private async void Proxy(ICoreWebView2WebResourceRequestedEventArgs args, ICoreWebView2WebResourceRequest request, Uri uri, string prefix)
    {
        // Debug alternative: keep the app origin and fetch each request from the Vite dev server. The deferral keeps the
        // request open while the HttpClient call runs off the STA; the response is created back on the STA.
        var deferral = args.GetDeferral();
        var started = Stopwatch.GetTimestamp();
        var headers = request.GetHeaders();
        using var message = new HttpRequestMessage(new HttpMethod(request.GetMethod()), _proxy + uri.PathAndQuery);
        foreach (var name in (string[])["Accept", "Sec-Fetch-Dest", "Sec-Fetch-Mode", "If-None-Match"])
        {
            if (headers.Contains(name) != 0)
            {
                message.Headers.TryAddWithoutValidation(name, headers.GetHeader(name));
            }
        }

        try
        {
            using var response = await _http.SendAsync(message).ConfigureAwait(true);
            var body = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(true);
            var type = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            var status = (int)response.StatusCode;
            var extra = response.Headers.ETag is { } etag ? "\r\nETag: " + etag : "";
            args.PutResponse(Respond(body, status, response.ReasonPhrase ?? "OK", type, extra, cache: "no-cache"));
            Log($"{prefix} -> proxy {status} {body.Length} B in {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1} ms");
        }
        catch (Exception exception)
        {
            Log($"{prefix} -> proxy failure {exception.GetType().Name}: {exception.Message}");
            args.PutResponse(Respond("dev server unavailable"u8, 502, "Bad Gateway", "text/plain"));
        }
        finally
        {
            deferral.Complete();
        }
    }

    private static string ContentType(string key) => Path.GetExtension(key) switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".json" => "application/json",
        ".css" => "text/css",
        ".png" => "image/png",
        _ => "application/octet-stream",
    };

    // The service worker script may claim the root scope only with this header.
    private static string ExtraHeaders(string key) => key.EndsWith("/sw.js", StringComparison.Ordinal) ? "\r\nService-Worker-Allowed: /" : "";

    private static unsafe string ReadBody(ICoreWebView2WebResourceRequest request)
    {
        if (request.GetContent() is not { } content)
        {
            return "";
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[16384];
        while (true)
        {
            uint read = 0;
            fixed (byte* data = chunk)
            {
                content.Read((nint)data, (uint)chunk.Length, (nint)(&read));
            }

            if (read == 0)
            {
                break;
            }

            buffer.Write(chunk, 0, (int)read);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private unsafe ICoreWebView2WebResourceResponse Respond(ReadOnlySpan<byte> content, int status, string reason, string contentType, string extraHeaders = "", string cache = "no-store")
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
            return _environment!.CreateWebResourceResponse(stream, status, reason, $"Content-Type: {contentType}\r\nCache-Control: {cache}{extraHeaders}");
        }
        finally
        {
            Marshal.Release(stream);
        }
    }

    private void Close()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        _closeRequestedAt = Stopwatch.GetTimestamp();
        PInvoke.PostMessage(_window, PInvoke.WM_CLOSE, default, default);
    }

    private void Shutdown()
    {
        if (_webView is not null)
        {
            if (_resourceHandler is not null) _webView.RemoveWebResourceRequested(_resourceToken);
            if (_navigationHandler is not null) _webView.RemoveNavigationCompleted(_navigationToken);
            if (_permissionHandler is not null) _webView.RemovePermissionRequested(_permissionToken);
        }

        _resourceHandler = null;
        _navigationHandler = null;
        _permissionHandler = null;
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
            Mark(browser.WaitForExit(TimeSpan.FromSeconds(20)) ? "browser-exited" : "browser-still-running", sinceClose: true);
        }
        catch (ArgumentException)
        {
            Mark("browser-exited", sinceClose: true);
        }
    }

    private string ProfileDirectory()
    {
        // Spike rule: profiles live under the repository's artifacts/webview2-profiles/desktop-origin-*, never %LOCALAPPDATA%.
        var name = Option("--profile") ?? "desktop-origin";
        if (!name.StartsWith("desktop-origin", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The profile name must start with desktop-origin.");
        }

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")))
            {
                return Path.Combine(directory.FullName, "artifacts", "webview2-profiles", name);
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
