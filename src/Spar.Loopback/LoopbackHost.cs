using System.Buffers;
using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spar.Bridge;
using Spar.DevServer;
using Spar.Hosting;
using Spar.Runtime;

namespace Spar.Loopback;

/// <summary>What the loopback host serves, and where it reports.</summary>
internal sealed class LoopbackOptions
{
    public required SparDispatcher Dispatcher { get; init; }

    /// <summary>The host's contract fingerprint, sent in <c>open</c>.</summary>
    public required string Fingerprint { get; init; }

    /// <summary>The page: the app's packed one, or folders a test names; null when <see cref="DevServer"/> serves it.</summary>
    public ILoopbackPage? Page { get; init; }

    /// <summary>
    /// A Debug build's development server, which serves the page through this origin instead of <see cref="Page"/>, with
    /// the Debug policy and each document's nonce (plan section 4); <see cref="HostLog"/> is then required.
    /// </summary>
    public DevServerSession? DevServer { get; init; }

    /// <summary>The run's host log, which the development server's proxy writes to.</summary>
    public HostLog? HostLog { get; init; }

    /// <summary>The port of 127.0.0.1 to listen on; 0, the default, takes a free one.</summary>
    public int Port { get; init; }

    /// <summary>The bearer token, base64url of 16 to 48 bytes; null makes a new one of 24 random bytes.</summary>
    public string? Token { get; init; }

    /// <summary>
    /// A dev session's run id (<see cref="LoopbackSession"/>): each document's channel names it, and
    /// <c>POST /_spar/session</c> answers it, so a page that loses its channel can wait for the backend's next run at
    /// the same address and token, and reload once it is a new one. Null (the test host) offers neither.
    /// </summary>
    public string? RunId { get; init; }

    /// <summary>Where the browser's CSP reports go, each body at most 64 KiB; null logs them through <see cref="Log"/>.</summary>
    public Action<byte[]>? Reports { get; init; }

    /// <summary>Whether failures carry their detail (Development and tests only).</summary>
    public bool IncludeDetails { get; init; }

    /// <summary>The app's page security, from its generated catalog: the policy every response carries.</summary>
    public SparPageSecurity Security { get; init; } = SparPageSecurity.Default;

    /// <summary>The page's import-map hashes, which <c>script-src</c> names (<see cref="PageHtml"/>).</summary>
    public IReadOnlyList<string> ScriptHashes { get; init; } = [];

    /// <summary>What the host refused or dropped, and the page's CSP violations. Called from any thread.</summary>
    public Action<string>? Log { get; init; }
}

/// <summary>
/// The page in an ordinary browser, its bridge over HTTP on 127.0.0.1 (plan sections 2c and 3c). Each document opens one
/// streamed channel that carries every host message; the page posts its own messages in order, one request at a time
/// (decision "Loopback transport: one ordered send path"). Every request must name this host and port (<c>Host</c>);
/// bridge requests need the bearer token and, when the browser sends one, this origin.
/// </summary>
internal sealed class LoopbackHost : IAsyncDisposable
{
    /// <summary>The header of a dev session's channel that names the backend's run (<see cref="LoopbackOptions.RunId"/>).</summary>
    public const string RunHeader = "X-Spar-Run";

    private const string ChannelPath = "/_spar/channel";
    private const string SendPath = "/_spar/send";
    private const string SessionPath = "/_spar/session";
    private const string DocumentHeader = "X-Spar-Doc";
    private const int MaxReportBytes = 64 * 1024;

    private readonly WebApplication _server;
    private readonly LoopbackOptions _options;
    private readonly byte[] _token;
    private readonly ConcurrentDictionary<string, Document> _documents = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _stopping = new();
    private readonly ConcurrentDictionary<string, PagePolicy> _policies = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DevServerProxy> _proxies = new(StringComparer.Ordinal);
    private int _port;

    private LoopbackHost(WebApplication server, LoopbackOptions options, byte[] token)
    {
        _server = server;
        _options = options;
        _token = token;
        Token = Base64Url.EncodeToString(_token);
    }

    /// <summary>The bearer token, which the page gets from the URL fragment only (plan section 3c).</summary>
    public string Token { get; }

    /// <summary>The origin the page is served from, for example <c>http://127.0.0.1:52311</c>.</summary>
    public Uri Origin => new($"http://127.0.0.1:{_port}");

    /// <summary>Raised when a document's page reports <c>ui-ready</c>, with the document id.</summary>
    public event Action<string>? DocumentReady;

    /// <summary>
    /// Starts listening on 127.0.0.1, on <see cref="LoopbackOptions.Port"/> or a free port. A port another process holds
    /// is an <see cref="IOException"/> whose inner exception is an <see cref="AddressInUseException"/>.
    /// </summary>
    public static async Task<LoopbackHost> StartAsync(LoopbackOptions options, CancellationToken cancellationToken)
    {
        if ((options.Page is null) == (options.DevServer is null) || (options.DevServer is not null && options.HostLog is null))
        {
            throw new ArgumentException("A loopback host serves either a page or a development server (with its host log).", nameof(options));
        }

        var token = options.Token is null ? RandomNumberGenerator.GetBytes(24) : LoopbackSession.TokenBytes(options.Token)
            ?? throw new ArgumentException("The token is not base64url of 16 to 48 bytes.", nameof(options));
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        builder.WebHost.UseKestrelCore();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            // 127.0.0.1 only, never another interface (plan section 3c): the page is for this machine's browser.
            kestrel.Listen(IPAddress.Loopback, options.Port);
            kestrel.AddServerHeader = false;

            // A document's channel can be quiet for minutes; that is not a slow client.
            kestrel.Limits.MinResponseDataRate = null;

            // The page sends batches of at most 1 MiB of text, or one larger message alone. Up to twice the bridge's
            // message limit, a body reaches the session, which refuses an oversized call by its id; past that the
            // server refuses the request (413) and the document's channel ends.
            kestrel.Limits.MaxRequestBodySize = Math.Max(30_000_000L, 2L * options.Dispatcher.Limits.MaxMessageBytes);
        });
        builder.Services.Configure<HostOptions>(host => host.ShutdownTimeout = TimeSpan.FromSeconds(5));

        var server = builder.Build();
        var host = new LoopbackHost(server, options, token);
        server.Run(host.HandleAsync);

        try
        {
            await server.StartAsync(cancellationToken);
        }
        catch
        {
            await server.DisposeAsync();
            host._stopping.Dispose();
            throw;
        }

        host._port = new Uri(server.Urls.Single()).Port;
        return host;
    }

    /// <summary>
    /// A host event (such as <c>backend-updated</c>) to every open document whose page listed <c>event</c>; returns how
    /// many documents are open. Called from any thread.
    /// </summary>
    public int SendEvent(string name, ReadOnlyMemory<byte> payload)
    {
        var open = 0;
        foreach (var document in _documents.Values)
        {
            document.Session.SendEvent(name, payload);
            open++;
        }

        return open;
    }

    /// <summary>The page's address with the token in its fragment, for the browser to open.</summary>
    public Uri PageUrl(string path = "/") => new(Origin, $"{path}#token={Token}");

    public async ValueTask DisposeAsync()
    {
        _stopping.Cancel();
        foreach (var document in _documents.Values)
        {
            document.Session.Close();
        }

        await _server.StopAsync(CancellationToken.None);
        await _server.DisposeAsync();
        _stopping.Dispose();
    }

    private async Task HandleAsync(HttpContext context)
    {
        var request = context.Request;
        var response = context.Response;

        // DNS rebinding: a page on another name that resolves here must get nothing, not even the assets.
        if (!IsThisHost(request.Host))
        {
            await RefuseAsync(response, StatusCodes.Status403Forbidden, "forbidden-host");
            return;
        }

        var origin = $"http://{request.Host.Value!.ToLowerInvariant()}";
        foreach (var (name, value) in PolicyFor(origin).Headers())
        {
            response.Headers[name] = value;
        }

        var path = request.Path.Value ?? "/";
        if (_options.DevServer is not null && path.StartsWith(DevServerProxy.RoutePrefix, StringComparison.Ordinal))
        {
            await DevServerRouteAsync(context, origin);
            return;
        }

        if (path.StartsWith("/_spar/", StringComparison.Ordinal))
        {
            await BridgeAsync(context, path, origin);
            return;
        }

        if (_options.DevServer is not null)
        {
            await ProxyAsync(context, origin);
            return;
        }

        await AssetAsync(context, path, PolicyFor(origin));
    }

    /// <summary>
    /// The policy for one of the two names the page may use (127.0.0.1 or localhost), composed once: connect-src names
    /// the origin. A Development or Test build is the only one with a loopback host, so DevelopmentOnly reports here.
    /// </summary>
    private PagePolicy PolicyFor(string origin) => _policies.GetOrAdd(
        origin,
        static (name, options) => new PagePolicy(options.Security, name, development: true, options.ScriptHashes),
        _options);

    private bool IsThisHost(HostString host)
    {
        var name = host.Host;
        var named = string.Equals(name, "127.0.0.1", StringComparison.Ordinal)
            || string.Equals(name, "localhost", StringComparison.OrdinalIgnoreCase);
        return named && host.Port == _port;
    }

    private async Task AssetAsync(HttpContext context, string path, PagePolicy policy)
    {
        var request = context.Request;
        var response = context.Response;
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
        {
            response.Headers.Allow = "GET, HEAD";
            await RefuseAsync(response, StatusCodes.Status405MethodNotAllowed, "method-not-allowed");
            return;
        }

        var relative = path == "/" ? "index.html" : path[1..];

        // Every request is answered, unknown paths with 404 (pitfalls, "An unanswered request to the app origin").
        if (!LoopbackAssets.IsSafePath(relative) || !_options.Page!.TryGet(relative, out var content, out var contentType))
        {
            await RefuseAsync(response, StatusCodes.Status404NotFound, "not-found");
            return;
        }

        // As the desktop hosts do: a document gets a fresh style nonce, in its policy and in a meta element.
        if (policy.StyleNonce && contentType.StartsWith("text/html", StringComparison.Ordinal))
        {
            var nonce = PageHtml.NewNonce();
            var at = PageHtml.NonceInsertionPoint(content.Span);
            content = (byte[])[.. content.Span[..at], .. PageHtml.NonceMeta(nonce), .. content.Span[at..]];
            response.Headers.ContentSecurityPolicy = policy.ContentSecurityPolicyWithNonce(nonce);
        }

        response.ContentType = contentType;
        response.ContentLength = content.Length;
        if (HttpMethods.IsGet(request.Method))
        {
            await response.Body.WriteAsync(content, context.RequestAborted);
        }
    }

    private async Task BridgeAsync(HttpContext context, string path, string origin)
    {
        var request = context.Request;
        var response = context.Response;
        if (!HttpMethods.IsPost(request.Method))
        {
            response.Headers.Allow = "POST";
            await RefuseAsync(response, StatusCodes.Status405MethodNotAllowed, "method-not-allowed");
            return;
        }

        // The browser sends CSP reports itself, without the token; they are only logged.
        if (path == PagePolicy.ReportPath)
        {
            await ReportAsync(context);
            return;
        }

        var sentOrigin = request.Headers.Origin.ToString();
        if (sentOrigin.Length > 0 && !string.Equals(sentOrigin, origin, StringComparison.Ordinal))
        {
            await RefuseAsync(response, StatusCodes.Status403Forbidden, "forbidden-origin");
            return;
        }

        if (!HasToken(request))
        {
            await RefuseAsync(response, StatusCodes.Status401Unauthorized, "unauthorized");
            return;
        }

        switch (path)
        {
            case ChannelPath:
                await ChannelAsync(context);
                break;
            case SendPath:
                await SendAsync(context);
                break;
            case SessionPath when _options.RunId is not null && _stopping.IsCancellationRequested:
                // A run that is stopping is not one a page can join: it waits for the next one.
                await RefuseAsync(response, StatusCodes.Status503ServiceUnavailable, "stopping");
                break;
            case SessionPath when _options.RunId is { } run:
                // A page that lost its channel asks whether this is a new run of its backend (src/Spar.Client/src/restart.ts).
                response.ContentType = "text/plain; charset=utf-8";
                await response.WriteAsync(run, context.RequestAborted);
                break;
            default:
                await RefuseAsync(response, StatusCodes.Status404NotFound, "not-found");
                break;
        }
    }

    private bool HasToken(HttpRequest request)
    {
        const string Scheme = "Bearer ";
        var header = request.Headers.Authorization.ToString();
        if (!header.StartsWith(Scheme, StringComparison.Ordinal))
        {
            return false;
        }

        Span<byte> sent = stackalloc byte[64];
        var decoded = Base64Url.DecodeFromChars(header.AsSpan(Scheme.Length), sent, out _, out var written);
        return decoded == OperationStatus.Done && CryptographicOperations.FixedTimeEquals(sent[..written], _token);
    }

    /// <summary>
    /// <c>POST /_spar/channel</c>: the body is the page's <c>hello</c>. The response stays open for the document's life
    /// and carries every host message, one per line, starting with <c>open</c>. Closing it closes the document.
    /// </summary>
    private async Task ChannelAsync(HttpContext context)
    {
        var response = context.Response;
        var hello = await ReadBodyAsync(context.Request, context.RequestAborted);
        if (!BridgeCodec.TryDecode(hello, out var decoded, out _) || decoded!.Type != BridgeMessageType.Hello)
        {
            await RefuseAsync(response, StatusCodes.Status400BadRequest, "invalid-request");
            return;
        }

        using var ended = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, _stopping.Token);
        var document = OpenDocument();
        try
        {
            response.ContentType = "text/plain; charset=utf-8";
            if (_options.RunId is { } run)
            {
                response.Headers[RunHeader] = run;
            }

            await response.StartAsync(ended.Token);

            document.Receive([hello]);
            await WriteChannelAsync(response, document.Outbox.Reader, ended.Token);
        }
        catch (OperationCanceledException) when (ended.IsCancellationRequested)
        {
            // The page went away (navigation, reload, closed tab) or the host is stopping.
        }
        finally
        {
            _documents.TryRemove(document.Id, out _);
            document.Session.Close();
            document.Session.Dispose();
        }
    }

    /// <summary><c>POST /_spar/send</c>: one or more page messages, one per line, for the document in <c>X-Spar-Doc</c>.</summary>
    private async Task SendAsync(HttpContext context)
    {
        var doc = context.Request.Headers[DocumentHeader].ToString();
        if (!_documents.TryGetValue(doc, out var document))
        {
            await RefuseAsync(context.Response, StatusCodes.Status404NotFound, "unknown-document");
            return;
        }

        var body = await ReadBodyAsync(context.Request, context.RequestAborted);

        // Split on LF only: a message never contains one, but its JSON may contain U+2028, which EnumerateLines splits on.
        document.Receive(body.Split('\n'));
        context.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    private async Task ReportAsync(HttpContext context)
    {
        var request = context.Request;
        if (request.ContentLength is > MaxReportBytes)
        {
            await RefuseAsync(context.Response, StatusCodes.Status413PayloadTooLarge, "too-large");
            return;
        }

        var report = await ReadReportAsync(request, context.RequestAborted);
        if (report is null)
        {
            await RefuseAsync(context.Response, StatusCodes.Status413PayloadTooLarge, "too-large");
            return;
        }

        if (_options.Reports is { } reports)
        {
            reports(report);
        }
        else
        {
            _options.Log?.Invoke($"csp violation: {Encoding.UTF8.GetString(report)}");
        }

        context.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    private Document OpenDocument()
    {
        var id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        var outbox = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
        var session = new BridgeSession(
            _options.Dispatcher,
            _options.Fingerprint,
            id,
            message => outbox.Writer.TryWrite(message),
            _options.IncludeDetails);

        session.Ready += () => DocumentReady?.Invoke(id);
        session.Dropped += reason => _options.Log?.Invoke($"document {id}: dropped {reason}");

        var document = new Document(id, session, outbox);
        _documents[id] = document;
        return document;
    }

    /// <summary>Writes what is queued, then flushes once; the flush takes the channel's token, never a command's.</summary>
    private static async Task WriteChannelAsync(HttpResponse response, ChannelReader<string> outbox, CancellationToken cancellationToken)
    {
        var writer = response.BodyWriter;
        while (await outbox.WaitToReadAsync(cancellationToken))
        {
            while (outbox.TryRead(out var message))
            {
                var length = Encoding.UTF8.GetByteCount(message);
                var span = writer.GetSpan(length + 1);
                Encoding.UTF8.GetBytes(message, span);
                span[length] = (byte)'\n';
                writer.Advance(length + 1);
            }

            await writer.FlushAsync(cancellationToken);
        }
    }

    /// <summary>
    /// The development server's own routes under <c>/_spar/dev-server/</c>: the error page's style and script, which need
    /// nothing, and its Retry, a POST that must come from this origin (a page on another site could otherwise post it,
    /// since its address passes the Host check). Neither needs the token: the error page has no SDK.
    /// </summary>
    private async Task DevServerRouteAsync(HttpContext context, string origin)
    {
        if (HttpMethods.IsPost(context.Request.Method)
            && !string.Equals(context.Request.Headers.Origin.ToString(), origin, StringComparison.Ordinal))
        {
            await RefuseAsync(context.Response, StatusCodes.Status403Forbidden, "forbidden-origin");
            return;
        }

        await ProxyAsync(context, origin);
    }

    /// <summary>
    /// A request for the page in a Debug build: answered by <see cref="DevServerProxy"/>, which fetches it from Vite on the
    /// thread pool and puts the host's own policy on the answer, with each document's fresh nonce.
    /// </summary>
    private async Task ProxyAsync(HttpContext context, string origin)
    {
        var request = context.Request;
        var response = context.Response;

        // The address as the browser sent it, still percent-encoded, as the desktop hosts pass it.
        var target = context.Features.Get<IHttpRequestFeature>()?.RawTarget ?? "";
        if (!target.StartsWith('/'))
        {
            await RefuseAsync(response, StatusCodes.Status400BadRequest, "invalid-request");
            return;
        }

        var headers = new List<KeyValuePair<string, string>>(DevServerProxy.ForwardedHeaders.Count);
        foreach (var name in DevServerProxy.ForwardedHeaders)
        {
            if (request.Headers.TryGetValue(name, out var value) && value.Count > 0)
            {
                headers.Add(new(name, value.ToString()));
            }
        }

        var proxy = _proxies.GetOrAdd(
            origin,
            static (name, options) => new DevServerProxy(options.DevServer!, options.Security, name, options.HostLog!),
            _options);
        var answer = await proxy.AnswerAsync(target, request.Method, headers);

        response.StatusCode = answer.Status;
        response.ContentType = answer.ContentType;
        if (answer.Csp is { } csp)
        {
            response.Headers.ContentSecurityPolicy = csp;
        }

        if (answer.Allow is { } allow)
        {
            response.Headers.Allow = allow;
        }

        response.ContentLength = answer.Body.Length;
        if (!HttpMethods.IsHead(request.Method))
        {
            await response.Body.WriteAsync(answer.Body, context.RequestAborted);
        }
    }

    /// <summary>A CSP report's body, or null when it is longer than <see cref="MaxReportBytes"/>.</summary>
    private static async Task<byte[]?> ReadReportAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        while (await request.Body.ReadAsync(chunk, cancellationToken) is var read and > 0)
        {
            if (buffer.Length + read > MaxReportBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static async Task<string> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        // Invalid UTF-8 becomes U+FFFD, as the wire rules ask of lone surrogates.
        using var reader = new StreamReader(request.Body, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static async Task RefuseAsync(HttpResponse response, int status, string code)
    {
        response.StatusCode = status;
        response.ContentType = "text/plain; charset=utf-8";
        await response.WriteAsync(code);
    }

    /// <summary>One document: its bridge session and the queue its channel writes from.</summary>
    private sealed class Document(string id, BridgeSession session, Channel<string> outbox)
    {
        private readonly Lock _receiving = new();

        public string Id { get; } = id;

        public BridgeSession Session { get; } = session;

        public Channel<string> Outbox { get; } = outbox;

        /// <summary>Hands a batch to the session in order; two requests for one document never interleave.</summary>
        public void Receive(string[] messages)
        {
            lock (_receiving)
            {
                foreach (var message in messages)
                {
                    if (message.Length > 0)
                    {
                        Session.Receive(message);
                    }
                }
            }
        }
    }
}
