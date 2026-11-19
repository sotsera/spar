using System.Runtime.InteropServices;
using Spar.Hosting;
using Spar.Linux.Interop;
using Spar.Runtime;

namespace Spar.Linux;

/// <summary>
/// Serves the app's packed page on <c>spar://app</c> through WebKit's URI scheme handler (plan section 3, "Assets and
/// the bridge"; decision "Desktop origin": WebKitGTK cannot intercept <c>https</c>). Every request of the scheme is
/// answered: what the origin answers is the shared <see cref="PageResponder"/>'s, another host on the scheme gets 403,
/// and nothing is ever a redirect, which WebKit would show as the page. Every response carries <c>Content-Type</c> both
/// as the response's type and inside its header set (WebKit otherwise blocks the app's own CSS, scripts and workers), and
/// the app's <see cref="PagePolicy"/> headers. It runs on the main loop and answers from memory: a file stored in the
/// executable is handed to WebKit in place (<c>g_bytes_new_static</c>), anything else as a copy; a request that comes
/// before the page is prepared is held (a reference on it) and answered when it is. Violation reports to
/// <c>report-uri</c> are answered 204 and not read yet (M1c step 4).
/// </summary>
internal sealed class SchemeServer
{
    public const string Scheme = "spar";

    /// <summary>
    /// The app's origin on Linux (decision "Desktop origin"); <c>connect-src</c> names it (decision "M1c's first
    /// questions", checked in <c>spikes/linux-gpu-csp</c>).
    /// </summary>
    public const string Origin = "spar://app";

    private readonly PageResponder _responder;
    private readonly MainLoop _ui;
    private readonly HostLog _log;
    private GCHandle _self;

    public SchemeServer(SparFrontend? frontend, MainLoop ui, HostLog log, SparPageSecurity security, bool development)
    {
        _ui = ui;
        _log = log;
        _responder = new PageResponder(frontend, security, Origin, development);
        if (frontend is null)
        {
            log.Error(PageResponder.NoPage);
        }
    }

    /// <summary>The page's first address.</summary>
    public static string StartUrl => Origin + "/";

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

    private PageResponse Response(string uri, string method)
    {
        var path = PageResponder.PathOf(uri, Origin);
        if (method == "POST" && path == PagePolicy.ReportPath)
        {
            return new PageResponse(204, "No Content", "text/plain; charset=utf-8", ReadOnlyMemory<byte>.Empty);
        }

        return _responder.Answer(path, method, notFound => _log.Info($"404 for {Shorten(notFound)}"));
    }

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
}
