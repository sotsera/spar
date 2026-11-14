using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using Spike;

namespace Linux.Desktop;

/// <summary>What the app gives the desktop host: its dispatch, its contract manifest and its packaged frontend.</summary>
public sealed record DesktopApplication(
    string Title,
    SparInvoke Invoke,
    string Manifest,
    Assembly Frontend,
    Func<string, string, ReadOnlyMemory<byte>, CancellationToken, SparStream>? OpenStream = null,
    ISparTelemetryRelay? Telemetry = null);

/// <summary>
/// Spike Linux desktop host. One thread owns GTK, the WebKitGTK view and the GLib main loop. The page is served from
/// <c>spar://app/</c> by a custom URI scheme: the embedded frontend, the fetch bridge at <c>/_spar/call/…</c> and a
/// streamed <c>/_spar/stream/…</c> response. A script message handler plus <c>evaluate_javascript</c> carry the
/// message channel; an injected shim presents it as <c>chrome.webview</c>, which the generated SDK already uses.
/// </summary>
public sealed partial class LinuxHost
{
    public const string Origin = "spar://app";

    // Presents WebKit's message handler with the WebView2 shape the SDK expects. Top frame only.
    private const string Shim = """
        (() => {
          const handler = window.webkit.messageHandlers.spar;
          const target = new EventTarget();
          window.chrome = window.chrome || {};
          window.chrome.webview = {
            postMessage: message => handler.postMessage(message),
            addEventListener: (type, listener) => target.addEventListener(type, listener),
            removeEventListener: (type, listener) => target.removeEventListener(type, listener),
          };
          Object.defineProperty(window, "__sparDeliver", {
            value: messages => { for (const data of messages) target.dispatchEvent(new MessageEvent("message", { data })); },
          });
        })();
        """;

    private static LinuxHost? s_current;

    private readonly DesktopApplication _application;
    private readonly string[] _args;
    private readonly Stopwatch _clock;
    private readonly string _fingerprint;
    private readonly Dictionary<string, byte[]> _assets = new(StringComparer.Ordinal);
    private readonly SparCallRegistry _calls = new();
    private readonly Queue<(long Epoch, string Text)> _outbox = new();
    private readonly string _dataDirectory;
    private int _flushScheduled;
    private string? _document;
    private nint _window;
    private nint _webView;
    private nint _webContext;
    private nint _loop;
    private GLibContext? _ui;
    private long _closeRequestedAt;
    private bool _loggedFetchHeaders;
    private bool _closeAsked;
    private bool _closeConfirmed;
    private TaskCompletionSource _uiReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource _loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private LinuxHost(DesktopApplication application, string[] args, Stopwatch clock)
    {
        _application = application;
        _args = args;
        _clock = clock;
        _fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(application.Manifest)))[..16];
        _dataDirectory = Path.GetFullPath(Option("--data-dir")
            ?? throw new ArgumentException("Pass --data-dir <folder> for WebKit's data and cache (never the user's home)."));

        foreach (var name in application.Frontend.GetManifestResourceNames())
        {
            if (name.StartsWith("spar/", StringComparison.Ordinal))
            {
                using var stream = application.Frontend.GetManifestResourceStream(name)!;
                using var copy = new MemoryStream();
                stream.CopyTo(copy);
                _assets[name[4..].Replace('\\', '/')] = copy.ToArray();
            }
        }
    }

    public Dictionary<string, int> AssetHits { get; } = [];

    public int ExitCode { get; set; }

    /// <summary>Runs the window on its own thread and returns the exit code when the window has closed.</summary>
    public static int Run(DesktopApplication application, string[] args)
    {
        var clock = Stopwatch.StartNew();
        using (var self = Process.GetCurrentProcess())
        {
            Console.WriteLine($"SPAR_MARK process-start->host {(DateTime.Now - self.StartTime).TotalMilliseconds:F1}");
        }

        var exitCode = 0;
        var thread = new Thread(() => exitCode = new LinuxHost(application, args, clock).RunOnUiThread()) { Name = "Spar UI" };
        thread.Start();
        thread.Join();
        return exitCode;
    }

    public void Mark(string name, bool sinceClose = false)
    {
        var ms = sinceClose && _closeRequestedAt != 0
            ? Stopwatch.GetElapsedTime(_closeRequestedAt).TotalMilliseconds
            : _clock.Elapsed.TotalMilliseconds;
        Console.WriteLine($"SPAR_MARK {name}{(sinceClose ? " (since close)" : "")} {ms:F1}");
    }

    private string? Option(string name)
    {
        var index = Array.IndexOf(_args, name);
        return index >= 0 && index + 1 < _args.Length ? _args[index + 1] : null;
    }

    private unsafe int RunOnUiThread()
    {
        s_current = this;
#if SPAR_GTK4
        if (Gtk.gtk_init_check() == 0)
#else
        if (Gtk.gtk_init_check(0, 0) == 0)
#endif
        {
            Console.Error.WriteLine("SPAR_ERROR gtk_init_check failed: no display?");
            return 2;
        }

        Mark("gtk-init");
        _ui = new GLibContext();
        SynchronizationContext.SetSynchronizationContext(_ui);
        var automation = _args.Contains("--automation");

        var dataPath = Path.Combine(_dataDirectory, "data");
        var cachePath = Path.Combine(_dataDirectory, "cache");
#if SPAR_GTK4
        var session = WebKit.webkit_network_session_new(dataPath, cachePath);
        _webContext = WebKit.webkit_web_context_new();
        ConfigureContext(automation);
        _webView = GObject.New(
            WebKit.webkit_web_view_get_type(),
            new("web-context", _webContext, WebKit.webkit_web_context_get_type()),
            new("network-session", session, WebKit.webkit_network_session_get_type()),
            new("is-controlled-by-automation", automation));
        GObject.g_object_unref(session); // The view holds its own reference.
#else
        var manager = GObject.New(
            WebKit.webkit_website_data_manager_get_type(),
            new("base-data-directory", dataPath),
            new("base-cache-directory", cachePath));
        _webContext = WebKit.webkit_web_context_new_with_website_data_manager(manager);
        GObject.g_object_unref(manager); // The context holds its own reference.
        ConfigureContext(automation);
        _webView = GObject.New(
            WebKit.webkit_web_view_get_type(),
            new("web-context", _webContext, WebKit.webkit_web_context_get_type()),
            new("is-controlled-by-automation", automation));
#endif
        Mark("view-created");

        var contentManager = WebKit.webkit_web_view_get_user_content_manager(_webView);
#if SPAR_GTK4
        WebKit.webkit_user_content_manager_register_script_message_handler(contentManager, "spar", null);
#else
        WebKit.webkit_user_content_manager_register_script_message_handler(contentManager, "spar");
#endif
        GObject.Connect(contentManager, "script-message-received::spar", (delegate* unmanaged<nint, nint, nint, void>)&OnScriptMessage);
        var script = WebKit.webkit_user_script_new(Shim, WebKit.InjectTopFrame, WebKit.InjectAtDocumentStart, 0, 0);
        WebKit.webkit_user_content_manager_add_script(contentManager, script);
        WebKit.webkit_user_script_unref(script);
        GObject.Connect(_webView, "load-changed", (delegate* unmanaged<nint, int, nint, void>)&OnLoadChanged);
        GObject.Connect(_webView, "close", (delegate* unmanaged<nint, nint, void>)&OnViewClose);
        GObject.Connect(_webView, "web-process-terminated", (delegate* unmanaged<nint, int, nint, void>)&OnWebProcessTerminated);
        if (_args.Contains("--devtools"))
        {
            WebKit.webkit_settings_set_enable_developer_extras(WebKit.webkit_web_view_get_settings(_webView), 1);
        }

        _window = Gtk.gtk_window_new(
#if !SPAR_GTK4
            0 // GTK_WINDOW_TOPLEVEL
#endif
            );
        Gtk.gtk_window_set_title(_window, _application.Title);
        Gtk.gtk_window_set_default_size(_window, 1024, 768);
#if SPAR_GTK4
        Gtk.gtk_window_set_child(_window, _webView);
        Gtk.gtk_window_present(_window);
#else
        Gtk.gtk_container_add(_window, _webView);
        Gtk.gtk_widget_show_all(_window);
#endif
        GObject.Connect(_window, "destroy", (delegate* unmanaged<nint, nint, void>)&OnWindowDestroyed);
        Mark("window-shown");

        WebKit.webkit_web_view_load_uri(_webView, Origin + "/" + (Option("--page") ?? ""));
        _loop = GLib.g_main_loop_new(0, 0);
#if SPAR_SELFTEST
        if (_args.Contains("--selftest"))
        {
            StartSelfTest();
        }
#endif

        GLib.g_main_loop_run(_loop);
        Mark("main-loop-ended", sinceClose: true);

        // The window (and with it the view) is destroyed. Drop the context, then watch the WebKit children exit.
        // WebKit tears its processes down through IPC handled on the main context, so keep pumping it while waiting.
        GObject.g_object_unref(_webContext);
        _webContext = 0;
        WaitForChildren();
        GLib.g_main_loop_unref(_loop);
        _ui.Dispose();
        return ExitCode;
    }

#if SPAR_SELFTEST
    private void StartSelfTest() => _ui!.Post(static async state => await SelfTest.RunAsync((LinuxHost)state!), this);
#endif

    private unsafe void ConfigureContext(bool automation)
    {
        WebKit.webkit_web_context_register_uri_scheme(_webContext, "spar", &OnSchemeRequest, 0, 0);
        var security = WebKit.webkit_web_context_get_security_manager(_webContext);
        WebKit.webkit_security_manager_register_uri_scheme_as_secure(security, "spar");
        WebKit.webkit_security_manager_register_uri_scheme_as_cors_enabled(security, "spar");
#if !SPAR_GTK4
        // WebKitGTK 4.1 runs web processes unsandboxed unless asked; 6.0 always sandboxes them (bubblewrap).
        if (_args.Contains("--sandbox"))
        {
            WebKit.webkit_web_context_set_sandbox_enabled(_webContext, 1);
        }
#endif

        if (automation)
        {
            WebKit.webkit_web_context_set_automation_allowed(_webContext, 1);
            GObject.Connect(_webContext, "automation-started", (delegate* unmanaged<nint, nint, nint, void>)&OnAutomationStarted);
        }
    }

    public void Close()
    {
        _closeRequestedAt = Stopwatch.GetTimestamp();

        // With the telemetry layer on, the page first flushes its exporters ("z"), for at most 1 s.
        if (!_closeConfirmed && _application.Telemetry is not null && _document is not null && _webView != 0)
        {
            if (!_closeAsked)
            {
                _closeAsked = true;
                Mark("close-asks-page", sinceClose: true);
                Post(_calls.Epoch, "z");
                _ = CloseAfterTimeoutAsync();
            }

            return;
        }

        Gtk.gtk_window_close(_window);
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
        Gtk.gtk_window_close(_window);
    }

    public Task UiReady => _uiReady.Task;

    public Task Loaded => _loaded.Task;

    public nint WebView => _webView;

    public void Navigate(string page)
    {
        _uiReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
        WebKit.webkit_web_view_load_uri(_webView, Origin + "/" + page);
    }

    public void Reload()
    {
        _uiReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
        WebKit.webkit_web_view_reload(_webView);
    }

    // ---- Signals (GTK thread) ----

    [UnmanagedCallersOnly]
    private static void OnWindowDestroyed(nint widget, nint data)
    {
        var host = s_current!;
        host.Mark("window-destroyed", sinceClose: true);

        // Closing: stop admitting work and cancel what runs; late replies are dropped by the epoch check.
        host._calls.NewEpoch();
        host._webView = 0;
        GLib.g_main_loop_quit(host._loop);
    }

    [UnmanagedCallersOnly]
    private static void OnLoadChanged(nint webView, int loadEvent, nint data)
    {
        var host = s_current!;
        switch (loadEvent)
        {
            case WebKit.LoadStarted:
                // A new document is coming: nothing of the old one may keep running or receive replies.
                var cancelled = host._calls.NewEpoch();
                host._document = null;
                host.Mark("load-started" + (cancelled > 0 ? $" cancelled={cancelled}" : ""));
                break;
            case WebKit.LoadCommitted:
                host.Mark("load-committed");
                break;
            case WebKit.LoadFinished:
                host.Mark("load-finished");
                host._loaded.TrySetResult();
                break;
        }
    }

    /// <summary>window.close() from the page, or an automation session closing its window.</summary>
    [UnmanagedCallersOnly]
    private static void OnViewClose(nint webView, nint data)
    {
        Console.WriteLine("SPAR_VIEW_CLOSE");
        s_current!.Close();
    }

    [UnmanagedCallersOnly]
    private static void OnWebProcessTerminated(nint webView, int reason, nint data) =>
        Console.WriteLine($"SPAR_WEB_PROCESS_TERMINATED reason={reason}");

    [UnmanagedCallersOnly]
    private static unsafe void OnAutomationStarted(nint context, nint session, nint data)
    {
        Console.WriteLine("SPAR_AUTOMATION started");
        GObject.Connect(session, "create-web-view", (delegate* unmanaged<nint, nint, nint>)&OnAutomationCreateWebView);
    }

    [UnmanagedCallersOnly]
    private static nint OnAutomationCreateWebView(nint session, nint data)
    {
        Console.WriteLine("SPAR_AUTOMATION create-web-view");
        return s_current!._webView; // (transfer none)
    }

    // ---- The custom scheme: assets, the fetch bridge and streamed responses ----

    [UnmanagedCallersOnly]
    private static void OnSchemeRequest(nint request, nint data)
    {
        try
        {
            s_current!.HandleSchemeRequest(request);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"SPAR_SCHEME_EXCEPTION {exception}");
            Respond(request, 500, "Internal Server Error", "text/plain", "host error"u8);
        }
    }

    private void HandleSchemeRequest(nint request)
    {
        var path = Marshal.PtrToStringUTF8(WebKit.webkit_uri_scheme_request_get_path(request)) ?? "/";
        var method = Marshal.PtrToStringUTF8(WebKit.webkit_uri_scheme_request_get_http_method(request)) ?? "GET";
        var headers = WebKit.webkit_uri_scheme_request_get_http_headers(request);
        if (!path.StartsWith("/_spar/", StringComparison.Ordinal))
        {
            var asset = path == "/" ? "/index.html" : path;
            lock (AssetHits)
            {
                AssetHits[asset] = AssetHits.GetValueOrDefault(asset) + 1;
            }

            if (method != "GET" || !_assets.TryGetValue(asset, out var content))
            {
                Respond(request, 404, "Not Found", "text/plain", "not found"u8);
                return;
            }

            Respond(request, 200, "OK", ContentType(asset), content);
            return;
        }

        string? Header(string name) => Soup.Header(headers, name);
        if (!_loggedFetchHeaders)
        {
            _loggedFetchHeaders = true;
            Console.WriteLine($"SPAR_FETCH_HEADERS method={method} origin={Header("Origin") ?? "(none)"} content-type={Header("Content-Type") ?? "(none)"} x-spar-call={Header("X-Spar-Call") ?? "(none)"}");
        }

        // Fetches from the page carry its origin. No Origin at all is accepted only for navigation-free probes.
        if (method != "POST" || Header("Origin") is { } origin && origin != Origin)
        {
            Respond(request, 403, "Forbidden", "application/json", SparMessage.ErrorJson("forbidden-origin", Header("Origin")));
            return;
        }

        if (path == "/_spar/ready")
        {
            Mark("ui-ready (fetch /_spar/ready)");
            _uiReady.TrySetResult();
            Respond(request, 204, "No Content", "text/plain", []);
            return;
        }

        if (path.StartsWith(SparTelemetryRoutes.Prefix, StringComparison.Ordinal))
        {
            // The page's OTLP exports: relayed unchanged by the optional telemetry layer, 404 without it.
            var export = ReadBody(request);
            GObject.g_object_ref(request);
            _ = CompleteTelemetryAsync(request, path, export, Header("Content-Type"));
            return;
        }

        var parts = path.Split('/');
        if (parts.Length != 5 || parts[2] is not ("call" or "stream"))
        {
            Respond(request, 404, "Not Found", "application/json", SparMessage.ErrorJson("unknown-route", path));
            return;
        }

        if (Header("X-Spar-Contracts") != _fingerprint)
        {
            Respond(request, 409, "Conflict", "application/json", SparMessage.ErrorJson("contract-mismatch", _fingerprint));
            return;
        }

        var body = ReadBody(request);
        var call = _calls.Start(Header("X-Spar-Document") ?? "-", Header("X-Spar-Call") ?? "-", SparTraceContext.FromHeaders(Header("traceparent"), Header("tracestate")));
        if (call is null)
        {
            Respond(request, 400, "Not Executed", "application/json", SparMessage.ErrorJson("cancelled-before-start", null));
            return;
        }

        if (parts[2] == "stream")
        {
            var batch = int.TryParse(Header("X-Spar-Batch"), out var size) ? Math.Clamp(size, 1, 10_000) : 100;
            StartFetchStream(request, call, parts[3], parts[4], body, batch);
            return;
        }

        // The request stays alive until the reply is ready; the reply is finished on the GTK thread.
        GObject.g_object_ref(request);
        _ = CompleteCallAsync(request, call, parts[3], parts[4], body);
    }

    private async Task CompleteTelemetryAsync(nint request, string path, byte[] body, string? contentType)
    {
        var timer = Stopwatch.StartNew();
        var relay = _application.Telemetry;
        var response = await Task.Run(() => SparTelemetryRoutes.HandleAsync(relay, path, body, contentType, CancellationToken.None));
        Console.WriteLine($"SPAR_OTLP {path} {response.Status} in={body.Length}B {timer.Elapsed.TotalMilliseconds:F1}ms");

        // Back on the GTK thread.
        Respond(request, response.Status, response.Status == 200 ? "OK" : "Error", response.ContentType, response.Body.Span);
        GObject.g_object_unref(request);
    }

    private async Task CompleteCallAsync(nint request, SparCall call, string contract, string command, byte[] body)
    {
        var reply = await Task.Run(() => SparDispatch.InvokeAsync(_application.Invoke, call, contract, command, body));
        _calls.End(call);

        // Back on the GTK thread (the await keeps the GLib context).
        var (status, reason) = reply.Kind switch
        {
            'r' => (200, "OK"),
            'c' => (499, "Cancelled"),
            'n' when reply.Field == "unknown-command" => (404, "Not Found"),
            'n' => (400, "Not Executed"),
            _ => (500, "Outcome Unknown"),
        };
        Respond(request, status, reason, "application/json", reply.Json.Span);
        GObject.g_object_unref(request);
    }

    /// <summary>
    /// A streamed response: WebKit reads a pollable pipe (GUnixInputStream) while the command writes NDJSON into it
    /// from a pool thread. The pipe's own buffer gives back-pressure: a full pipe blocks the writer.
    /// </summary>
    private unsafe void StartFetchStream(nint request, SparCall call, string contract, string command, byte[] body, int batch)
    {
        var fds = stackalloc int[2];
        if (LibC.pipe2(fds, LibC.OCloexec) != 0)
        {
            Respond(request, 500, "Outcome Unknown", "application/json", SparMessage.ErrorJson("pipe-failed", Marshal.GetLastPInvokeError().ToString(CultureInfo.InvariantCulture)));
            return;
        }

        var input = Gio.g_unix_input_stream_new(fds[0], 1); // Owns the read end.
        var writer = new FileStream(new SafeFileHandle(fds[1], ownsHandle: true), FileAccess.Write, bufferSize: 0);
        RespondStream(request, input, "application/x-ndjson");
        GObject.g_object_unref(input);

        _ = PumpFetchStreamAsync(writer, call, contract, command, body, batch);
    }

    // Not in an unsafe member: C# 15 rejects await there (CS8652).
    private Task PumpFetchStreamAsync(FileStream writer, SparCall call, string contract, string command, byte[] body, int batch)
    {
        var started = Stopwatch.GetTimestamp();
        return Task.Run(async () =>
        {
            var buffer = new ArrayBufferWriter<byte>(64 * 1024);
            var writes = 0;
            await SparDispatch.PumpAsync(
                token => OpenStream(contract, command, body, token),
                call,
                batch,
                useCredits: false,
                (message, _) =>
                {
                    // Transport writes never take the command's token: a cancelling page keeps reading.
                    buffer.ResetWrittenCount();
                    message.WriteTo(buffer, newline: true);
                    writer.Write(buffer.WrittenSpan);
                    writes++;
                    LogFinal(call, message);
                    return ValueTask.CompletedTask;
                });
            writer.Dispose();
            _calls.End(call);
            Console.WriteLine($"SPAR_FETCH_STREAM id={call.Id} writes={writes} {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1}ms");
        });
    }

    private SparStream OpenStream(string contract, string command, byte[] body, CancellationToken cancellationToken) =>
        _application.OpenStream is { } open
            ? open(contract, command, body, cancellationToken)
            : throw new SparUnknownCommandException("This app has no streaming commands.");

    private static byte[] ReadBody(nint request)
    {
        var stream = WebKit.webkit_uri_scheme_request_get_http_body(request);
        if (stream == 0)
        {
            return [];
        }

        try
        {
            return Gio.ReadAll(stream);
        }
        finally
        {
            GObject.g_object_unref(stream);
        }
    }

    private static void Respond(nint request, int status, string reason, string contentType, ReadOnlySpan<byte> content)
    {
        var stream = Gio.MemoryStream(content);
        Finish(request, stream, content.Length, status, reason, contentType);
        GObject.g_object_unref(stream);
    }

    private static void RespondStream(nint request, nint stream, string contentType) =>
        Finish(request, stream, -1, 200, "OK", contentType);

    private static void Finish(nint request, nint stream, long length, int status, string reason, string contentType)
    {
        var response = WebKit.webkit_uri_scheme_response_new(stream, length);
        WebKit.webkit_uri_scheme_response_set_status(response, (uint)status, reason);
        WebKit.webkit_uri_scheme_response_set_content_type(response, contentType);
        var headers = Soup.soup_message_headers_new(Soup.MessageHeadersResponse);
        Soup.soup_message_headers_append(headers, "Cache-Control", "no-store");
        WebKit.webkit_uri_scheme_response_set_http_headers(response, headers); // Takes the headers.
        WebKit.webkit_uri_scheme_request_finish_with_response(request, response);
        GObject.g_object_unref(response);
    }

    private static string ContentType(string path) => Path.GetExtension(path) switch
    {
        ".html" => "text/html",
        ".js" => "text/javascript",
        ".css" => "text/css",
        ".svg" => "image/svg+xml",
        ".json" => "application/json",
        _ => "application/octet-stream",
    };

    // ---- The message channel: unary calls, streams with credits, and cancellation (protocol in SparMessage) ----

    [UnmanagedCallersOnly]
    private static void OnScriptMessage(nint contentManager, nint value, nint data)
    {
        try
        {
#if !SPAR_GTK4
            value = WebKit.webkit_javascript_result_get_js_value(value);
#endif
            var message = GLib.TakeString(Jsc.jsc_value_to_string(value));
            if (message is not null)
            {
                s_current!.OnMessage(message);
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"SPAR_MESSAGE_EXCEPTION {exception}");
        }
    }

    private void OnMessage(string message)
    {
        // WebKitGTK gives no sender frame or origin with a script message: check the top document instead.
        var uri = Marshal.PtrToStringUTF8(WebKit.webkit_web_view_get_uri(_webView));
        if (uri is null || !uri.StartsWith(Origin + "/", StringComparison.Ordinal))
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
                Mark("hello");
                return;
            case 'x' when fields.Length == 2:
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
            Post(_calls.Epoch, SparMessage.NotExecuted(id, "cancelled-before-start", null).ToString());
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

    private async Task ReplyAsync(SparCall call, string contract, string command, byte[] payload)
    {
        var reply = await Task.Run(() => SparDispatch.InvokeAsync(_application.Invoke, call, contract, command, payload));
        _calls.End(call);
        Send(call, reply);
    }

    private void Send(SparCall call, SparMessage message)
    {
        LogFinal(call, message);
        Post(call.Epoch, message.ToString());
    }

    /// <summary>Queues a message for the page; one evaluate_javascript per GTK-thread turn carries everything queued.</summary>
    private void Post(long epoch, string text)
    {
        lock (_outbox)
        {
            _outbox.Enqueue((epoch, text));
        }

        if (Interlocked.Exchange(ref _flushScheduled, 1) == 0)
        {
            _ui!.Post(static state => ((LinuxHost)state!).Flush(), this);
        }
    }

    public long Evaluations { get; private set; }

    public long Delivered { get; private set; }

    private unsafe void Flush()
    {
        Volatile.Write(ref _flushScheduled, 0);
        var buffer = new ArrayBufferWriter<byte>(4096);
        "window.__sparDeliver("u8.CopyTo(buffer.GetSpan(21));
        buffer.Advance(21);
        var count = 0;
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            lock (_outbox)
            {
                // At most about 1 MB per evaluation; the rest goes in the next turn.
                while (_outbox.TryPeek(out var item) && (count == 0 || writer.BytesPending + writer.BytesCommitted < 1 << 20))
                {
                    _outbox.Dequeue();
                    if (item.Epoch == _calls.Epoch)
                    {
                        writer.WriteStringValue(item.Text);
                        count++;
                    }
                }

                if (_outbox.Count > 0 && Interlocked.Exchange(ref _flushScheduled, 1) == 0)
                {
                    _ui!.Post(static state => ((LinuxHost)state!).Flush(), this);
                }
            }

            writer.WriteEndArray();
        }

        if (count == 0 || _webView == 0)
        {
            return;
        }

        ");"u8.CopyTo(buffer.GetSpan(2));
        buffer.Advance(2);
        Evaluations++;
        Delivered += count;
        fixed (byte* script = buffer.WrittenSpan)
        {
            WebKit.webkit_web_view_evaluate_javascript(_webView, script, buffer.WrittenCount, 0, 0, 0, null, 0);
        }
    }

    private static void LogFinal(SparCall call, SparMessage message)
    {
        if (message.Kind is 'c' or 'u' || (message.Kind is 'n' && message.Field != "contract-mismatch"))
        {
            var latency = call.CancelRequestedAt == 0 ? "" : $" cancel->{message.Outcome}={Stopwatch.GetElapsedTime(call.CancelRequestedAt).TotalMilliseconds:F2}ms";
            Console.WriteLine($"SPAR_CALL_END id={call.Id} {message.Outcome}{latency}");
        }
    }

    // ---- Processes ----

    /// <summary>All descendants of this process: (pid, name).</summary>
    public static List<(int Pid, string Name)> Descendants()
    {
        var parents = new Dictionary<int, (int Parent, string Name)>();
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory), out var pid))
            {
                continue;
            }

            try
            {
                // /proc/<pid>/stat: "pid (comm) state ppid …"; comm may contain spaces, so split after the last ')'.
                var stat = File.ReadAllText(Path.Combine(directory, "stat"));
                var close = stat.LastIndexOf(')');
                var name = stat[(stat.IndexOf('(') + 1)..close];
                var rest = stat[(close + 2)..].Split(' ');
                if (rest[0] != "Z")
                {
                    parents[pid] = (int.Parse(rest[1], CultureInfo.InvariantCulture), name);
                }
            }
            catch (IOException)
            {
                // The process ended while we looked.
            }
        }

        var self = Environment.ProcessId;
        var result = new List<(int, string)>();
        var frontier = new Queue<int>([self]);
        while (frontier.TryDequeue(out var parent))
        {
            foreach (var (pid, (ppid, name)) in parents)
            {
                if (ppid == parent)
                {
                    result.Add((pid, name));
                    frontier.Enqueue(pid);
                }
            }
        }

        return result;
    }

    /// <summary>Resident set and proportional set size (shared pages divided among sharers), in MB.</summary>
    public static (double RssMB, double PssMB) Memory(int pid)
    {
        double Read(string file, string key)
        {
            try
            {
                foreach (var line in File.ReadLines($"/proc/{pid}/{file}"))
                {
                    if (line.StartsWith(key, StringComparison.Ordinal))
                    {
                        return double.Parse(line[key.Length..].Trim().Split(' ')[0], CultureInfo.InvariantCulture) / 1024;
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            return 0;
        }

        return (Read("status", "VmRSS:"), Read("smaps_rollup", "Pss:"));
    }

    private void WaitForChildren()
    {
        var started = Stopwatch.GetTimestamp();
        var seen = Descendants();
        Console.WriteLine($"SPAR_CHILDREN at-loop-end: {string.Join(", ", seen.Select(c => $"{c.Name}({c.Pid})"))}");
        while (Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(10))
        {
            var alive = Descendants();
            if (alive.Count == 0)
            {
                Mark("children-exited", sinceClose: true);
                return;
            }

            while (GLib.g_main_context_iteration(0, 0) != 0)
            {
            }

            Thread.Sleep(1);
        }

        Console.WriteLine($"SPAR_CHILDREN still alive after 10 s: {string.Join(", ", Descendants().Select(c => $"{c.Name}({c.Pid})"))}");
    }
}
