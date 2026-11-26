using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Spar.Telemetry;

namespace Spar.OpenTelemetry;

/// <summary>
/// The page's spans to the app's OTLP endpoint (section 6b; decisions "Browser telemetry through the host: confirmed"
/// and "M2d's decide-first questions", item 4): each request's bytes go unchanged, with the configured headers, which the
/// page never sees. Over <c>http/protobuf</c> or <c>http/json</c> to <c>{endpoint}/v1/traces</c>; over <c>grpc</c>, the
/// same protobuf framed as one gRPC message to the trace service's <c>Export</c> over HTTP/2 (JSON is refused there,
/// 415). After a failure to reach the endpoint the relay answers 503 at once for <see cref="Pause"/>; it never replays a
/// request, so retries stay with the page's bounded exporter.
/// </summary>
internal sealed class OtlpRelay : ITelemetryRelay, IDisposable
{
    /// <summary>How long the relay answers 503 at once after it could not reach the endpoint.</summary>
    public static readonly TimeSpan Pause = TimeSpan.FromSeconds(5);

    private const string GrpcPath = "opentelemetry.proto.collector.trace.v1.TraceService/Export";

    private readonly OtlpSettings _settings;
    private readonly HttpClient _client;
    private long _failedAt;

    public OtlpRelay(OtlpSettings settings, HttpMessageHandler? handler = null)
    {
        _settings = settings;
        _client = new HttpClient(handler ?? new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(5) }) { Timeout = TimeSpan.FromSeconds(10) };
    }

    public bool Enabled => _settings.Enabled;

    public string PageServiceName => _settings.ServiceName + "-page";

    public int PageDelayMilliseconds => _settings.DelayMilliseconds;

    public async Task<TelemetryRelayAnswer> ForwardTracesAsync(ReadOnlyMemory<byte> body, string contentType, CancellationToken cancellationToken)
    {
        if (_settings.Endpoint is not { } endpoint)
        {
            return new(404, "Not Found");
        }

        var failedAt = Interlocked.Read(ref _failedAt);
        if (failedAt != 0 && Stopwatch.GetElapsedTime(failedAt) < Pause)
        {
            return new(503, "Service Unavailable");
        }

        var json = contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase);
        using var request = _settings.Protocol == OtlpSettings.Grpc
            ? json ? null : GrpcRequest(endpoint, body)
            : HttpRequest(endpoint, body, json);
        if (request is null)
        {
            return new(415, "Unsupported Media Type");
        }

        foreach (var (name, value) in _settings.Headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        try
        {
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            Interlocked.Exchange(ref _failedAt, 0);
            return _settings.Protocol == OtlpSettings.Grpc ? FromGrpc(response) : new((int)response.StatusCode, response.ReasonPhrase ?? "");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            Interlocked.Exchange(ref _failedAt, Stopwatch.GetTimestamp());
            return new(503, "Service Unavailable");
        }
    }

    public void Dispose() => _client.Dispose();

    private static HttpRequestMessage HttpRequest(Uri endpoint, ReadOnlyMemory<byte> body, bool json)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Append(endpoint, "v1/traces")) { Content = new ReadOnlyMemoryContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(json ? "application/json" : "application/x-protobuf");
        return request;
    }

    /// <summary>One gRPC message: a byte saying it is not compressed, its length as four big-endian bytes, the protobuf.</summary>
    private static HttpRequestMessage GrpcRequest(Uri endpoint, ReadOnlyMemory<byte> body)
    {
        var framed = new byte[5 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(framed.AsSpan(1), (uint)body.Length);
        body.Span.CopyTo(framed.AsSpan(5));
        var request = new HttpRequestMessage(HttpMethod.Post, Append(endpoint, GrpcPath))
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = new ByteArrayContent(framed),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
        request.Headers.TryAddWithoutValidation("te", "trailers");
        return request;
    }

    /// <summary>gRPC's status (in the trailers, or in the headers of an answer without a body) as the page's HTTP status.</summary>
    private static TelemetryRelayAnswer FromGrpc(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            return new((int)response.StatusCode, response.ReasonPhrase ?? "");
        }

        var status = Status(response.TrailingHeaders) ?? Status(response.Headers) ?? "0";
        return status switch
        {
            "0" => new(200, "OK"),
            "16" => new(401, "Unauthorized"),
            "7" => new(403, "Forbidden"),
            "8" or "14" => new(503, "Service Unavailable"),
            "3" => new(400, "Bad Request"),
            _ => new(502, "Bad Gateway"),
        };
    }

    private static string? Status(HttpHeaders headers) =>
        headers.TryGetValues("grpc-status", out var values) ? values.FirstOrDefault() : null;

    private static Uri Append(Uri endpoint, string path)
    {
        var text = endpoint.ToString();
        return new Uri(text.EndsWith('/') ? text + path : text + "/" + path);
    }
}
