using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Bridge.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

[assembly: SupportedOSPlatform("windows10.0.17763")]

namespace Bridge;

/// <summary>
/// One run: an offscreen, never activated window with WebView2; the page runs one mode (micro, full or probe) for one
/// encoding and posts its result; the host writes it to artifacts/bridge-encoding/desktop/, closes, and waits for the
/// browser process to exit. Usage: <c>Bridge.Desktop --mode full --enc A --run 1</c>.
/// </summary>
internal sealed class Host
{
    public const string Origin = "https://app.spar.invalid";

    private static Host? s_current;
    private readonly TaskCompletionSource<string> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _mode;
    private readonly string _encoding;
    private readonly string _run;
    private readonly string _root;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private HWND _window;
    private UiContext? _ui;
    private int _uiThread;
    private ICoreWebView2Environment? _environment;
    private ICoreWebView2Controller? _controller;
    private ICoreWebView2? _webView;
    private WebResourceRequestedHandler? _resourceHandler;
    private WebMessageReceivedHandler? _messageHandler;
    private NavigationCompletedHandler? _navigationHandler;
    private BridgeSession? _session;
    private bool _performanceEnabled;
    private uint _browserProcessId;

    private Host(string mode, string encoding, string run, string root)
    {
        _mode = mode;
        _encoding = encoding;
        _run = run;
        _root = root;
    }

    public int ExitCode { get; private set; }

    [STAThread]
    private static int Main(string[] args)
    {
        string Arg(string name, string fallback)
        {
            var at = Array.IndexOf(args, "--" + name);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : fallback;
        }

        var host = new Host(Arg("mode", "micro"), Arg("enc", "A"), Arg("run", "1"), Root());
        s_current = host;
        host.CreateWindow();
        SynchronizationContext.SetSynchronizationContext(host._ui);
        host._ui!.Post(static async state => await ((Host)state!).RunAsync(), host);
        while (PInvoke.GetMessage(out var message, HWND.Null, 0, 0) > 0)
        {
            PInvoke.TranslateMessage(message);
            PInvoke.DispatchMessage(message);
        }

        host.WaitForBrowserExit();
        return host.ExitCode;
    }

    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Repository root (global.json) not found; refusing to use a default profile.");
    }

    private async Task RunAsync()
    {
        var cpuStart = Process.GetCurrentProcess().TotalProcessorTime;
        try
        {
            var profile = Path.Combine(_root, "artifacts", "webview2-profiles", "bridge-encoding");
            Directory.CreateDirectory(profile);
            var environmentHandler = new EnvironmentCompletedHandler();
            Marshal.ThrowExceptionForHR(NativeLoader.CreateCoreWebView2EnvironmentWithOptions(null, profile, 0, environmentHandler));
            _environment = await environmentHandler.Task;
            var controllerHandler = new ControllerCompletedHandler();
            _environment.CreateCoreWebView2Controller(WindowHandle(), controllerHandler);
            _controller = await controllerHandler.Task;
            _webView = _controller.GetCoreWebView2();
            _browserProcessId = _webView.GetBrowserProcessId();
            Resize();

            _resourceHandler = new WebResourceRequestedHandler(OnWebResourceRequested);
            _webView.AddWebResourceRequested(_resourceHandler);
            _webView.AddWebResourceRequestedFilter(Origin + "/*", 0);
            _messageHandler = new WebMessageReceivedHandler(OnWebMessageReceived);
            _webView.AddWebMessageReceived(_messageHandler);
            var navigated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _navigationHandler = new NavigationCompletedHandler(() => navigated.TrySetResult());
            _webView.AddNavigationCompleted(_navigationHandler);

            _session = new BridgeSession(Codecs.Get(_encoding), SendFrame);
            _webView.Navigate($"{Origin}/bench.html?mode={_mode}&enc={_encoding}");
            await navigated.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Console.WriteLine($"[{_clock.Elapsed.TotalMilliseconds,8:F0} ms] navigated, mode={_mode} enc={_encoding} run={_run}");

            var result = await _done.Task.WaitAsync(TimeSpan.FromSeconds(120));
            var cpu = Process.GetCurrentProcess().TotalProcessorTime - cpuStart;
            var stats = _session.Stats;
            var hostJson = $"{{\"cpuMs\":{cpu.TotalMilliseconds:F0},\"malformed\":{stats.Malformed},\"unrecognized\":{stats.Unrecognized},\"framesOut\":{stats.FramesOut},\"bytesOut\":{stats.BytesOut},\"pushFrames\":{stats.PushFrames},\"pushItems\":{stats.PushItems},\"runtime\":\"{RuntimeInformation.FrameworkDescription}\",\"aot\":{(!RuntimeFeature.IsDynamicCodeSupported ? "true" : "false")}}}";
            var output = Path.Combine(_root, "artifacts", "bridge-encoding", "desktop");
            Directory.CreateDirectory(output);
            var file = Path.Combine(output, $"{_mode}-{_encoding}-{_run}.json");
            File.WriteAllText(file, $"{{\"hostSide\":{hostJson},\"page\":{result}}}", new UTF8Encoding(false));
            Console.WriteLine($"[{_clock.Elapsed.TotalMilliseconds,8:F0} ms] result written: {Path.GetRelativePath(_root, file)}");
            if (result.Contains("\"error\":", StringComparison.Ordinal) && result.Contains("\"stack\":", StringComparison.Ordinal))
            {
                ExitCode = 3;
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Host failure: " + exception);
            ExitCode = 2;
        }

        _session?.Dispose();
        PInvoke.PostMessage(_window, PInvoke.WM_CLOSE, default, default);
    }

    // ---------------------------------------------------------------- bridge

    private void SendFrame(ReadOnlyMemory<byte> frame)
    {
        var text = Encoding.UTF8.GetString(frame.Span);
        if (Environment.CurrentManagedThreadId == _uiThread)
        {
            Post(text);
        }
        else
        {
            _ui!.Post(static state => { var (host, message) = ((Host, string))state!; host.Post(message); }, (this, text));
        }
    }

    private void Post(string text)
    {
        if (_webView is null)
        {
            return;
        }

        if (_encoding == "Bj")
        {
            _webView.PostWebMessageAsJson(text);
        }
        else
        {
            _webView.PostWebMessageAsString(text);
        }
    }

    private void OnWebMessageReceived(ICoreWebView2WebMessageReceivedEventArgs args)
    {
        if (!args.GetSource().StartsWith(Origin + "/", StringComparison.Ordinal))
        {
            return;
        }

        if (_mode == "probe")
        {
            Probe(args);
            return;
        }

        string? message;
        if (_encoding == "Bj")
        {
            message = args.GetWebMessageAsJson();
        }
        else if (args.TryGetWebMessageAsString(out message) < 0)
        {
            return;
        }

        if (message is null || (message.Length > 0 && message[0] == '#'))
        {
            return;
        }

        _session!.Receive(Encoding.UTF8.GetBytes(message));
    }

    // ---------------------------------------------------------------- transport probes

    private void Probe(ICoreWebView2WebMessageReceivedEventArgs args)
    {
        var json = args.GetWebMessageAsJson();
        var hr = args.TryGetWebMessageAsString(out var text);
        var webView = _webView!;
        if (text == "#send-all")
        {
            var lone = "a" + (char)0xD800 + "b";
            var separators = "a" + (char)0x2028 + "b\nc" + (char)0 + "d";
            Try("string with a lone surrogate", () => webView.PostWebMessageAsString(lone));
            Try("string with U+2028, newline, NUL", () => webView.PostWebMessageAsString(separators));
            Try("JSON with an escaped lone surrogate", () => webView.PostWebMessageAsJson("{\"t\":\"a\\ud800b\"}"));
            Try("JSON with a raw lone surrogate", () => webView.PostWebMessageAsJson("{\"t\":\"" + lone + "\"}"));
            Try("JSON with a 20-digit number", () => webView.PostWebMessageAsJson("{\"n\":12345678901234567890}"));
            Try("JSON with __proto__", () => webView.PostWebMessageAsJson("{\"__proto__\":{\"x\":1}}"));
            Try("JSON with a duplicate key", () => webView.PostWebMessageAsJson("{\"id\":7,\"id\":8}"));
            Try("not JSON", () => webView.PostWebMessageAsJson("r 7 {\"ok\":true}"));
            Try("two JSON values", () => webView.PostWebMessageAsJson("{\"a\":1}{\"b\":2}"));
            Try("JSON with trailing whitespace and newline", () => webView.PostWebMessageAsJson("{\"a\":1}\n "));
            webView.PostWebMessageAsString("#done");
            return;
        }

        webView.PostWebMessageAsString($"#seen tryString=0x{hr:X8} string={Describe(text)} json={Describe(json)}");
        webView.PostWebMessageAsString("#done");

        void Try(string name, Action post)
        {
            webView.PostWebMessageAsString("#case " + name);
            try
            {
                post();
            }
            catch (Exception exception)
            {
                webView.PostWebMessageAsString($"#threw 0x{exception.HResult:X8} {exception.GetType().Name}");
            }
        }
    }

    private static string Describe(string? value)
    {
        if (value is null)
        {
            return "null";
        }

        var builder = new StringBuilder();
        builder.Append(value.Length).Append(" units [");
        foreach (var ch in value.AsSpan(0, Math.Min(value.Length, 60)))
        {
            builder.Append(ch is >= ' ' and < (char)0x7F ? ch.ToString() : $"<{(int)ch:X4}>");
        }

        return builder.Append(']').ToString();
    }

    // ---------------------------------------------------------------- page and bench endpoints

    private void OnWebResourceRequested(ICoreWebView2WebResourceRequestedEventArgs args)
    {
        var request = args.GetRequest();
        var method = request.GetMethod();
        var path = new Uri(request.GetUri()).AbsolutePath;
        var body = ReadAll(request.GetContent());
        switch (method, path)
        {
            case ("GET", "/bench.html"):
                args.PutResponse(Respond(200, "text/html; charset=utf-8", Corpus.PageFile("bench.html")));
                return;
            case ("GET", "/bench.js"):
                args.PutResponse(Respond(200, "text/javascript; charset=utf-8", Corpus.PageFile("bench.js")));
                return;
            case ("GET", "/corpus.js"):
                args.PutResponse(Respond(200, "text/javascript; charset=utf-8", Corpus.Script()));
                return;
            case ("POST", "/bench/mark"):
                {
                    var deferral = args.GetDeferral();
                    _ = MarkAsync(args, deferral);
                    return;
                }

            case ("POST", "/bench/result"):
                args.PutResponse(Respond(200, "application/json", "{}"u8.ToArray()));
                _done.TrySetResult(Encoding.UTF8.GetString(body));
                return;
            default:
                args.PutResponse(Respond(404, "text/plain", []));
                return;
        }
    }

    private async Task MarkAsync(ICoreWebView2WebResourceRequestedEventArgs args, ICoreWebView2Deferral deferral)
    {
        var json = "{}";
        try
        {
            if (!_performanceEnabled)
            {
                await CallDevToolsAsync("Performance.enable", "{}");
                _performanceEnabled = true;
            }

            json = Metrics(await CallDevToolsAsync("Performance.getMetrics", "{}"));
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("CDP failed: " + exception.Message);
        }

        args.PutResponse(Respond(200, "application/json", Encoding.UTF8.GetBytes(json)));
        deferral.Complete();
    }

    private Task<string> CallDevToolsAsync(string method, string parameters)
    {
        var handler = new DevToolsCallHandler(method);
        _webView!.CallDevToolsProtocolMethod(method, parameters, handler);
        return handler.Task;
    }

    /// <summary>Keeps TaskDuration and ScriptDuration (seconds) from a Performance.getMetrics answer.</summary>
    private static string Metrics(string answer)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(answer));
        string? name = null;
        double task = -1, script = -1;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName && reader.ValueTextEquals("name"u8))
            {
                reader.Read();
                name = reader.GetString();
            }
            else if (reader.TokenType == JsonTokenType.PropertyName && reader.ValueTextEquals("value"u8))
            {
                reader.Read();
                if (name == "TaskDuration")
                {
                    task = reader.GetDouble();
                }
                else if (name == "ScriptDuration")
                {
                    script = reader.GetDouble();
                }
            }
        }

        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{{\"TaskDuration\":{task},\"ScriptDuration\":{script}}}");
    }

    private unsafe ICoreWebView2WebResourceResponse Respond(int status, string contentType, byte[] body)
    {
        nint stream;
        fixed (byte* data = body)
        {
            stream = NativeLoader.SHCreateMemStream(data, (uint)body.Length);
        }

        if (stream == 0)
        {
            throw new OutOfMemoryException("SHCreateMemStream failed.");
        }

        try
        {
            return _environment!.CreateWebResourceResponse(stream, status, status == 200 ? "OK" : "Not Found", $"Content-Type: {contentType}\r\nCache-Control: no-store");
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

    // ---------------------------------------------------------------- window and lifetime

    private unsafe nint WindowHandle() => (nint)_window.Value;

    private void Resize()
    {
        if (_controller is not null)
        {
            PInvoke.GetClientRect(_window, out var client);
            _controller.PutBounds(new Rect { Left = 0, Top = 0, Right = client.right, Bottom = client.bottom });
        }
    }

    private void Shutdown()
    {
        _resourceHandler = null;
        _messageHandler = null;
        _navigationHandler = null;
        _controller?.Close();
        _controller = null;
        _webView = null;
        _environment = null;
    }

    private unsafe void CreateWindow()
    {
        var instance = (HINSTANCE)(nint)PInvoke.GetModuleHandle((char*)null).Value;
        fixed (char* className = "SparBridgeEncodingSpike")
        fixed (char* title = "bridge-encoding spike")
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WindowProcedure,
                hInstance = instance,
                lpszClassName = className,
            };
            if (PInvoke.RegisterClassEx(windowClass) == 0)
            {
                throw new InvalidOperationException("RegisterClassEx failed: " + Marshal.GetLastPInvokeError());
            }

            // Shown, offscreen and never activated (a hidden window throttles timers and never paints).
            _window = PInvoke.CreateWindowEx(WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW, className, title,
                WINDOW_STYLE.WS_OVERLAPPEDWINDOW, -32000, -32000, 900, 640, HWND.Null, HMENU.Null, instance, null);
            if (_window.IsNull)
            {
                throw new InvalidOperationException("CreateWindowEx failed: " + Marshal.GetLastPInvokeError());
            }
        }

        _uiThread = Environment.CurrentManagedThreadId;
        _ui = new UiContext(_window);
        PInvoke.ShowWindow(_window, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
    }

    private void WaitForBrowserExit()
    {
        if (_browserProcessId == 0)
        {
            return;
        }

        var closedAt = Stopwatch.GetTimestamp();
        try
        {
            using var browser = Process.GetProcessById((int)_browserProcessId);
            var exited = browser.WaitForExit(TimeSpan.FromSeconds(20));
            Console.WriteLine($"browser {(exited ? "exited" : "STILL RUNNING")} {Stopwatch.GetElapsedTime(closedAt).TotalMilliseconds:F0} ms after the loop ended");
        }
        catch (ArgumentException)
        {
            Console.WriteLine("browser already exited");
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
            Console.Error.WriteLine("Window procedure failure: " + exception);
        }

        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }
}

/// <summary>Single-threaded synchronization context drained from the window's message loop.</summary>
internal sealed class UiContext(HWND window) : SynchronizationContext
{
    public const uint DrainMessage = PInvoke.WM_APP + 1;

    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();
    private int _signalled;

    public override void Post(SendOrPostCallback d, object? state)
    {
        _queue.Enqueue((d, state));
        if (Interlocked.Exchange(ref _signalled, 1) == 0)
        {
            PInvoke.PostMessage(window, DrainMessage, default, default);
        }
    }

    public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException();

    public void Drain()
    {
        Volatile.Write(ref _signalled, 0);
        while (_queue.TryDequeue(out var item))
        {
            item.Callback(item.State);
        }
    }
}
