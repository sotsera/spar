using System.Runtime.InteropServices;
using System.Diagnostics;
using Spar.Hosting;
using Spar.Runtime;
using Spar.Windows.Interop;

namespace Spar.Windows;

/// <summary>
/// Serves the app's packed page on <c>https://app.localhost</c> through <c>WebResourceRequested</c> (plan section 3,
/// "Assets and the bridge"). The source-kinds filter (<c>ICoreWebView2_22</c>) sees documents and workers alike; every
/// request to the origin is answered, unknown paths with 404, and a second filter answers every foreign document with
/// 403, so a navigation never reaches the network (section 3b). What the origin answers is the shared
/// <see cref="PageResponder"/>'s; nothing is ever a redirect. Every response, worker scripts included, carries the
/// app's <see cref="PagePolicy"/> headers and an exact <c>Content-Type</c>. Violation reports posted to <c>report-uri</c>
/// are copied out, within a size and rate limit, and handed to the <see cref="CspViolationLog"/>. It runs on the UI
/// thread and answers from memory; a request that arrives before the page is prepared takes a deferral and is answered
/// when it is.
/// </summary>
internal sealed class PageServer
{
    public const string Origin = "https://app.localhost";

    /// <summary>The largest <c>report-uri</c> body read; a larger one is answered 204 and dropped unread.</summary>
    public const int MaxReportBytes = 16 * 1024;

    private readonly PageResponder _responder;
    private readonly UiThread _ui;
    private readonly HostLog _log;
    private readonly CspViolationLog? _violations;
    private readonly ReportLimiter _reports = new();
    private string? _sharedHeaders;

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
        _ui = ui;
        _log = log;
        _violations = violations;
        _responder = new PageResponder(frontend, security, Origin, development);
        if (frontend is null)
        {
            log.Error(PageResponder.NoPage);
        }
    }

    /// <summary>The policy every response carries, once the page is decoded; the host logs it at start.</summary>
    public Task<PagePolicy> Policy => _responder.Policy;

    /// <summary>The page's first address.</summary>
    public static string StartUrl => Origin + "/";

    /// <summary>
    /// Whether an address belongs to the app's origin. WebView2 hands the host canonical URLs (lower-case scheme and
    /// host, no default port), so the origin is a prefix ending at the path's slash; anything else, another port
    /// included, is foreign. <see cref="Uri"/> is not used: its flag names would trip the production boundary scan.
    /// </summary>
    public static bool IsAppOrigin(string address) => PageResponder.IsOnOrigin(address, Origin);

    /// <summary>The decoded path of an address on the app's origin, without its query and fragment.</summary>
    internal static string PathOf(string address) => PageResponder.PathOf(address, Origin);

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

            if (_responder.IsPrepared)
            {
                var report = method == "POST" && PathOf(uri) == PagePolicy.ReportPath;
                Respond(environment, args, report ? Report(args) : Answer(uri, method));
                return;
            }

            // The compressed files are still being decoded: answer when they are, never decode on this thread.
            var deferral = Com.Take<ICoreWebView2Deferral>(args.GetDeferral());
            deferred = true;
            _ = _responder.Policy.ContinueWith(_ => _ui.Post(() => AnswerLater(environment, args, deferral, uri, method)), TaskScheduler.Default);
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
    /// A violation report the browser posted to <c>report-uri</c>: past the rate or size limit, or with the endpoint off
    /// in this build, it is answered 204 and dropped unread; otherwise its body is copied out (a memory read) and parsed
    /// and logged off this thread.
    /// </summary>
    private PageResponse Report(ICoreWebView2WebResourceRequestedEventArgs2 args)
    {
        if (_violations is null)
        {
            return NoContent();
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

        return NoContent();
    }

    /// <summary>What the app's origin answers for one address and method.</summary>
    private PageResponse Answer(string address, string method) =>
        _responder.Answer(PathOf(address), method, path => _log.Info($"404 for {Shorten(path)}"));

    private static PageResponse Refusal(int status, string reason, string code) => PageResponder.Refusal(status, reason, code);

    private static PageResponse NoContent() => new(204, "No Content", "text/plain; charset=utf-8", ReadOnlyMemory<byte>.Empty);

    /// <summary>
    /// Puts the response on the request. <c>SHCreateMemStream</c> copies the bytes, and WebView2 reads the stream whole on
    /// this thread (pitfalls, "<c>WebResourceRequested</c> cannot stream"), so the image's memory is only read here.
    /// </summary>
    private unsafe void Respond(ICoreWebView2Environment10 environment, ICoreWebView2WebResourceRequestedEventArgs2 args, PageResponse response)
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
            var csp = response.Csp ?? _responder.CurrentPolicy.ContentSecurityPolicy;
            var allow = response.Allow is null ? "" : $"\r\nAllow: {response.Allow}";
            var headers = $"Content-Type: {response.ContentType}\r\nContent-Security-Policy: {csp}\r\n{SharedHeaders()}{allow}";
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

    /// <summary>The headers every response shares after its policy, in WebView2's form; composed once the page is prepared.</summary>
    private string SharedHeaders()
    {
        if (_sharedHeaders is { } prepared)
        {
            return prepared;
        }

        var text = string.Join("\r\n", _responder.SharedHeaders.Select(header => $"{header.Key}: {header.Value}"));
        if (_responder.IsPrepared)
        {
            _sharedHeaders = text;
        }

        return text;
    }

    private static string Shorten(string text) => text.Length <= 200 ? text : string.Concat(text.AsSpan(0, 200), "…");
}
