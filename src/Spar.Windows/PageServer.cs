using System.Runtime.InteropServices;
using System.Text;
using Spar.Hosting;
using Spar.Windows.Interop;

namespace Spar.Windows;

/// <summary>
/// Serves the app's packed page on <c>https://app.localhost</c> through <c>WebResourceRequested</c> (plan section 3,
/// "Assets and the bridge"). The source-kinds filter (<c>ICoreWebView2_22</c>) sees documents and workers alike; every
/// request to the origin is answered, unknown paths with 404, and a second filter answers every foreign document with
/// 403, so a navigation never reaches the network (section 3b). Nothing is ever a redirect. Every response carries
/// <see cref="SparPageHeaders"/> and an exact <c>Content-Type</c>. It runs on the UI thread and answers from memory:
/// compressed files are decoded on a background thread at start, and a request that arrives before that has finished
/// takes a deferral and is answered when it has.
/// </summary>
internal sealed class PageServer
{
    public const string Origin = "https://app.localhost";

    private const string NoPage =
        "This build of the app embeds no page. Debug builds serve it from the development server, which arrives with the " +
        "development loop; until then build with -p:SparDevServer=false, or run a Release build.";

    private readonly SparFrontend? _frontend;
    private readonly Task _decoded;
    private readonly UiThread _ui;
    private readonly HostLog _log;
    private readonly string _commonHeaders;

    public PageServer(SparFrontend? frontend, UiThread ui, HostLog log)
    {
        _frontend = frontend;
        _ui = ui;
        _log = log;
        _decoded = frontend is null ? Task.CompletedTask : Task.Run(frontend.DecodeAll);
        _commonHeaders = string.Join("\r\n", SparPageHeaders.For(Origin).Select(header => $"{header.Key}: {header.Value}"));
        if (frontend is null)
        {
            log.Error(NoPage);
        }
    }

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

            if (_decoded.IsCompleted)
            {
                Respond(environment, args, Answer(uri, method));
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

    /// <summary>What the app's origin answers for one address and method.</summary>
    private Response Answer(string address, string method)
    {
        var path = PathOf(address);

        // The browser posts CSP reports itself (report-uri); the body is not read, so the report costs this thread nothing.
        if (method == "POST" && path == SparPageHeaders.CspReportPath)
        {
            _log.Warn("The page reported a Content Security Policy violation.");
            return new Response(204, "No Content", "text/plain; charset=utf-8", ReadOnlyMemory<byte>.Empty, "");
        }

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

        return new Response(200, "OK", contentType, method == "HEAD" ? ReadOnlyMemory<byte>.Empty : content, "");
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
            var headers = $"Content-Type: {response.ContentType}\r\n{_commonHeaders}{response.Extra}";
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

    private readonly record struct Response(int Status, string Reason, string ContentType, ReadOnlyMemory<byte> Body, string Extra);
}
