using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Spar.Hosting;
using Spar.Runtime;

namespace Spar.DevServer;

/// <summary>
/// What a Debug build's origin answers (plan section 4; decision "Debug serves Vite through the app's own origin"): each
/// request, fetched from the Vite development server in-process and answered as the app's own origin's, host-neutral.
/// The host takes the request on its UI thread, reads the method, the address and the headers below, and finishes the
/// response there once <see cref="AnswerAsync"/> completes on the thread pool; the UI thread never waits.
/// <list type="bullet">
/// <item><b>What it refuses.</b> The host has already refused every address off the app's origin. Here: methods other than
/// GET and HEAD (405), and every path under <c>/_spar/</c> but this proxy's own pages (404), so the host's endpoints never
/// reach Vite. The fetch goes only to the one loopback origin of <see cref="DevServerAddress"/>; the path is appended to
/// it, never resolved, so no request leaves for another host. Vite's redirects are refused (a host never answers with
/// one), and so is an encoded body, which the WebView would not decode.</item>
/// <item><b>What it forwards:</b> <c>Accept</c> and the <c>Sec-Fetch-*</c> headers, nothing else: no cookies, no
/// conditional headers, so Vite never answers 304 and a cached page never keeps an old nonce
/// (<c>spikes/csp-navigation</c>).</item>
/// <item><b>What it answers:</b> Vite's status, body and <c>Content-Type</c>, and the host's own policy and headers in place
/// of Vite's (<c>Cache-Control: no-store</c> included, no <c>ETag</c>), on every response, worker scripts included. An HTML
/// response gets a fresh nonce in place of the plugin's placeholder (<see cref="NoncePlaceholder"/>, Vite's
/// <c>html.cspNonce</c>), in its body and its policy's <c>script-src</c> and <c>style-src</c>; the policy names the
/// server's HMR WebSocket in <c>connect-src</c>.</item>
/// <item><b>When the server is not there:</b> a document gets the Development error page (<see cref="DevErrorPage"/>) with
/// the server's last output and a Retry button; anything else gets 502.</item>
/// </list>
/// </summary>
internal sealed class DevServerProxy
{
    /// <summary>The Spar Vite plugin's <c>html.cspNonce</c> in development (src/Spar.Client/src/vite/devServer.ts).</summary>
    public const string NoncePlaceholder = "spar-dev-nonce";

    /// <summary>The proxy's own pages and the error page's Retry, under the host's <c>/_spar/</c>.</summary>
    public const string RoutePrefix = "/_spar/dev-server/";

    /// <summary>The largest response taken from the server; Vite's largest prebundled module is a few megabytes.</summary>
    public const int MaxBodyBytes = 64 * 1024 * 1024;

    /// <summary>How long one request waits for the server; its first request may wait for dependency optimisation.</summary>
    private static readonly TimeSpan RequestLimit = TimeSpan.FromSeconds(120);

    private readonly DevServerSession _session;
    private readonly SparPageSecurity _security;
    private readonly string _origin;
    private readonly HostLog _log;
    private readonly ILogger _requests;
    private readonly Lock _gate = new();
    private (string Socket, PagePolicy Policy)? _policy;

    /// <param name="origin">The app's origin with its scheme: <c>https://app.localhost</c> or <c>spar://app</c>.</param>
    public DevServerProxy(DevServerSession session, SparPageSecurity security, string origin, HostLog log)
    {
        _session = session;
        _security = security;
        _origin = origin;
        _log = log;
        _requests = log.For(DevServerSession.Category);
        PlainPolicy = new PagePolicy(security, origin, development: true);
    }

    /// <summary>The request headers the host reads and passes on, and the only ones forwarded to the server.</summary>
    public static IReadOnlyList<string> ForwardedHeaders { get; } =
        ["Accept", "Sec-Fetch-Dest", "Sec-Fetch-Mode", "Sec-Fetch-Site", "Sec-Fetch-User"];

    /// <summary>The policy of the proxy's own pages, which need no nonce and no WebSocket.</summary>
    public PagePolicy PlainPolicy { get; }

    /// <summary>
    /// The answer for one request to the app's origin: <paramref name="pathAndQuery"/> is the address after the
    /// origin, starting with <c>/</c>; <paramref name="headers"/> holds those of <see cref="ForwardedHeaders"/> it had.
    /// </summary>
    public async Task<PageResponse> AnswerAsync(string pathAndQuery, string method, IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        var path = PageResponder.PathOf(_origin + pathAndQuery, _origin);
        if (path.StartsWith("/_spar/", StringComparison.Ordinal))
        {
            return await OwnRouteAsync(path, method).ConfigureAwait(false);
        }

        if (method != "GET" && method != "HEAD")
        {
            return PageResponder.Refusal(405, "Method Not Allowed", "method-not-allowed") with { Allow = "GET, HEAD", Csp = PlainPolicy.ContentSecurityPolicy };
        }

        var document = IsDocument(headers);
        var outcome = await _session.Current.ConfigureAwait(false);
        if (!outcome.IsReady)
        {
            return Unavailable(outcome, document);
        }

        try
        {
            return await FetchAsync(outcome.Address!, pathAndQuery, method, headers, document).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            // Refused, or a connection that broke while the body was read: the server is gone or going.
            var failed = _session.Unreachable(outcome, $"Nothing answers at {outcome.Address!.Origin}: {exception.Message}");
            return Unavailable(failed, document);
        }
        catch (ArgumentException)
        {
            // An address whose path would leave the server (DevServerAddress.Target): never fetched.
            return Refused(404, "not-found");
        }
        catch (OperationCanceledException)
        {
            _log.Warn($"The development server did not answer {Shorten(pathAndQuery)} within {RequestLimit.TotalSeconds:0} s.");
            var slow = outcome with { Problem = $"The development server did not answer within {RequestLimit.TotalSeconds:0} s.", Output = [] };
            return Unavailable(slow, document);
        }
    }

    /// <summary>Whether the request is for a document (a window or frame navigation), which gets the error page.</summary>
    internal static bool IsDocument(IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        var destination = Header(headers, "Sec-Fetch-Dest");
        if (destination is not null)
        {
            return destination is "document" or "iframe" or "frame";
        }

        return Header(headers, "Accept")?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>
    /// An HTML body with its fresh nonce in place of the plugin's placeholder; null when the body has no placeholder,
    /// which means it did not come through the Spar Vite plugin.
    /// </summary>
    internal static byte[]? WithNonce(byte[] html, string nonce)
    {
        var text = Encoding.UTF8.GetString(html);
        return text.Contains(NoncePlaceholder, StringComparison.Ordinal)
            ? Encoding.UTF8.GetBytes(text.Replace(NoncePlaceholder, nonce, StringComparison.Ordinal))
            : null;
    }

    private async Task<PageResponse> FetchAsync(
        DevServerAddress address, string pathAndQuery, string method, IReadOnlyList<KeyValuePair<string, string>> headers, bool document)
    {
        var started = Stopwatch.GetTimestamp();
        using var request = new HttpRequestMessage(method == "HEAD" ? HttpMethod.Head : HttpMethod.Get, address.Target(pathAndQuery));
        foreach (var (name, value) in headers)
        {
            if (ForwardedHeaders.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        using var limit = new CancellationTokenSource(RequestLimit);
        using var response = await _session.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token).ConfigureAwait(false);
        var status = (int)response.StatusCode;
        if (status is >= 300 and < 400)
        {
            _log.Warn($"The development server answered {Shorten(pathAndQuery)} with a redirect ({status}), which the app's origin never sends.");
            return Refused(502, "redirect-refused");
        }

        if (response.Content.Headers.ContentEncoding.Any(encoding => !encoding.Equals("identity", StringComparison.OrdinalIgnoreCase)))
        {
            _log.Warn($"The development server encoded {Shorten(pathAndQuery)} ({string.Join(", ", response.Content.Headers.ContentEncoding)}), which the WebView does not decode.");
            return Refused(502, "encoded-response");
        }

        var body = await ReadAsync(response.Content, limit.Token).ConfigureAwait(false);
        if (body is null)
        {
            _log.Warn($"The development server's answer to {Shorten(pathAndQuery)} is larger than {MaxBodyBytes} bytes.");
            return Refused(502, "response-too-large");
        }

        var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
        var policy = PolicyFor(address);
        var csp = policy.ContentSecurityPolicy;
        if (contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
        {
            var nonce = PageHtml.NewNonce();
            if (WithNonce(body, nonce) is { } withNonce)
            {
                body = withNonce;
                csp = policy.ContentSecurityPolicyWithNonce(nonce);
            }
            else if (document)
            {
                return ErrorPage(new DevServerOutcome(
                    address,
                    $"The development server at {address.Origin} served the page without the Spar Vite plugin's nonce. Add spar() from \"@spar/app/vite\" to the plugins in vite.config.ts.",
                    _session.Settings.Url is not null,
                    []));
            }
        }

        if (_requests.IsEnabled(LogLevel.Debug))
        {
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            _requests.Log(LogLevel.Debug, default, $"{method} {Shorten(pathAndQuery)} {status} {body.Length} B in {elapsed:0.0} ms", null, static (text, _) => text);
        }

        var reason = response.ReasonPhrase is { Length: > 0 } phrase ? phrase : response.StatusCode.ToString();
        return new PageResponse(status, reason, contentType, method == "HEAD" ? ReadOnlyMemory<byte>.Empty : body, csp);
    }

    /// <summary>The proxy's own routes: the error page's style and script, and its Retry.</summary>
    private async Task<PageResponse> OwnRouteAsync(string path, string method)
    {
        if (path == RoutePrefix + "retry" && method == "POST")
        {
            var outcome = await _session.RetryAsync().ConfigureAwait(false);
            return outcome.IsReady
                ? new PageResponse(204, "No Content", "text/plain; charset=utf-8", ReadOnlyMemory<byte>.Empty, PlainPolicy.ContentSecurityPolicy)
                : Refused(503, "dev-server-unavailable");
        }

        var file = path switch
        {
            RoutePrefix + "page.css" => ("text/css; charset=utf-8", DevErrorPage.Style),
            RoutePrefix + "page.js" => ("text/javascript; charset=utf-8", DevErrorPage.Script),
            _ => default((string, string)?),
        };
        if (file is not { } found || method is not ("GET" or "HEAD"))
        {
            return Refused(404, "not-found");
        }

        var bytes = method == "HEAD" ? [] : Encoding.UTF8.GetBytes(found.Item2);
        return new PageResponse(200, "OK", found.Item1, bytes, PlainPolicy.ContentSecurityPolicy);
    }

    private PageResponse Unavailable(DevServerOutcome outcome, bool document) =>
        document ? ErrorPage(outcome) : Refused(502, "dev-server-unavailable");

    private PageResponse ErrorPage(DevServerOutcome outcome)
    {
        _log.Warn($"Development error page shown: {outcome.Problem}");
        var html = DevErrorPage.Render(outcome, _session.Settings);
        return new PageResponse(503, "Service Unavailable", "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html), PlainPolicy.ContentSecurityPolicy);
    }

    private PageResponse Refused(int status, string code) =>
        PageResponder.Refusal(status, status == 404 ? "Not Found" : status == 503 ? "Service Unavailable" : "Bad Gateway", code)
            with { Csp = PlainPolicy.ContentSecurityPolicy };

    /// <summary>The policy for pages from <paramref name="address"/>, composed once per server (a retry may change the port).</summary>
    private PagePolicy PolicyFor(DevServerAddress address)
    {
        lock (_gate)
        {
            if (_policy is { } cached && cached.Socket == address.HmrSocket)
            {
                return cached.Policy;
            }
        }

        var policy = new PagePolicy(_security, _origin, development: true, devServerSocket: address.HmrSocket);
        lock (_gate)
        {
            _policy = (address.HmrSocket, policy);
        }

        _log.Info($"Content-Security-Policy (development server): {policy.ContentSecurityPolicyWithNonce("<nonce>")}");
        return policy;
    }

    private static async Task<byte[]?> ReadAsync(HttpContent content, CancellationToken token)
    {
        if (content.Headers.ContentLength > MaxBodyBytes)
        {
            return null;
        }

        await using var stream = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var buffer = new MemoryStream(content.Headers.ContentLength is { } length ? (int)length : 16 * 1024);
        var chunk = new byte[64 * 1024];
        while (await stream.ReadAsync(chunk, token).ConfigureAwait(false) is var read and > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static string? Header(IReadOnlyList<KeyValuePair<string, string>> headers, string name)
    {
        foreach (var (key, value) in headers)
        {
            if (key.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }

    private static string Shorten(string text) => text.Length <= 200 ? text : string.Concat(text.AsSpan(0, 200), "…");
}
