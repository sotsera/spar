using System.Runtime.InteropServices;
using Spar.DevServer;
using Spar.Hosting;
using Spar.Linux.Interop;
using Spar.Runtime;
using Spar.Telemetry;

namespace Spar.Linux;

/// <summary>
/// Serves the app's packed page on <c>spar://app</c> through WebKit's URI scheme handler (plan section 3, "Assets and
/// the bridge"; decision "Desktop origin": WebKitGTK cannot intercept <c>https</c>). Every request of the scheme is
/// answered: what the origin answers is the shared <see cref="PageResponder"/>'s, another host on the scheme gets 403,
/// and nothing is ever a redirect, which WebKit would show as the page. Every response carries <c>Content-Type</c> both
/// as the response's type and inside its header set (WebKit otherwise blocks the app's own CSS, scripts and workers), and
/// the app's <see cref="PagePolicy"/> headers. It runs on the main loop and answers from memory: a file stored in the
/// executable is handed to WebKit in place (<c>g_bytes_new_static</c>), anything else as a copy; a request that comes
/// before the page is prepared is held (a reference on it) and answered when it is. A violation report posted to
/// <c>report-uri</c> is answered 204 at once; within the rate limit its body is read without blocking the loop, at most
/// <see cref="CspViolationLog.MaxReportBytes"/>, and handed to the <see cref="CspViolationLog"/>, as on Windows. A Debug
/// build that serves its page from the Vite development server (plan section 4) answers every other request through
/// <see cref="DevServerProxy"/>: the headers it forwards are read on the loop, the request is held, the fetch runs on the
/// thread pool, and the response is finished back on the loop, so a slow server holds the request and never the loop.
/// </summary>
internal sealed class SchemeServer
{
    public const string Scheme = "spar";

    /// <summary>
    /// The app's origin on Linux (decision "Desktop origin"); <c>connect-src</c> names it (decision "M1c's first
    /// questions", checked in <c>docs/evidence/linux-gpu-csp</c>).
    /// </summary>
    public const string Origin = "spar://app";

    private readonly PageResponder _responder;
    private readonly MainLoop _ui;
    private readonly HostLog _log;
    private readonly CspViolationLog? _violations;
    private readonly ReportLimiter _reports = new();
    private GCHandle _self;
    private DevServerProxy? _devServer;
    private TelemetryRoute _telemetry = TelemetryRoute.None;
    private Task<SparRunningApp>? _app;

    /// <param name="development">A Development or Test build, where <c>CspReports.DevelopmentOnly</c> reports.</param>
    /// <param name="violations">Where reports go; null when the endpoint does not report in this build.</param>
    /// <param name="routes">The window kinds' routes, answered with the frontend's page.</param>
    public SchemeServer(
        SparFrontend? frontend, MainLoop ui, HostLog log, SparPageSecurity security, bool development, CspViolationLog? violations,
        IReadOnlyList<string>? routes = null)
    {
        _ui = ui;
        _log = log;
        _violations = violations;
        _responder = new PageResponder(frontend, security, Origin, development) { Routes = routes ?? [] };

        // A Debug build without an embedded page serves it from the development server (UseDevServer).
        if (frontend is null && !DevServerSettings.IsSupported)
        {
            log.Error(PageResponder.NoPage);
        }
    }

    /// <summary>The page's first address.</summary>
    public static string StartUrl => Origin + "/";

    /// <summary>
    /// A Debug build's development server (loop thread, before the first load): every request but the violation
    /// reports goes to it. Called only behind <see cref="DevServerSettings.IsSupported"/>, so no other build has it, and
    /// no signature a Production build keeps names its types.
    /// </summary>
    public void UseDevServer(DevServerSession session, SparPageSecurity security) =>
        _devServer = new DevServerProxy(session, security, Origin, _log);

    /// <summary>The app's telemetry route (loop thread, before the first load): its relay when the layer is in the app.</summary>
    public void UseTelemetry(TelemetryRoute route) => _telemetry = route;

    /// <summary>
    /// The app whose blob handles and resources the origin serves (<c>/_spar/blob/</c>, <c>/_spar/resource/</c>), set on
    /// the loop thread before the first load; its loads wait for the app's services, on the thread pool.
    /// </summary>
    public void UseBlobs(Task<SparRunningApp> app) => _app = app;

    /// <summary>The policy every response carries, once the page is prepared; the host logs it at start.</summary>
    public Task<PagePolicy> Policy => _responder.Policy;

    /// <summary>Whether an address belongs to the app's origin (WebKit hands canonical URLs: lower-case scheme and host).</summary>
    public static bool IsAppOrigin(string address) => PageResponder.IsOnOrigin(address, Origin);

    /// <summary>
    /// Registers the scheme on <paramref name="context"/> (loop thread), as secure (a secure context, like
    /// <c>https</c>) and CORS-enabled (module scripts and workers are fetched in CORS mode). The registration lasts as
    /// long as the context, the process's default one, so this server stays reachable until <see cref="Release"/>.
    /// </summary>
    public unsafe void Register(nint context)
    {
        _self = GCHandle.Alloc(this);
        WebKit.webkit_web_context_register_uri_scheme(context, Scheme, &OnRequest, GCHandle.ToIntPtr(_self), 0);
        var security = WebKit.webkit_web_context_get_security_manager(context);
        WebKit.webkit_security_manager_register_uri_scheme_as_secure(security, Scheme);
        WebKit.webkit_security_manager_register_uri_scheme_as_cors_enabled(security, Scheme);
    }

    /// <summary>Frees the handle the scheme's callback finds this server by, once the main loop has ended.</summary>
    public void Release()
    {
        if (_self.IsAllocated)
        {
            _self.Free();
        }
    }

    /// <summary>The headers of one response, in the order they are sent: <c>Content-Type</c>, the policy, then the shared ones.</summary>
    internal static IEnumerable<KeyValuePair<string, string>> HeadersOf(PageResponse response, PageResponder responder)
    {
        yield return new("Content-Type", response.ContentType);
        yield return new("Content-Security-Policy", response.Csp ?? responder.CurrentPolicy.ContentSecurityPolicy);
        foreach (var header in responder.SharedHeaders)
        {
            yield return header;
        }

        if (response.Allow is { } allow)
        {
            yield return new("Allow", allow);
        }

        if (response.Disposition is { } disposition)
        {
            yield return new("Content-Disposition", disposition);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnRequest(nint request, nint data)
    {
        if (GCHandle.FromIntPtr(data).Target is not SchemeServer server)
        {
            return;
        }

        using var scope = server._ui.Enter("scheme request");
        try
        {
            server.Answer(request);
        }
        catch (Exception exception)
        {
            server._log.Error("A request to the app's origin could not be answered", exception);
        }
    }

    /// <summary>Answers one request (loop thread); <paramref name="request"/> is borrowed.</summary>
    private void Answer(nint request)
    {
        var uri = Marshal.PtrToStringUTF8(WebKit.webkit_uri_scheme_request_get_uri(request)) ?? "";
        var method = Marshal.PtrToStringUTF8(WebKit.webkit_uri_scheme_request_get_http_method(request)) ?? "GET";
        if (!IsAppOrigin(uri))
        {
            Respond(request, PageResponder.Refusal(403, "Forbidden", "forbidden-origin"));
            _log.Warn($"403 for an address of the scheme outside the app's origin: {Shorten(uri)}");
            return;
        }

        var path = PageResponder.PathOf(uri, Origin);
        if (TelemetryRoute.Matches(path))
        {
            AnswerTelemetry(request, path, method);
            return;
        }

        // Before the development server: a blob is the backend's, never Vite's.
        if (BlobRoutes.Matches(path))
        {
            AnswerBlob(request, path, QueryOf(uri), method);
            return;
        }

        if (method == "POST" && PageResponder.PathOf(uri, Origin) == PagePolicy.ReportPath)
        {
            ReadReport(request);
            Respond(request, NoContent());
            return;
        }

        if (DevServerSettings.IsSupported && _devServer is { } proxy)
        {
            Proxy(request, proxy, uri, method);
            return;
        }

        if (_responder.IsPrepared)
        {
            Respond(request, Response(uri, method));
            return;
        }

        // The page is still being prepared: answer when it is, never decode on this thread. The request is kept alive.
        var held = GLib.g_object_ref(request);
        _ = _responder.Policy.ContinueWith(_ => _ui.Post(() => AnswerLater(held, uri, method), "scheme request"), TaskScheduler.Default);
    }

    private void AnswerLater(nint request, string uri, string method)
    {
        try
        {
            Respond(request, Response(uri, method));
        }
        catch (Exception exception)
        {
            _log.Error($"A held response for {Shorten(uri)} failed", exception);
        }
        finally
        {
            GLib.g_object_unref(request);
        }
    }

    /// <summary>
    /// A Debug build's request for the development server (loop thread): the forwarded headers read now, the request
    /// held, the answer fetched on the thread pool and the request finished back on the loop.
    /// </summary>
    private void Proxy(nint request, DevServerProxy proxy, string uri, string method)
    {
        var headers = new List<KeyValuePair<string, string>>(DevServerProxy.ForwardedHeaders.Count);
        var soup = WebKit.webkit_uri_scheme_request_get_http_headers(request);
        if (soup != 0)
        {
            foreach (var name in DevServerProxy.ForwardedHeaders)
            {
                if (Marshal.PtrToStringUTF8(WebKit.soup_message_headers_get_one(soup, name)) is { } value)
                {
                    headers.Add(new(name, value));
                }
            }
        }

        var fragment = uri.IndexOf('#', StringComparison.Ordinal);
        var pathAndQuery = fragment < 0 ? uri[Origin.Length..] : uri[Origin.Length..fragment];
        var held = GLib.g_object_ref(request);
        _ = Task.Run(() => proxy.AnswerAsync(pathAndQuery, method, headers))
            .ContinueWith(answer => _ui.Post(() => Proxied(held, uri, answer), "scheme request"), TaskScheduler.Default);
    }

    private void Proxied(nint request, string uri, Task<PageResponse> answer)
    {
        try
        {
            if (!answer.IsCompletedSuccessfully)
            {
                _log.Error($"The development server's answer for {Shorten(uri)} failed", answer.Exception?.GetBaseException());
            }

            Respond(request, answer.IsCompletedSuccessfully ? answer.Result : PageResponder.Refusal(502, "Bad Gateway", "dev-server-failed"));
        }
        catch (Exception exception)
        {
            _log.Error($"A development server response for {Shorten(uri)} failed", exception);
        }
        finally
        {
            GLib.g_object_unref(request);
        }
    }

    private static PageResponse NoContent() => new(204, "No Content", "text/plain; charset=utf-8", ReadOnlyMemory<byte>.Empty);

    [UnmanagedCallersOnly]
    private static unsafe void OnReportRead(nint stream, nint result, nint data)
    {
        var handle = GCHandle.FromIntPtr(data);
        var read = (ReportRead)handle.Target!;
        handle.Free();
        using var scope = read.Server._ui.Enter("report read");
        try
        {
            nuint count = 0;
            nint error = 0;
            var ok = GLib.g_input_stream_read_all_finish(stream, result, &count, &error) != 0;
            if (error != 0)
            {
                GLib.g_error_free(error);
            }

            // One byte more than the limit was asked for: a body that fills it is too large, and dropped unread.
            if (ok && count > 0 && count <= CspViolationLog.MaxReportBytes)
            {
                read.Server._violations?.PostReport(new ReadOnlySpan<byte>(read.Buffer, (int)count).ToArray());
            }
            else
            {
                read.Server._violations?.NoteRefused();
            }
        }
        catch (Exception exception)
        {
            read.Server._log.Error("A violation report could not be read", exception);
        }
        finally
        {
            NativeMemory.Free(read.Buffer);
            GLib.g_object_unref(stream);
        }
    }

    /// <summary>
    /// A violation report the browser posted to <c>report-uri</c> (loop thread): past the rate limit, without a body, or
    /// with the endpoint off in this build, it is dropped unread; otherwise its body is read asynchronously into a
    /// buffer of its own, one byte more than the limit, and parsed and logged off the loop.
    /// </summary>
    private unsafe void ReadReport(nint request)
    {
        if (_violations is null)
        {
            return;
        }

        if (!_reports.TryTake(System.Diagnostics.Stopwatch.GetTimestamp()))
        {
            _violations.NoteRefused();
            return;
        }

        var body = WebKit.webkit_uri_scheme_request_get_http_body(request);
        if (body == 0)
        {
            _violations.NoteRefused();
            return;
        }

        const int Asked = CspViolationLog.MaxReportBytes + 1;
        var read = new ReportRead(this, (byte*)NativeMemory.Alloc(Asked));
        GLib.g_input_stream_read_all_async(
            body, read.Buffer, Asked, GLib.PriorityDefault, 0, &OnReportRead, GCHandle.ToIntPtr(GCHandle.Alloc(read)));
    }

    /// <summary>
    /// A request to the telemetry routes (<see cref="TelemetryRoute"/>, loop thread): the checks and the configuration are
    /// answered at once; a traces request is held, its body read asynchronously (the route's limit and one byte), the
    /// forward run on the thread pool and the request finished back on the loop.
    /// </summary>
    private unsafe void AnswerTelemetry(nint request, string path, string method)
    {
        var soup = WebKit.webkit_uri_scheme_request_get_http_headers(request);
        var contentType = soup == 0 ? "" : Marshal.PtrToStringUTF8(WebKit.soup_message_headers_get_one(soup, "Content-Type")) ?? "";
        if (_telemetry.Check(path, method, contentType) is { } answer)
        {
            Respond(request, answer);
            return;
        }

        var body = WebKit.webkit_uri_scheme_request_get_http_body(request);
        if (body == 0)
        {
            Respond(request, PageResponder.Refusal(400, "Bad Request", "telemetry-without-body"));
            return;
        }

        const int Asked = TelemetryRoute.MaxBody + 1;
        var read = new TelemetryRead(this, (byte*)NativeMemory.Alloc(Asked), GLib.g_object_ref(request), contentType, _telemetry);
        GLib.g_input_stream_read_all_async(
            body, read.Buffer, Asked, GLib.PriorityDefault, 0, &OnTelemetryRead, GCHandle.ToIntPtr(GCHandle.Alloc(read)));
    }

    [UnmanagedCallersOnly]
    private static unsafe void OnTelemetryRead(nint stream, nint result, nint data)
    {
        var handle = GCHandle.FromIntPtr(data);
        var read = (TelemetryRead)handle.Target!;
        handle.Free();
        using var scope = read.Server._ui.Enter("telemetry read");
        byte[] body = [];
        try
        {
            nuint count = 0;
            nint error = 0;
            var ok = GLib.g_input_stream_read_all_finish(stream, result, &count, &error) != 0;
            if (error != 0)
            {
                GLib.g_error_free(error);
            }

            body = ok ? new ReadOnlySpan<byte>(read.Buffer, (int)count).ToArray() : [];
        }
        finally
        {
            NativeMemory.Free(read.Buffer);
            GLib.g_object_unref(stream);
        }

        var server = read.Server;
        _ = Task.Run(() => read.Route.ForwardAsync(body, read.ContentType, CancellationToken.None))
            .ContinueWith(forwarded => server._ui.Post(() => server.Forwarded(read.Request, forwarded), "telemetry forwarded"), TaskScheduler.Default);
    }

    /// <summary>
    /// A blob handle or a resource: the request held, the answer made on the thread pool (a resource runs its command, a
    /// file is read), and the request finished back on the loop.
    /// </summary>
    private void AnswerBlob(nint request, string path, string query, string method)
    {
        if (_app is not { } app)
        {
            Respond(request, PageResponder.Refusal(404, "Not Found", "not-found"));
            return;
        }

        var held = GLib.g_object_ref(request);
        _ = Task.Run(async () => await BlobRoutes.AnswerAsync((await app).Dispatcher, method, path, query, null, CancellationToken.None))
            .ContinueWith(answered => _ui.Post(() => BlobAnswered(held, answered, path), "blob answered"), TaskScheduler.Default);
    }

    private void BlobAnswered(nint request, Task<PageResponse> answered, string path)
    {
        try
        {
            if (!answered.IsCompletedSuccessfully)
            {
                _log.Warn($"{Shorten(path)} could not be served: {answered.Exception?.GetBaseException().Message}");
            }

            Respond(request, answered.IsCompletedSuccessfully ? answered.Result : PageResponder.Refusal(500, "Internal Server Error", "failed"));
        }
        catch (Exception exception)
        {
            _log.Info($"A blob response was not delivered: {exception.Message}");
        }
        finally
        {
            GLib.g_object_unref(request);
        }
    }

    /// <summary>The query of an address, without its <c>?</c> and fragment; empty when it has none.</summary>
    private static string QueryOf(string address)
    {
        var start = address.IndexOf('?');
        if (start < 0)
        {
            return "";
        }

        var end = address.IndexOf('#', start);
        return end < 0 ? address[(start + 1)..] : address[(start + 1)..end];
    }

    private void Forwarded(nint request, Task<PageResponse> forwarded)
    {
        try
        {
            if (!forwarded.IsCompletedSuccessfully)
            {
                _log.Warn($"The page's telemetry could not be forwarded: {forwarded.Exception?.GetBaseException().Message}");
            }

            Respond(request, forwarded.IsCompletedSuccessfully ? forwarded.Result : PageResponder.Refusal(502, "Bad Gateway", "telemetry-failed"));
        }
        catch (Exception exception)
        {
            _log.Info($"A telemetry response was not delivered: {exception.Message}");
        }
        finally
        {
            GLib.g_object_unref(request);
        }
    }

    private PageResponse Response(string uri, string method) =>
        _responder.Answer(PageResponder.PathOf(uri, Origin), method, notFound => _log.Info($"404 for {Shorten(notFound)}"));

    /// <summary>
    /// Finishes the request with the response: its body as <c>GBytes</c> (in place when it lives in the executable's
    /// image, which outlives every reader; otherwise copied), the status, the type and the headers.
    /// </summary>
    private unsafe void Respond(nint request, PageResponse response)
    {
        nint bytes;
        using (var pinned = response.Body.Pin())
        {
            var length = (nuint)response.Body.Length;
            bytes = SparFrontend.IsImageMemory(response.Body)
                ? GLib.g_bytes_new_static(pinned.Pointer, length)
                : GLib.g_bytes_new(pinned.Pointer, length);
        }

        var stream = GLib.g_memory_input_stream_new_from_bytes(bytes);
        GLib.g_bytes_unref(bytes);
        var answer = WebKit.webkit_uri_scheme_response_new(stream, response.Body.Length);
        GLib.g_object_unref(stream);
        try
        {
            WebKit.webkit_uri_scheme_response_set_status(answer, (uint)response.Status, response.Reason);
            WebKit.webkit_uri_scheme_response_set_content_type(answer, response.ContentType);
            var headers = WebKit.soup_message_headers_new(WebKit.SoupHeadersResponse);
            foreach (var (name, value) in HeadersOf(response, _responder))
            {
                WebKit.soup_message_headers_append(headers, name, value);
            }

            WebKit.webkit_uri_scheme_response_set_http_headers(answer, headers);
            WebKit.webkit_uri_scheme_request_finish_with_response(request, answer);
        }
        finally
        {
            GLib.g_object_unref(answer);
        }
    }

    private static string Shorten(string text) => text.Length <= 200 ? text : string.Concat(text.AsSpan(0, 200), "…");

    /// <summary>One report being read: its native buffer, which the read writes into until its callback runs.</summary>
    private sealed unsafe class ReportRead(SchemeServer server, byte* buffer)
    {
        public SchemeServer Server { get; } = server;

        public byte* Buffer { get; } = buffer;
    }

    /// <summary>One telemetry request being read: its buffer, the held request, its type and the route that forwards it.</summary>
    private sealed unsafe class TelemetryRead(SchemeServer server, byte* buffer, nint request, string contentType, TelemetryRoute route)
    {
        public SchemeServer Server { get; } = server;

        public byte* Buffer { get; } = buffer;

        public nint Request { get; } = request;

        public string ContentType { get; } = contentType;

        public TelemetryRoute Route { get; } = route;
    }
}
