using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OpenTelemetry;
using Spike;

namespace React.Telemetry;

/// <summary>
/// Relays the page's OTLP/HTTP export requests to the collector the backend exports to, reading the standard
/// OTEL_EXPORTER_OTLP_* variables (endpoint, protocol, headers, timeout; signal-specific ones first). The request body
/// is forwarded byte for byte. With the gRPC protocol it is framed as one gRPC message, which carries the same
/// protobuf; JSON bodies can only go to an http/protobuf or http/json collector.
/// The page never sees the endpoint or the headers (an API key, for instance), and needs no CORS.
/// </summary>
internal sealed class OtlpRelay : ISparTelemetryRelay
{
    private static readonly byte[] Empty = [];

    // One client for the app's lifetime; its activities are suppressed below, so relaying never creates spans.
    private readonly HttpClient _http = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private readonly Dictionary<string, Target> _targets = new(StringComparer.Ordinal);

    // After a failed connection, answer 503 at once for a while instead of waiting for the collector again
    // (a refused connection takes about 2 s on Windows); the page's exporter retries a bounded number of times.
    private static readonly TimeSpan Backoff = TimeSpan.FromSeconds(5);
    private long _unavailableUntil;

    public OtlpRelay(string pageServiceName)
    {
        // The page batches like the backend: OTEL_BSP_SCHEDULE_DELAY (Aspire sets it), else the SDK default of 5 s.
        // SPAR_TELEMETRY_PAGE_DELAY_MS overrides it for the spike's measurements.
        var delay = int.TryParse(Environment.GetEnvironmentVariable("SPAR_TELEMETRY_PAGE_DELAY_MS"), NumberStyles.None, CultureInfo.InvariantCulture, out var ms) ? ms
            : int.TryParse(Environment.GetEnvironmentVariable("OTEL_BSP_SCHEDULE_DELAY"), NumberStyles.None, CultureInfo.InvariantCulture, out var bsp) ? bsp
            : 5000;
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("serviceName"u8, pageServiceName);
            writer.WriteNumber("scheduledDelayMs"u8, delay);
            writer.WriteBoolean("logs"u8, true);
            writer.WriteEndObject();
        }

        PageConfig = buffer.ToArray();
        foreach (var signal in new[] { "traces", "logs", "metrics" })
        {
            _targets[signal] = Target.Read(signal);
        }
    }

    public ReadOnlyMemory<byte> PageConfig { get; }

    public async Task<SparRelayResponse> ForwardAsync(string signal, ReadOnlyMemory<byte> body, string? contentType, CancellationToken cancellationToken)
    {
        var target = _targets[signal];
        if (target.Grpc && contentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) == true)
        {
            return new(415, Encoding.UTF8.GetBytes("{\"error\":\"json-over-grpc\"}"), "application/json");
        }

        if (Environment.TickCount64 < Interlocked.Read(ref _unavailableUntil))
        {
            return new(503, Empty, "application/x-protobuf");
        }

        // No span for the relay itself (it would be exported, relayed and traced again), and no ambient parent.
        using var suppress = SuppressInstrumentationScope.Begin();
        var ambient = Activity.Current;
        Activity.Current = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(target.Timeout);
            using var request = target.Grpc ? GrpcRequest(target, body) : HttpRequest(target, body, contentType!);
            foreach (var (name, value) in target.Headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token).ConfigureAwait(false);
            var bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
            if (!target.Grpc)
            {
                // Pass the collector's answer through: the page's exporter retries 429/502/503/504 itself.
                return new((int)response.StatusCode, bytes, response.Content.Headers.ContentType?.ToString() ?? "application/x-protobuf");
            }

            var grpcStatus = GrpcStatus(response);
            return response.StatusCode == HttpStatusCode.OK && grpcStatus == "0"
                ? new(200, Empty, "application/x-protobuf")
                : new(grpcStatus is "8" or "14" ? 503 : 502, Empty, "application/x-protobuf");
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            // The collector is down or slow: the page may retry; the backend never replays anything itself.
            Interlocked.Exchange(ref _unavailableUntil, Environment.TickCount64 + (long)Backoff.TotalMilliseconds);
            Console.WriteLine($"SPAR_OTLP_RELAY_FAILED {signal} {exception.GetType().Name}: {exception.Message}");
            return new(503, Empty, "application/x-protobuf");
        }
        finally
        {
            Activity.Current = ambient;
        }
    }

    private static HttpRequestMessage HttpRequest(Target target, ReadOnlyMemory<byte> body, string contentType)
    {
        var content = new ReadOnlyMemoryContent(body);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return new HttpRequestMessage(HttpMethod.Post, target.Uri) { Content = content };
    }

    /// <summary>One length-prefixed gRPC message (no compression) over HTTP/2.</summary>
    private static HttpRequestMessage GrpcRequest(Target target, ReadOnlyMemory<byte> body)
    {
        var framed = new byte[5 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(framed.AsSpan(1), (uint)body.Length);
        body.Span.CopyTo(framed.AsSpan(5));
        var content = new ByteArrayContent(framed);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
        var request = new HttpRequestMessage(HttpMethod.Post, target.Uri)
        {
            Content = content,
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
        request.Headers.TryAddWithoutValidation("te", "trailers");
        return request;
    }

    private static string? GrpcStatus(HttpResponseMessage response) =>
        response.TrailingHeaders.TryGetValues("grpc-status", out var trailer) ? trailer.FirstOrDefault()
        : response.Headers.TryGetValues("grpc-status", out var header) ? header.FirstOrDefault()
        : null;

    private sealed record Target(Uri Uri, bool Grpc, IReadOnlyList<(string Name, string Value)> Headers, TimeSpan Timeout)
    {
        public static Target Read(string signal)
        {
            var upper = signal.ToUpperInvariant();
            string? Variable(string name) =>
                Environment.GetEnvironmentVariable($"OTEL_EXPORTER_OTLP_{upper}_{name}") is { Length: > 0 } specific ? specific
                : Environment.GetEnvironmentVariable($"OTEL_EXPORTER_OTLP_{name}") is { Length: > 0 } general ? general
                : null;

            // The .NET exporter's default protocol is gRPC; the relay follows it so one setting serves both.
            var grpc = (Variable("PROTOCOL") ?? "grpc") == "grpc";
            var baseEndpoint = (Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") ?? (grpc ? "http://localhost:4317" : "http://localhost:4318")).TrimEnd('/');
            var service = signal switch
            {
                "traces" => "trace.v1.TraceService",
                "logs" => "logs.v1.LogsService",
                _ => "metrics.v1.MetricsService",
            };
            var uri = Environment.GetEnvironmentVariable($"OTEL_EXPORTER_OTLP_{upper}_ENDPOINT") is { Length: > 0 } exact ? new Uri(exact)
                : grpc ? new Uri($"{baseEndpoint}/opentelemetry.proto.collector.{service}/Export")
                : new Uri($"{baseEndpoint}/v1/{signal}");

            var headers = new List<(string, string)>();
            foreach (var source in new[] { Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_HEADERS"), Environment.GetEnvironmentVariable($"OTEL_EXPORTER_OTLP_{upper}_HEADERS") })
            {
                foreach (var pair in (source ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var equals = pair.IndexOf('=', StringComparison.Ordinal);
                    if (equals > 0)
                    {
                        headers.Add((pair[..equals].Trim(), Uri.UnescapeDataString(pair[(equals + 1)..].Trim())));
                    }
                }
            }

            var timeout = int.TryParse(Variable("TIMEOUT"), NumberStyles.None, CultureInfo.InvariantCulture, out var ms) ? ms : 10_000;
            return new Target(uri, grpc, headers, TimeSpan.FromMilliseconds(timeout));
        }
    }
}
