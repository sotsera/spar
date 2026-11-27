using System.Text;
using Spar.Runtime;

namespace Spar.Hosting;

/// <summary>One answer of the app's origin: what a host puts on the request, with the shared headers added.</summary>
/// <param name="Csp">The response's own policy (a document with a style nonce); null for the shared one.</param>
/// <param name="Allow">The <c>Allow</c> header's value, for a refused method; null otherwise.</param>
/// <param name="Disposition">The <c>Content-Disposition</c> header's value, for a blob served as a download; null otherwise.</param>
internal readonly record struct PageResponse(
    int Status,
    string Reason,
    string ContentType,
    ReadOnlyMemory<byte> Body,
    string? Csp = null,
    string? Allow = null,
    string? Disposition = null);

/// <summary>
/// What every desktop host's origin answers (plan section 3b), host-neutral: the app's packed page from memory, 404 for
/// an unknown path, 405 for a method other than GET and HEAD, never a redirect, and an HTML document with its fresh style
/// nonce when the app asked for one. The compressed files are decoded, and the policy composed with the page's hashes,
/// on a background thread at start; until then <see cref="IsPrepared"/> is false and a host defers the request rather
/// than decode on its UI thread. Each host turns a <see cref="PageResponse"/> into its WebView's own response.
/// </summary>
internal sealed class PageResponder
{
    public const string NoPage =
        "This build of the app embeds no page. Debug builds serve it from the development server, which arrives with the " +
        "development loop; until then build with -p:SparDevServer=false, or run a Release build.";

    private readonly SparFrontend? _frontend;
    private readonly Task<PagePolicy> _prepared;
    private readonly PagePolicy _fallback;
    private PagePolicy? _policy;

    /// <param name="origin">The page's origin with its scheme, which <c>connect-src</c> names.</param>
    /// <param name="development">A Development or Test build, where <c>CspReports.DevelopmentOnly</c> reports.</param>
    public PageResponder(SparFrontend? frontend, SparPageSecurity security, string origin, bool development)
    {
        _frontend = frontend;
        Origin = origin;
        _prepared = Task.Run(() => Prepare(frontend, security, origin, development));

        // For a refusal that comes before the page is prepared: the policy without the page hashes.
        _fallback = Prepare(null, security, origin, development);
    }

    /// <summary>The origin this responder answers for.</summary>
    public string Origin { get; }

    /// <summary>
    /// The window kinds' routes (<c>/settings</c>): a path at or below one, whose last segment has no extension, is
    /// answered with the frontend's <c>index.html</c>, so the page's router shows the window's view.
    /// </summary>
    public IReadOnlyList<string> Routes { get; init; } = [];

    /// <summary>Whether the build embeds a page at all.</summary>
    public bool HasPage => _frontend is not null;

    /// <summary>The policy every response carries, once the page is prepared; the host logs it at start.</summary>
    public Task<PagePolicy> Policy => _prepared;

    /// <summary>Whether the page is prepared, so <see cref="Answer"/> never waits.</summary>
    public bool IsPrepared => _prepared.IsCompletedSuccessfully;

    /// <summary>The prepared policy, or the one without the page's hashes while it is being prepared; never waits.</summary>
    public PagePolicy CurrentPolicy => IsPrepared ? _policy ??= _prepared.Result : _fallback;

    /// <summary>The headers after <c>Content-Security-Policy</c> that every response shares, in the order hosts send them.</summary>
    public IEnumerable<KeyValuePair<string, string>> SharedHeaders => CurrentPolicy.Headers().Skip(1);

    /// <summary>Whether an address belongs to the origin: the origin, then the path's slash; another port is foreign.</summary>
    public static bool IsOnOrigin(string address, string origin) =>
        address.StartsWith(origin, StringComparison.Ordinal)
        && address.Length > origin.Length
        && address[origin.Length] == '/';

    /// <summary>The decoded path of an address on <paramref name="origin"/>, without its query and fragment.</summary>
    public static string PathOf(string address, string origin)
    {
        var path = address.AsSpan(origin.Length);
        var end = path.IndexOfAny('?', '#');
        return PercentDecode(end < 0 ? path : path[..end]);
    }

    /// <summary>A refusal with a short code as its body.</summary>
    public static PageResponse Refusal(int status, string reason, string code) =>
        new(status, reason, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(code));

    /// <summary>
    /// What the origin answers for one decoded path and method, once <see cref="IsPrepared"/>. <paramref name="notFound"/>
    /// hears each path that has no file.
    /// </summary>
    public PageResponse Answer(string path, string method, Action<string> notFound)
    {
        if (method != "GET" && method != "HEAD")
        {
            return Refusal(405, "Method Not Allowed", "method-not-allowed") with { Allow = "GET, HEAD" };
        }

        if (_frontend is null)
        {
            return new PageResponse(404, "Not Found", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(NoPage));
        }

        var relative = path == "/" || IsRoute(path) ? "index.html" : path.TrimStart('/');
        if (!_frontend.TryGet(relative, out var content, out var contentType))
        {
            notFound(path);
            return Refusal(404, "Not Found", "not-found");
        }

        if (CurrentPolicy.StyleNonce && contentType.StartsWith("text/html", StringComparison.Ordinal))
        {
            return Document(content, contentType, method);
        }

        return new PageResponse(200, "OK", contentType, method == "HEAD" ? ReadOnlyMemory<byte>.Empty : content);
    }

    /// <summary>Whether <paramref name="path"/> is a window kind's route or a view below it (no extension in its last segment).</summary>
    internal bool IsRoute(string path) =>
        !path[(path.LastIndexOf('/') + 1)..].Contains('.')
        && Routes.Any(route => AppWindows.WindowRoutes.IsUnder(path, route));

    /// <summary>
    /// What the page and its decoded files need, prepared once off the UI thread: every compressed file decoded, and the
    /// policy composed with the page's import-map hashes and its theme and splash style hashes.
    /// </summary>
    private static PagePolicy Prepare(SparFrontend? frontend, SparPageSecurity security, string origin, bool development)
    {
        frontend?.DecodeAll();
        var scripts = frontend is null ? [] : PageHtml.ImportMapHashes(frontend);
        var styles = frontend is null ? [] : PageHtml.StyleHashes(frontend);
        return new PagePolicy(security, origin, development, scripts, styles);
    }

    /// <summary>
    /// An HTML document with its fresh style nonce (<c>StyleNonce()</c>): in a meta element after <c>&lt;head&gt;</c> and
    /// in the document's policy. The copy is of a small file, and needs no parsing beyond finding that tag.
    /// </summary>
    private PageResponse Document(ReadOnlyMemory<byte> content, string contentType, string method)
    {
        var nonce = PageHtml.NewNonce();
        var policy = CurrentPolicy.ContentSecurityPolicyWithNonce(nonce);
        if (method == "HEAD")
        {
            return new PageResponse(200, "OK", contentType, ReadOnlyMemory<byte>.Empty, policy);
        }

        var html = content.Span;
        var at = PageHtml.NonceInsertionPoint(html);
        byte[] document = [.. html[..at], .. PageHtml.NonceMeta(nonce), .. html[at..]];
        return new PageResponse(200, "OK", contentType, document, policy);
    }

    /// <summary>
    /// Decodes <c>%XX</c> escapes as UTF-8; a malformed escape stays as it is, and finds no file. Both WebViews escape
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
}
