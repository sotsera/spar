using System.Buffers;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using React.Desktop.Interop;
using Spike;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

[assembly: SupportedOSPlatform("windows10.0")]

namespace React.Desktop;

/// <summary>What the app gives the desktop host: its dispatch, its contract manifest and its packaged frontend.</summary>
public sealed record DesktopApplication(
    string Title,
    SparInvoke Invoke,
    string Manifest,
    Func<Stream> OpenFrontend,
    Func<Task<string?>>? DevServer = null,
    Func<string, string, ReadOnlyMemory<byte>, CancellationToken, SparStream>? OpenStream = null,
    ISparTelemetryRelay? Telemetry = null);

/// <summary>
/// Spike desktop host. One STA thread owns the window, the WebView2 controller and the message loop. The page is served
/// from <c>https://app.spar.local/</c> by <c>WebResourceRequested</c>: the embedded frontend, and the bridge at
/// <c>/_spar/call/…</c>, the same URL shape the loopback host serves. A <c>postMessage</c> bridge exists too, for comparison.
/// </summary>
public sealed partial class DesktopHost
{
    public const string Origin = "https://app.spar.local";

    private static DesktopHost? s_current;
    private static long s_anonymousCalls;

    private readonly DesktopApplication _application;
    private readonly string[] _args;
    private readonly Stopwatch _clock;
    private readonly string _fingerprint;
    private readonly Task<Dictionary<string, byte[]>> _assets;
    private readonly bool _offscreen;
    private readonly bool _cacheable;
    private readonly bool _brotli;
    private readonly SparCallRegistry _calls = new();
    private int _staThreadId;
    private string? _document;
    private HWND _window;
    private UiContext? _ui;
    private ICoreWebView2Environment? _environment;
    private ICoreWebView2Controller? _controller;
    private ICoreWebView2? _webView;
    private NavigationCompletedHandler? _navigationHandler;
    private WebResourceRequestedHandler? _resourceHandler;
    private WebMessageReceivedHandler? _messageHandler;
    private NavigationStartingHandler? _navigationStartingHandler;
    private EventRegistrationToken _navigationStartingToken;
    private EventRegistrationToken _navigationToken;
    private EventRegistrationToken _resourceToken;
    private EventRegistrationToken _messageToken;
    private TaskCompletionSource _navigated = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource _uiReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private uint _browserProcessId;
    private long _closeRequestedAt;
    private string _pageOrigin = Origin;
    private bool _closeAsked;
    private bool _closeConfirmed;

    private DesktopHost(DesktopApplication application, string[] args, Stopwatch clock)
    {
        _application = application;
        _args = args;
        _clock = clock;
#if SPIKE_NO_SHA
        _fingerprint = application.Manifest.Length.ToString(System.Globalization.CultureInfo.InvariantCulture); // Size probe only.
#else
        _fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(application.Manifest)))[..16];
#endif
        _offscreen = args.Contains("--offscreen");
        _cacheable = args.Contains("--cacheable");
        _brotli = args.Contains("--brotli");
        PipeStream.ReturnPending = args.Contains("--istream-pending");

        // Unpack the frontend while the browser starts; the first request awaits it.
        _assets = Task.Run(() => LoadAssets(application.OpenFrontend));
    }

    /// <summary>Request counts per asset path, for the caching check.</summary>
    public Dictionary<string, int> AssetHits { get; } = [];

    public Dictionary<string, double> Marks { get; } = [];

    public int ExitCode { get; set; }

    /// <summary>Runs the window on its own STA thread and returns the exit code when the window has closed.</summary>
    public static int Run(DesktopApplication application, string[] args)
    {
        var clock = Stopwatch.StartNew();
#if !SPIKE_NO_PROCESS
        using (var self = Process.GetCurrentProcess())
        {
            Console.WriteLine($"[process start -> host: {(DateTime.Now - self.StartTime).TotalMilliseconds:F1} ms]");
        }
#endif

        var exitCode = 0;
        var thread = new Thread(() => exitCode = new DesktopHost(application, args, clock).RunOnSta()) { Name = "Spar UI" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return exitCode;
    }

    private int RunOnSta()
    {
        s_current = this;
        _staThreadId = Environment.CurrentManagedThreadId;
        CreateWindow();
        SynchronizationContext.SetSynchronizationContext(_ui);
        _ui!.Post(static async state => await ((DesktopHost)state!).StartAsync(), this);

        while (PInvoke.GetMessage(out var message, HWND.Null, 0, 0) > 0)
        {
            PInvoke.TranslateMessage(message);
            PInvoke.DispatchMessage(message);
        }

        Mark("message-loop-ended", sinceClose: true);
        WaitForBrowserExit();
        return ExitCode;
    }

    public void Close()
    {
        _closeRequestedAt = Stopwatch.GetTimestamp();
        PInvoke.PostMessage(_window, PInvoke.WM_CLOSE, default, default);
    }

    private unsafe void CreateWindow()
    {
        var instance = (HINSTANCE)(nint)PInvoke.GetModuleHandle((char*)null).Value;
        fixed (char* className = "SparDesktopSpike")
        fixed (char* title = _application.Title)
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

            var (x, y) = _offscreen ? (-32000, -32000) : (120, 120);
            _window = PInvoke.CreateWindowEx(0, className, title, WINDOW_STYLE.WS_OVERLAPPEDWINDOW, x, y, 960, 720, HWND.Null, HMENU.Null, instance, null);
            if (_window.IsNull)
            {
                throw new InvalidOperationException("CreateWindowEx failed: " + Marshal.GetLastPInvokeError());
            }
        }

        _ui = new UiContext(_window);
        PInvoke.ShowWindow(_window, _offscreen ? SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE : SHOW_WINDOW_CMD.SW_SHOWNORMAL);
        Mark("window");
    }

    private async Task StartAsync()
    {
        try
        {
            // A development build embeds no bundle: the page comes from the dev server, the bridge stays in-process.
            var devServerTask = _application.DevServer?.Invoke();
            var profile = ProfileDirectory();
            Directory.CreateDirectory(profile);
            var environmentHandler = new EnvironmentCompletedHandler();
            Marshal.ThrowExceptionForHR(NativeLoader.CreateCoreWebView2EnvironmentWithOptions(null, profile, 0, environmentHandler));
            _environment = await environmentHandler.Task;
            Mark("environment");

            var controllerHandler = new ControllerCompletedHandler();
            _environment.CreateCoreWebView2Controller(WindowHandle, controllerHandler);
            _controller = await controllerHandler.Task;
            Mark("controller");

            _webView = _controller.GetCoreWebView2();
            _browserProcessId = _webView.GetBrowserProcessId();
            ResizeController();

            _resourceHandler = new WebResourceRequestedHandler(OnWebResourceRequested);
            _resourceToken = _webView.AddWebResourceRequested(_resourceHandler);
            var devServer = devServerTask is null ? null : await devServerTask;
            if (devServer is not null)
            {
                _pageOrigin = new Uri(devServer).GetLeftPart(UriPartial.Authority);
                Mark("dev-server");
            }

            // Packaged: every request to our origin. Development: only the bridge, on the dev server's origin.
            _webView.AddWebResourceRequestedFilter(devServer is null ? Origin + "/*" : _pageOrigin + "/_spar/*", 0); // COREWEBVIEW2_WEB_RESOURCE_CONTEXT_ALL
            _messageHandler = new WebMessageReceivedHandler(OnWebMessageReceived);
            _messageToken = _webView.AddWebMessageReceived(_messageHandler);
            _navigationStartingHandler = new NavigationStartingHandler(OnNavigationStarting);
            _navigationStartingToken = _webView.AddNavigationStarting(_navigationStartingHandler);
            _navigationHandler = new NavigationCompletedHandler(() => _navigated.TrySetResult());
            _navigationToken = _webView.AddNavigationCompleted(_navigationHandler);

            _webView.Navigate(devServer ?? Origin + "/");
            await _navigated.Task;
            Mark("navigation-completed");
#if SPAR_SELFTEST
            if (_args.Contains("--selftest") && await Task.WhenAny(_uiReady.Task, Task.Delay(TimeSpan.FromSeconds(15))) != _uiReady.Task)
            {
                await SelfTest.DiagnoseAsync(this);
                ExitCode = 3;
                Close();
                return;
            }
#endif
            await _uiReady.Task;
            Mark("ui-ready");
            Console.WriteLine($"SPAR_UI_READY at={_clock.ElapsedMilliseconds}ms");

#if SPAR_SELFTEST
            if (_args.Contains("--selftest"))
            {
                await SelfTest.RunAsync(this);
            }
#endif
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Host failure: " + exception);
            ExitCode = 2;
            Close();
        }
    }

    // ---- Assets and the fetch bridge (WebResourceRequested, raised on the STA thread) ----

    private void OnWebResourceRequested(ICoreWebView2WebResourceRequestedEventArgs args)
    {
        var request = args.GetRequest();
        var path = new Uri(request.GetUri()).AbsolutePath;
        if (path.StartsWith("/_spar/", StringComparison.Ordinal))
        {
            OnBridgeRequest(args, request, path);
            return;
        }

        // The first request may arrive before the zip is unpacked; that wait is a few milliseconds at most.
        var assets = _assets.GetAwaiter().GetResult();
        var key = path == "/" ? "index.html" : path.TrimStart('/');
        lock (AssetHits)
        {
            AssetHits[key] = AssetHits.GetValueOrDefault(key) + 1;
        }

#if SPAR_SELFTEST
        if (_args.Contains("--gzip") && assets.TryGetValue(key, out var plain))
        {
            using var gz = new MemoryStream();
            using (var zip = new System.IO.Compression.GZipStream(gz, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true))
            {
                zip.Write(plain);
            }

            Console.WriteLine($"GZIP {key} {plain.Length}B -> {gz.Length}B");
            args.PutResponse(Respond(gz.ToArray(), 200, "OK", ContentType(key) + "\r\nContent-Encoding: gzip"));
            return;
        }

        // Probe: serve Brotli-compressed bytes and let the browser decode them (no decompression code in the app).
        if (_brotli && assets.TryGetValue(key, out var raw))
        {
            var encoded = new byte[System.IO.Compression.BrotliEncoder.GetMaxCompressedLength(raw.Length)];
            System.IO.Compression.BrotliEncoder.TryCompress(raw, encoded, out var written, quality: 11, window: 22);
            Console.WriteLine($"BROTLI {key} {raw.Length}B -> {written}B");
            args.PutResponse(Respond(encoded[..written], 200, "OK", ContentType(key) + "\r\nContent-Encoding: br"));
            return;
        }
#endif

        args.PutResponse(assets.TryGetValue(key, out var bytes)
            ? Respond(bytes, 200, "OK", ContentType(key))
            : Respond([], 404, "Not Found", "text/plain"));
    }

    private void OnBridgeRequest(ICoreWebView2WebResourceRequestedEventArgs args, ICoreWebView2WebResourceRequest request, string path)
    {
        var headers = request.GetHeaders();
        string? Header(string name) => headers.Contains(name) != 0 ? headers.GetHeader(name) : null;

        // Only the app's own page may call: same-origin requests carry our Origin; anything else is refused.
        if (request.GetMethod() != "POST" || Header("Origin") != _pageOrigin)
        {
            args.PutResponse(Error(403, "forbidden-origin", null));
            return;
        }

        if (path == "/_spar/ready")
        {
            _uiReady.TrySetResult();
            args.PutResponse(Respond([], 204, "No Content", "text/plain"));
            return;
        }

        if (path.StartsWith(SparTelemetryRoutes.Prefix, StringComparison.Ordinal))
        {
            OnTelemetryRequest(args, request, path, Header("Content-Type"));
            return;
        }

        var parts = path.Split('/');
        if (parts.Length != 5 || parts[2] is not ("call" or "stream"))
        {
            args.PutResponse(Error(404, "unknown-route", path));
            return;
        }

        if (Header("X-Spar-Contracts") != _fingerprint)
        {
            args.PutResponse(Error(409, "contract-mismatch", _fingerprint));
            return;
        }

        var body = ReadAll(request.GetContent());
        var document = Header("X-Spar-Document") ?? "-";
        var id = Header("X-Spar-Call") ?? "f" + Interlocked.Increment(ref s_anonymousCalls).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var call = _calls.Start(document, id, SparTraceContext.FromHeaders(Header("traceparent"), Header("tracestate")));
        if (call is null)
        {
            // The page cancelled before the call arrived: it never runs.
            args.PutResponse(Error(400, "cancelled-before-start", null));
            return;
        }

        if (parts[2] == "stream")
        {
            var batch = int.TryParse(Header("X-Spar-Batch"), out var size) ? Math.Clamp(size, 1, 10_000) : 100;
            args.PutResponse(StartFetchStream(call, parts[3], parts[4], body, batch));
            return;
        }

        var deferral = args.GetDeferral();
        _ = CompleteCallAsync(args, deferral, call, parts[3], parts[4], body);
    }

    /// <summary>The page's OTLP exports: relayed unchanged by the optional telemetry layer, 404 without it.</summary>
    private void OnTelemetryRequest(ICoreWebView2WebResourceRequestedEventArgs args, ICoreWebView2WebResourceRequest request, string path, string? contentType)
    {
        var body = ReadAll(request.GetContent());
        var deferral = args.GetDeferral();
        _ = CompleteTelemetryAsync(args, deferral, path, body, contentType);
    }

    private async Task CompleteTelemetryAsync(
        ICoreWebView2WebResourceRequestedEventArgs args, ICoreWebView2Deferral deferral, string path, byte[] body, string? contentType)
    {
        var timer = Stopwatch.StartNew();
        var relay = _application.Telemetry;
        var response = await Task.Run(() => SparTelemetryRoutes.HandleAsync(relay, path, body, contentType, CancellationToken.None));
        Console.WriteLine($"SPAR_OTLP {path} {response.Status} in={body.Length}B {timer.Elapsed.TotalMilliseconds:F1}ms");

        // Back on the STA thread.
        try
        {
            args.PutResponse(Respond(response.Body.ToArray(), response.Status, response.Status == 200 ? "OK" : "Error", response.ContentType));
            deferral.Complete();
        }
        catch (COMException exception)
        {
            Console.WriteLine($"SPAR_LATE_REPLY otlp 0x{exception.HResult:X8}");
        }
    }

    private async Task CompleteCallAsync(
        ICoreWebView2WebResourceRequestedEventArgs args, ICoreWebView2Deferral deferral, SparCall call, string contract, string command, byte[] body)
    {
        var reply = await InvokeAsync(call, contract, command, body);

        // Back on the STA thread (the awaits keep the UI context).
        try
        {
            var (status, reason) = reply.Kind switch
            {
                'r' => (200, "OK"),
                'c' => (499, "Cancelled"),
                'n' when reply.Field == "unknown-command" => (404, "Not Found"),
                'n' => (400, "Not Executed"),
                _ => (500, "Outcome Unknown"),
            };
            args.PutResponse(Respond(reply.Json.ToArray(), status, reason, "application/json"));
            deferral.Complete();
        }
        catch (COMException exception)
        {
            // The document that made the request is gone (navigation or close).
            Console.WriteLine($"SPAR_LATE_REPLY id={call.Id} 0x{exception.HResult:X8}");
        }
    }

    private async Task<SparMessage> InvokeAsync(SparCall call, string contract, string command, byte[] body)
    {
        var timer = Stopwatch.StartNew();

        // Backend work leaves the UI thread; the continuation returns to it.
        var reply = await Task.Run(() => SparDispatch.InvokeAsync(_application.Invoke, call, contract, command, body));
        _calls.End(call);
        if (!command.Equals("echo", StringComparison.Ordinal))
        {
            Console.WriteLine($"SPAR_CALL {contract}.{command} {reply.Outcome} {timer.ElapsedMilliseconds}ms in={body.Length}B out={reply.Json.Length}B{CancelLatency(call, reply)}");
        }

        return reply;
    }

    /// <summary>Streams over the fetch response itself: a pipe that WebView2 reads while the command writes.</summary>
    private ICoreWebView2WebResourceResponse StartFetchStream(SparCall call, string contract, string command, byte[] body, int batch)
    {
        var pipe = new PipeStream(capacity: 256 * 1024, _staThreadId);
        _ = Task.Run(async () =>
        {
            await SparDispatch.PumpAsync(
                token => OpenStream(contract, command, body, token),
                call,
                batch,
                useCredits: false,
                (message, token) =>
                {
                    LogFinal(call, message);
                    var buffer = new ArrayBufferWriter<byte>(message.Json.Length + 32);
                    message.WriteTo(buffer, newline: true);
                    return pipe.WriteAsync(buffer.WrittenMemory, token);
                });
            pipe.Complete();
            _calls.End(call);
        });
        return RespondStream(pipe);
    }

    private SparStream OpenStream(string contract, string command, byte[] body, CancellationToken cancellationToken) =>
        _application.OpenStream is { } open
            ? open(contract, command, body, cancellationToken)
            : throw new SparUnknownCommandException("This app has no streaming commands.");

    // ---- The postMessage channel: unary calls, streams with credits, and cancellation (protocol in SparMessage) ----

    private void OnNavigationStarting()
    {
        // A new document is coming: nothing of the old one may keep running or receive replies.
        var cancelled = _calls.NewEpoch();
        _document = null;
        if (cancelled > 0)
        {
            Console.WriteLine($"SPAR_NAVIGATION cancelled={cancelled}");
        }
    }

    private void OnWebMessageReceived(ICoreWebView2WebMessageReceivedEventArgs args)
    {
        if (!args.GetSource().StartsWith(_pageOrigin + "/", StringComparison.Ordinal)
            || args.TryGetWebMessageAsString(out var message) < 0 || message is null)
        {
            return;
        }

        if (message == "z")
        {
            // The page finished its closing work (a telemetry flush).
            FinishClose("confirmed");
            return;
        }

        if (message.Length < 3)
        {
            return;
        }

        var fields = message.Split(' ', message[0] switch { 'q' => 7, 's' => 9, 'k' => 3, _ => 2 });
        var document = _document ?? "-";
        switch (message[0])
        {
            case 'h':
                _document = fields[1];
                return;
            case 'x' when fields.Length == 2:
                if (_calls.Find(document, fields[1]) is null)
                {
                    Console.WriteLine($"SPAR_CANCEL id={fields[1]} not-running (remembered in case the call arrives later)");
                }

                _calls.Cancel(document, fields[1]);
                return;
            case 'k' when fields.Length == 3 && int.TryParse(fields[2], out var credits):
                _calls.Find(document, fields[1])?.AddCredits(credits);
                return;
            case 'q' when fields.Length == 7:
                StartMessageCall(document, fields[1], fields[2], fields[3], fields[4], SparTraceContext.FromField(fields[5]), fields[6], stream: false, batch: 0, credits: 0);
                return;
            case 's' when fields.Length == 9 && int.TryParse(fields[6], out var batch) && int.TryParse(fields[7], out var window):
                StartMessageCall(document, fields[1], fields[2], fields[3], fields[4], SparTraceContext.FromField(fields[5]), fields[8], stream: true, Math.Clamp(batch, 1, 10_000), window);
                return;
        }
    }

    private void StartMessageCall(string document, string id, string contract, string command, string fingerprint, SparTraceContext trace, string body, bool stream, int batch, int credits)
    {
        var call = _calls.Start(document, id, trace);
        if (call is null)
        {
            PostWebMessage(_calls.Epoch, SparMessage.NotExecuted(id, "cancelled-before-start", null).ToString());
            return;
        }

        if (fingerprint != _fingerprint)
        {
            _calls.End(call);
            Send(call, SparMessage.NotExecuted(id, "contract-mismatch", _fingerprint));
            return;
        }

        var payload = Encoding.UTF8.GetBytes(body);
        if (!stream)
        {
            _ = ReplyAsync(call, contract, command, payload);
            return;
        }

        call.AddCredits(credits);
        _ = Task.Run(async () =>
        {
            await SparDispatch.PumpAsync(
                token => OpenStream(contract, command, payload, token),
                call,
                batch,
                useCredits: true,
                (message, _) =>
                {
                    Send(call, message);
                    return ValueTask.CompletedTask;
                });
            _calls.End(call);
        });
    }

    private async Task ReplyAsync(SparCall call, string contract, string command, byte[] payload) =>
        Send(call, await InvokeAsync(call, contract, command, payload));

    /// <summary>Formats now (the caller reuses its buffer), posts on the STA, and drops it if the document changed.</summary>
    private void Send(SparCall call, SparMessage message)
    {
        LogFinal(call, message);
        PostWebMessage(call.Epoch, message.ToString());
    }

    private void PostWebMessage(long epoch, string text)
    {
        _ui!.Post(static state =>
        {
            var (host, epoch, text) = ((DesktopHost, long, string))state!;
            if (epoch == host._calls.Epoch && host._webView is { } webView)
            {
                webView.PostWebMessageAsString(text);
            }
        }, (this, epoch, text));
    }

    private static void LogFinal(SparCall call, SparMessage message)
    {
        if (message.Kind is 'c' or 'e' or 'n' or 'u')
        {
            Console.WriteLine($"SPAR_STREAM id={call.Id} {message.Outcome}{CancelLatency(call, message)}");
        }
    }

    private static string CancelLatency(SparCall call, SparMessage message) =>
        call.CancelRequestedAt == 0 ? string.Empty
            : $" cancel->{message.Outcome}={Stopwatch.GetElapsedTime(call.CancelRequestedAt).TotalMilliseconds:F2}ms";

    // ---- Helpers ----

    private unsafe ICoreWebView2WebResourceResponse Respond(byte[] content, int status, string reason, string contentType)
    {
        nint stream;
        fixed (byte* data = content)
        {
            stream = NativeLoader.SHCreateMemStream(data, (uint)content.Length);
        }

        if (stream == 0)
        {
            throw new OutOfMemoryException("SHCreateMemStream failed.");
        }

        try
        {
            return _environment!.CreateWebResourceResponse(stream, status, reason, $"Content-Type: {contentType}\r\nCache-Control: {(_cacheable ? "max-age=3600" : "no-store")}");
        }
        finally
        {
            Marshal.Release(stream);
        }
    }

    private unsafe ICoreWebView2WebResourceResponse RespondStream(PipeStream pipe)
    {
        // Our reference is released after the response took its own.
        var stream = ComInterfaceMarshaller<IStream>.ConvertToUnmanaged(pipe);
        try
        {
            return _environment!.CreateWebResourceResponse((nint)stream, 200, "OK", "Content-Type: application/x-ndjson\r\nCache-Control: no-store");
        }
        finally
        {
            ComInterfaceMarshaller<IStream>.Free(stream);
        }
    }

    private ICoreWebView2WebResourceResponse Error(int status, string error, string? detail) =>
        Respond(ErrorBody(error, detail), status, "Error", "application/json");

    private static byte[] ErrorBody(string error, string? detail)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new System.Text.Json.Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("error"u8, error);
            writer.WriteString("detail"u8, detail);
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
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

    private static Dictionary<string, byte[]> LoadAssets(Func<Stream> open)
    {
#if SPIKE_NO_ZIP
        return open is null ? [] : []; // Size probe only: not functional.
#elif SPIKE_BROTLI
        // Size probe only: decode one Brotli blob instead of reading a zip.
        using var input = new MemoryStream();
        open().CopyTo(input);
        var output = new byte[input.Length * 8];
        System.IO.Compression.BrotliDecoder.TryDecompress(input.ToArray(), output, out var written);
        return new() { ["index.html"] = output[..written] };
#else
        using var archive = new ZipArchive(open(), ZipArchiveMode.Read);
        var assets = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            using var source = entry.Open();
            using var copy = new MemoryStream((int)entry.Length);
            source.CopyTo(copy);
            assets[entry.FullName] = copy.ToArray();
        }

        return assets;
#endif
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

    /// <summary>Spike rule: profiles live under the repository's <c>artifacts/webview2-profiles/</c>, never %LOCALAPPDATA%.</summary>
    private string ProfileDirectory()
    {
        var name = _args.Contains("--selftest") ? "browser-telemetry-desktop-selftest" : "browser-telemetry-desktop";
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")))
            {
                return Path.Combine(directory.FullName, "artifacts", "webview2-profiles", name);
            }
        }

        return Path.Combine(AppContext.BaseDirectory, "webview2-profile");
    }

    internal bool IsDevServer => _pageOrigin != Origin;

    internal ICoreWebView2 WebView => _webView ?? throw new InvalidOperationException("The WebView is not created.");

    internal string Fingerprint => _fingerprint;

    internal uint BrowserProcessId => _browserProcessId;

    /// <summary>Waits for the next <c>/_spar/ready</c> from the page (after a reload, for instance).</summary>
    internal Task NextUiReady()
    {
        _uiReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return _uiReady.Task;
    }

    internal void Mark(string name, bool sinceClose = false)
    {
        var milliseconds = sinceClose && _closeRequestedAt != 0
            ? Stopwatch.GetElapsedTime(_closeRequestedAt).TotalMilliseconds
            : _clock.Elapsed.TotalMilliseconds;
        Marks[name] = milliseconds;
        Console.WriteLine($"[{milliseconds,8:F1} ms] {name}");
    }

    private unsafe nint WindowHandle => (nint)_window.Value;

    private void ResizeController()
    {
        if (_controller is null)
        {
            return;
        }

        PInvoke.GetClientRect(_window, out var client);
        _controller.PutBounds(new Rect { Left = 0, Top = 0, Right = client.right, Bottom = client.bottom });
    }

    /// <summary>
    /// With the telemetry layer on, a close first asks the page ("z") to flush its exporters, and waits at most 1 s for
    /// its "z". Returns true while waiting (the window stays open). Without the layer the window closes at once.
    /// </summary>
    private bool AskPageBeforeClose()
    {
        if (_closeConfirmed || _application.Telemetry is null || _webView is null || _document is null)
        {
            return false;
        }

        if (!_closeAsked)
        {
            _closeAsked = true;
            Mark("close-asks-page", sinceClose: true);
            _webView.PostWebMessageAsString("z");
            _ = CloseAfterTimeoutAsync();
        }

        return true;
    }

    private async Task CloseAfterTimeoutAsync()
    {
        await Task.Delay(1000);
        FinishClose("timeout");
    }

    private void FinishClose(string how)
    {
        if (!_closeAsked || _closeConfirmed)
        {
            return;
        }

        _closeConfirmed = true;
        Mark("close-page-" + how, sinceClose: true);
        PInvoke.PostMessage(_window, PInvoke.WM_CLOSE, default, default);
    }

    private void Shutdown()
    {
        if (_webView is not null)
        {
            if (_navigationHandler is not null) _webView.RemoveNavigationCompleted(_navigationToken);
            if (_resourceHandler is not null) _webView.RemoveWebResourceRequested(_resourceToken);
            if (_messageHandler is not null) _webView.RemoveWebMessageReceived(_messageToken);
            if (_navigationStartingHandler is not null) _webView.RemoveNavigationStarting(_navigationStartingToken);
        }

        _navigationHandler = null;
        _resourceHandler = null;
        _messageHandler = null;
        _navigationStartingHandler = null;

        // Closing: stop admitting work and cancel what runs; late replies are dropped by the epoch check.
        _calls.NewEpoch();
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

#if SPIKE_NO_PROCESS
        if (_browserProcessId != 0)
        {
            return; // Size probe only.
        }
#endif

        try
        {
            using var browser = Process.GetProcessById((int)_browserProcessId);
            var exited = browser.WaitForExit(TimeSpan.FromSeconds(10));
            Mark(exited ? "browser-exited" : "browser-still-running-after-10s", sinceClose: true);
        }
        catch (ArgumentException)
        {
            Mark("browser-exited", sinceClose: true);
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
                    if (host is not null && host._closeRequestedAt == 0)
                    {
                        host._closeRequestedAt = Stopwatch.GetTimestamp();
                    }

                    if (host is not null && host.AskPageBeforeClose())
                    {
                        return default;
                    }

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
