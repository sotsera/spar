using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;
using Spar.Hosting;
using Spar.Runtime;
using Spar.Windows.Interop;

namespace Spar.Windows;

/// <summary>
/// Serves the app's packed page on <c>https://app.localhost</c> through <c>WebResourceRequested</c> (plan section 3,
/// "Assets and the bridge"). The source-kinds filter (<c>ICoreWebView2_22</c>) sees documents and workers alike; every
/// request to the origin is answered, unknown paths with 404, and a second filter answers every foreign document with
/// 403, so a navigation never reaches the network (section 3b). Nothing is ever a redirect. Every response, worker
/// scripts included, carries the app's <see cref="PagePolicy"/> headers and an exact <c>Content-Type</c>; an HTML
/// document gets its fresh style nonce when the app asked for one. Violation reports posted to <c>report-uri</c> are
/// copied out, within a size and rate limit, and handed to the <see cref="CspViolationLog"/>. It runs on the UI thread
/// and answers from memory: compressed files are decoded, and the policy composed with the page's hashes, on a
/// background thread at start, and a request that arrives before that has finished takes a deferral and is answered
/// when it has.
/// </summary>
internal sealed class PageServer
{
    public const string Origin = "https://app.localhost";

    private const string NoPage =
        "This build of the app embeds no page. Debug builds serve it from the development server, which arrives with the " +
        "development loop; until then build with -p:SparDevServer=false, or run a Release build.";

    /// <summary>The largest <c>report-uri</c> body read; a larger one is answered 204 and dropped unread.</summary>
    public const int MaxReportBytes = 16 * 1024;

    private readonly SparFrontend? _frontend;
    private readonly Task<Served> _decoded;
    private readonly UiThread _ui;
    private readonly HostLog _log;
    private readonly CspViolationLog? _violations;
    private readonly ReportLimiter _reports = new();
    private readonly Served _fallback;
    private Served? _served;

    /// <param name="security">The app's page security, from the generated catalog.</param>
    /// <param name="development">A Development or Test build, where <c>CspReports.DevelopmentOnly</c> reports.</param>
    /// <param name="violations">Where reports go; null when the endpoint does not report in this build.</param>
    public PageServer(
        SparFrontend? frontend,
        UiThread ui,
        HostLog log,
        SparPageSecurity security,
        bool development,
        CspViolationLog? violations)
    {
        _frontend = frontend;
        _ui = ui;
        _log = log;
        _violations = violations;
        _decoded = Task.Run(() => Prepare(frontend, security, development));

        // For the 403 of a foreign document that comes before the page is prepared: the policy without the page hashes.
        _fallback = Prepare(null, security, development);
        if (frontend is null)
        {
            log.Error(NoPage);
        }
    }

    /// <summary>The policy every response carries, once the page is decoded; the host logs it at start.</summary>
    public Task<PagePolicy> Policy => _decoded.ContinueWith(static task => task.Result.Policy, TaskScheduler.Default);

    /// <summary>The page's first address.</summary>
    public static string StartUrl => Origin + "/";

    /// <summary>
    /// Whether an address belongs to the app's origin. WebView2 hands the host canonical URLs (lower-case scheme and
    /// host, no default port), so the origin is a prefix ending at the path's slash; anything else, another port
    /// included, is foreign. <see cref="Uri"/> is not used: its flag names would trip the production boundary scan.
    /// </summary>
    public static bool IsAppOrigin(string address) =>
        address.StartsWith(Origin, StringComparison.Ordinal)
        && address.Length > Origin.Length
        && address[Origin.Length] == '/';

    /// <summary>The decoded path of an address on the app's origin, without its query and fragment.</summary>
    internal static string PathOf(string address)
    {
        var path = address.AsSpan(Origin.Length);
        var end = path.IndexOfAny('?', '#');
        return PercentDecode(end < 0 ? path : path[..end]);
    }

    /// <summary>
    /// Decodes <c>%XX</c> escapes as UTF-8; a malformed escape stays as it is, and finds no file. WebView2 escapes
    /// everything outside ASCII, so the other characters are ASCII.
    /// </summary>
    private static string PercentDecode(ReadOnlySpan<char> path)
    {
        if (!path.Contains('%'))
        {
            return path.ToString();
        }

        var bytes = new List<byte>(path.Length);
        for (var i = 0; i < path.Length; i++)
        {
            var escaped = path[i] == '%' && i + 2 < path.Length;
            if (escaped && byte.TryParse(path.Slice(i + 1, 2), System.Globalization.NumberStyles.HexNumber, null, out var value))
            {
                bytes.Add(value);
                i += 2;
                continue;
            }

            bytes.Add((byte)path[i]);
        }

        return Encoding.UTF8.GetString([.. bytes]);
    }

    /// <summary>The two filters: everything on the app's origin, from documents and workers; and every document elsewhere.</summary>
    public static void AddFilters(ICoreWebView2_22 webView)
    {
        webView.AddWebResourceRequestedFilterWithRequestSourceKinds(Origin + "/*", WebView2Values.ContextAll, WebView2Values.SourceKindsAll);
        webView.AddWebResourceRequestedFilterWithRequestSourceKinds("*", WebView2Values.ContextDocument, WebView2Values.SourceKindsAll);
    }

    /// <summary>Answers one request; <paramref name="argsPointer"/> is the event's borrowed args.</summary>
    public void OnRequested(ICoreWebView2Environment10 environment, nint argsPointer)
    {
        var args = Com.Borrow<ICoreWebView2WebResourceRequestedEventArgs2>(argsPointer);
        var deferred = false;
        try
        {
            var (uri, method) = ReadRequest(args);
            if (!IsAppOrigin(uri))
            {
                Respond(environment, args, Refusal(403, "Forbidden", "forbidden-origin"));
                _log.Warn($"403 for a document outside the app's origin: {Shorten(uri)}");
                return;
            }

            if (_decoded.IsCompletedSuccessfully)
            {
                var report = method == "POST" && PathOf(uri) == PagePolicy.ReportPath;
                Respond(environment, args, report ? Report(args) : Answer(uri, method));
                return;
            }

            // The compressed files are still being decoded: answer when they are, never decode on this thread.
            var deferral = Com.Take<ICoreWebView2Deferral>(args.GetDeferral());
            deferred = true;
            _ = _decoded.ContinueWith(_ => _ui.Post(() => AnswerLater(environment, args, deferral, uri, method)), TaskScheduler.Default);
        }
        finally
        {
            if (!deferred)
            {
                Com.Release(args);
            }
        }
    }

    private void AnswerLater(ICoreWebView2Environment10 environment, ICoreWebView2WebResourceRequestedEventArgs2 args, ICoreWebView2Deferral deferral, string uri, string method)
    {
        try
        {
            Respond(environment, args, Answer(uri, method));
            deferral.Complete();
        }
        catch (Exception exception)
        {
            _log.Error($"A deferred response for {Shorten(uri)} failed", exception);
        }
        finally
        {
            Com.Release(deferral);
            Com.Release(args);
        }
    }

    private static (string Uri, string Method) ReadRequest(ICoreWebView2WebResourceRequestedEventArgs2 args)
    {
        var request = Com.Take<ICoreWebView2WebResourceRequest>(args.GetRequest());
        try
        {
            return (request.GetUri(), request.GetMethod());
        }
        finally
        {
            Com.Release(request);
        }
    }

    /// <summary>
    /// What the page and its decoded files need, prepared once off the UI thread: every compressed file decoded, the
    /// policy composed with the page's import-map hashes, and the headers every response shares.
    /// </summary>
    private static Served Prepare(SparFrontend? frontend, SparPageSecurity security, bool development)
    {
        frontend?.DecodeAll();
        var hashes = frontend is null ? [] : PageHtml.ImportMapHashes(frontend);
        var policy = new PagePolicy(security, Origin, development, hashes);
        var shared = string.Join("\r\n", policy.Headers().Skip(1).Select(header => $"{header.Key}: {header.Value}"));
        return new Served(policy, shared);
    }

    /// <summary>
    /// A violation report the browser posted to <c>report-uri</c>: past the rate or size limit, or with the endpoint off
    /// in this build, it is answered 204 and dropped unread; otherwise its body is copied out (a memory read) and parsed
    /// and logged off this thread.
    /// </summary>
    private Response Report(ICoreWebView2WebResourceRequestedEventArgs2 args)
    {
        if (_violations is null)
        {
            return new Response(204, "No Content", "text/plain; charset=utf-8", ReadOnlyMemory<byte>.Empty, "");
        }

        if (!_reports.TryTake(Stopwatch.GetTimestamp()))
        {
            _violations.NoteRefused();
        }
        else
        {
            var request = Com.Take<ICoreWebView2WebResourceRequest>(args.GetRequest());
            try
            {
                if (RequestBody.Read(request.GetContent(), MaxReportBytes) is { Length: > 0 } body)
                {
                    _violations.PostReport(body);
                }
                else
                {
                    _violations.NoteRefused();
                }
            }
            finally
            {
                Com.Release(request);
            }
        }

        return new Response(204, "No Content", "text/plain; charset=utf-8", ReadOnlyMemory<byte>.Empty, "");
    }

    /// <summary>What the app's origin answers for one address and method.</summary>
    private Response Answer(string address, string method)
    {
        var path = PathOf(address);
        if (method != "GET" && method != "HEAD")
        {
            return Refusal(405, "Method Not Allowed", "method-not-allowed") with { Extra = "\r\nAllow: GET, HEAD" };
        }

        if (_frontend is null)
        {
            return new Response(404, "Not Found", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(NoPage), "");
        }

        var relative = path == "/" ? "index.html" : path.TrimStart('/');
        if (!_frontend.TryGet(relative, out var content, out var contentType))
        {
            _log.Info($"404 for {Shorten(path)}");
            return Refusal(404, "Not Found", "not-found");
        }

        if (Prepared.Policy.StyleNonce && contentType.StartsWith("text/html", StringComparison.Ordinal))
        {
            return Document(content, contentType, method);
        }

        return new Response(200, "OK", contentType, method == "HEAD" ? ReadOnlyMemory<byte>.Empty : content, "");
    }

    /// <summary>
    /// An HTML document with its fresh style nonce (<c>StyleNonce()</c>): in a meta element after <c>&lt;head&gt;</c> and
    /// in the document's policy. The copy is of a small file, and needs no parsing beyond finding that tag.
    /// </summary>
    private Response Document(ReadOnlyMemory<byte> content, string contentType, string method)
    {
        var nonce = PageHtml.NewNonce();
        var policy = Prepared.Policy.ContentSecurityPolicyWithNonce(nonce);
        if (method == "HEAD")
        {
            return new Response(200, "OK", contentType, ReadOnlyMemory<byte>.Empty, "") { Csp = policy };
        }

        var html = content.Span;
        var at = PageHtml.NonceInsertionPoint(html);
        byte[] document = [.. html[..at], .. PageHtml.NonceMeta(nonce), .. html[at..]];
        return new Response(200, "OK", contentType, document, "") { Csp = policy };
    }

    private static Response Refusal(int status, string reason, string code) =>
        new(status, reason, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(code), "");

    /// <summary>
    /// Puts the response on the request. <c>SHCreateMemStream</c> copies the bytes, and WebView2 reads the stream whole on
    /// this thread (pitfalls, "<c>WebResourceRequested</c> cannot stream"), so the image's memory is only read here.
    /// </summary>
    private unsafe void Respond(ICoreWebView2Environment10 environment, ICoreWebView2WebResourceRequestedEventArgs2 args, Response response)
    {
        nint stream;
        fixed (byte* bytes = response.Body.Span)
        {
            stream = Native.CreateMemoryStream(bytes, (uint)response.Body.Length);
        }

        if (stream == 0)
        {
            throw new OutOfMemoryException("SHCreateMemStream failed.");
        }

        try
        {
            var prepared = Prepared;
            var csp = response.Csp ?? prepared.Policy.ContentSecurityPolicy;
            var headers = $"Content-Type: {response.ContentType}\r\nContent-Security-Policy: {csp}\r\n{prepared.SharedHeaders}{response.Extra}";
            var created = environment.CreateWebResourceResponse(stream, response.Status, response.Reason, headers);
            try
            {
                args.PutResponse(created);
            }
            finally
            {
                Marshal.Release(created);
            }
        }
        finally
        {
            Marshal.Release(stream);
        }
    }

    private static string Shorten(string text) => text.Length <= 200 ? text : string.Concat(text.AsSpan(0, 200), "…");

    /// <summary>The prepared policy, or the one without the page's hashes while it is being prepared; never waits.</summary>
    private Served Prepared => _decoded.IsCompletedSuccessfully ? _served ??= _decoded.Result : _fallback;

    private readonly record struct Response(int Status, string Reason, string ContentType, ReadOnlyMemory<byte> Body, string Extra)
    {
        /// <summary>The response's own policy (a document with a nonce); null for the shared one.</summary>
        public string? Csp { get; init; }
    }

    /// <summary>The composed policy, and the headers after it that every response shares.</summary>
    private sealed record Served(PagePolicy Policy, string SharedHeaders);
}
