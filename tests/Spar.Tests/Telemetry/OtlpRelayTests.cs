using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using Spar.OpenTelemetry;

namespace Spar.Tests.Telemetry;

/// <summary>
/// The relay of the page's spans (decision "M2d's decide-first questions", item 4) against a fake endpoint: the bytes
/// unchanged with the configured headers, OTLP/HTTP's path, gRPC's framing and status, and the pause after a failure.
/// </summary>
public sealed class OtlpRelayTests
{
    private static readonly byte[] Spans = [0x0a, 0x03, 0x01, 0x02, 0x03];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("http://127.0.0.1:4318", "http://127.0.0.1:4318/v1/traces")]
    [InlineData("http://collector:4318/otlp/", "http://collector:4318/otlp/v1/traces")]
    public async Task Over_http_protobuf_the_bytes_go_to_v1_traces_with_the_configured_headers(string endpoint, string expected)
    {
        var handler = new FakeHandler();
        using var relay = Relay(endpoint, OtlpSettings.HttpProtobuf, handler, "x-api-key=secret%20value,tenant=a");

        var answer = await relay.ForwardTracesAsync(Spans, "application/x-protobuf", Token);

        Assert.Equal(200, answer.Status);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(expected, request.Uri);
        Assert.Equal("application/x-protobuf", request.ContentType);
        Assert.Equal(Spans, request.Body);
        Assert.Equal("secret value", request.Headers["x-api-key"]);
        Assert.Equal("a", request.Headers["tenant"]);
    }

    [Fact]
    public async Task Over_http_json_a_json_body_goes_as_json()
    {
        var handler = new FakeHandler();
        using var relay = Relay("http://127.0.0.1:4318", OtlpSettings.HttpJson, handler);

        await relay.ForwardTracesAsync("{}"u8.ToArray(), "application/json", Token);

        Assert.Equal("application/json", Assert.Single(handler.Requests).ContentType);
    }

    [Fact]
    public async Task Over_grpc_the_bytes_are_one_uncompressed_message_to_the_trace_service()
    {
        var handler = new FakeHandler();
        using var relay = Relay("http://127.0.0.1:4317", OtlpSettings.Grpc, handler);

        var answer = await relay.ForwardTracesAsync(Spans, "application/x-protobuf", Token);

        Assert.Equal(200, answer.Status);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("http://127.0.0.1:4317/opentelemetry.proto.collector.trace.v1.TraceService/Export", request.Uri);
        Assert.Equal(HttpVersion.Version20, request.Version);
        Assert.Equal("application/grpc", request.ContentType);
        Assert.Equal("trailers", request.Headers["te"]);
        Assert.Equal(0, request.Body[0]);
        Assert.Equal((uint)Spans.Length, BinaryPrimitives.ReadUInt32BigEndian(request.Body.AsSpan(1, 4)));
        Assert.Equal(Spans, request.Body[5..]);
    }

    [Theory]
    [InlineData("16", 401)]
    [InlineData("7", 403)]
    [InlineData("14", 503)]
    [InlineData("8", 503)]
    [InlineData("3", 400)]
    [InlineData("13", 502)]
    public async Task A_grpc_status_becomes_the_page_http_status(string grpcStatus, int expected)
    {
        var handler = new FakeHandler { GrpcStatus = grpcStatus };
        using var relay = Relay("http://127.0.0.1:4317", OtlpSettings.Grpc, handler);

        var answer = await relay.ForwardTracesAsync(Spans, "application/x-protobuf", Token);

        Assert.Equal(expected, answer.Status);
    }

    [Fact]
    public async Task Json_cannot_go_over_grpc_and_is_refused_without_a_request()
    {
        var handler = new FakeHandler();
        using var relay = Relay("http://127.0.0.1:4317", OtlpSettings.Grpc, handler);

        var answer = await relay.ForwardTracesAsync("{}"u8.ToArray(), "application/json", Token);

        Assert.Equal(415, answer.Status);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task After_the_endpoint_cannot_be_reached_the_relay_answers_503_at_once_without_retrying()
    {
        var handler = new FakeHandler { Fail = true };
        using var relay = Relay("http://127.0.0.1:4318", OtlpSettings.HttpProtobuf, handler);

        var first = await relay.ForwardTracesAsync(Spans, "application/x-protobuf", Token);
        handler.Fail = false;
        var second = await relay.ForwardTracesAsync(Spans, "application/x-protobuf", Token);

        Assert.Equal(503, first.Status);
        Assert.Equal(503, second.Status);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task The_endpoint_answer_reaches_the_page_unchanged_over_http()
    {
        var handler = new FakeHandler { Status = HttpStatusCode.TooManyRequests };
        using var relay = Relay("http://127.0.0.1:4318", OtlpSettings.HttpProtobuf, handler);

        Assert.Equal(429, (await relay.ForwardTracesAsync(Spans, "application/x-protobuf", Token)).Status);
    }

    [Fact]
    public void The_settings_follow_the_standard_variables()
    {
        var variables = new Dictionary<string, string>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = " http://127.0.0.1:4318 ",
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "HTTP/PROTOBUF",
            ["OTEL_RESOURCE_ATTRIBUTES"] = "deployment.environment=dev,service.name=notes",
            ["OTEL_BSP_SCHEDULE_DELAY"] = "250",
        };

        var settings = OtlpSettings.Read(name => variables.GetValueOrDefault(name), "com.example.App");

        Assert.Equal(new Uri("http://127.0.0.1:4318"), settings.Endpoint);
        Assert.Equal(OtlpSettings.HttpProtobuf, settings.Protocol);
        Assert.Equal("notes", settings.ServiceName);
        Assert.Equal(250, settings.DelayMilliseconds);
        Assert.Equal("notes-page", new OtlpRelay(settings).PageServiceName);
    }

    [Fact]
    public void Without_an_endpoint_the_layer_is_off_with_grpc_the_app_id_and_five_seconds()
    {
        var settings = OtlpSettings.Read(_ => null, "com.example.App");

        Assert.False(settings.Enabled);
        Assert.Equal(OtlpSettings.Grpc, settings.Protocol);
        Assert.Equal("com.example.App", settings.ServiceName);
        Assert.Equal(5000, settings.DelayMilliseconds);
    }

    [Fact]
    public void A_service_name_of_its_own_wins_over_the_resource_attribute()
    {
        var variables = new Dictionary<string, string>
        {
            ["OTEL_SERVICE_NAME"] = "notebook",
            ["OTEL_RESOURCE_ATTRIBUTES"] = "service.name=other",
        };

        Assert.Equal("notebook", OtlpSettings.Read(name => variables.GetValueOrDefault(name), "id").ServiceName);
    }

    [Fact]
    public void Malformed_header_pairs_are_left_out()
    {
        var pairs = OtlpSettings.ParsePairs("=nokey,novalue,a=1,, b = 2 ");

        Assert.Equal([new("a", "1"), new("b", "2")], pairs);
    }

    private static OtlpRelay Relay(string endpoint, string protocol, FakeHandler handler, string? headers = null) =>
        new(new OtlpSettings(new Uri(endpoint), protocol, OtlpSettings.ParsePairs(headers), "com.example.App", 5000), handler);

    private sealed record SeenRequest(HttpMethod Method, string Uri, Version Version, string? ContentType, byte[] Body, Dictionary<string, string> Headers);

    private sealed class FakeHandler : HttpMessageHandler
    {
        public List<SeenRequest> Requests { get; } = [];

        public bool Fail { get; set; }

        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

        public string? GrpcStatus { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
            Requests.Add(new(request.Method, request.RequestUri!.ToString(), request.Version, request.Content?.Headers.ContentType?.MediaType, body, headers));
            if (Fail)
            {
                throw new HttpRequestException("Connection refused.");
            }

            var response = new HttpResponseMessage(Status) { Content = new ByteArrayContent([]) };
            if (GrpcStatus is not null)
            {
                response.TrailingHeaders.TryAddWithoutValidation("grpc-status", GrpcStatus);
            }

            return response;
        }
    }
}
