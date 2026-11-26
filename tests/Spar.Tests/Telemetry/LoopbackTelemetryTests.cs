using System.Net;
using System.Net.Http.Headers;
using Spar.Hosting;
using Spar.Loopback;
using Spar.Telemetry;

namespace Spar.Tests.Telemetry;

/// <summary>
/// The loopback host's telemetry routes over real HTTP: the page's settings and its spans behind the session's token,
/// like every bridge request, since loopback's page has no host-injected script.
/// </summary>
public sealed class LoopbackTelemetryTests : IAsyncLifetime
{
    private readonly FakeTelemetryRelay _relay = new();
    private TestBackend _backend = null!;
    private LoopbackHost _host = null!;
    private HttpClient _http = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _backend = TestBackend.Create();
        _host = await LoopbackHost.StartAsync(
            new LoopbackOptions
            {
                Dispatcher = _backend.Dispatcher,
                Fingerprint = TestBackend.Fingerprint,
                Page = new LoopbackAssets(),
                Telemetry = TelemetryRoute.For(new SparBuilder([], "com.example.App") { TelemetryRelay = _relay }),
            },
            Token);
        _http = new HttpClient { BaseAddress = _host.Origin };
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _host.DisposeAsync();
        await _backend.DisposeAsync();
    }

    [Theory]
    [InlineData("/_spar/otlp/config")]
    [InlineData("/_spar/otlp/v1/traces")]
    public async Task Without_the_token_the_routes_are_401(string path)
    {
        using var response = await _http.PostAsync(path, Protobuf([1, 2, 3]), Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, _relay.Forwarded);
    }

    [Fact]
    public async Task With_the_token_the_page_gets_its_settings()
    {
        using var response = await SendAsync("/_spar/otlp/config", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"serviceName\":\"com.example.App-page\",\"delayMs\":5000}", await response.Content.ReadAsStringAsync(Token));
    }

    [Fact]
    public async Task With_the_token_the_page_spans_reach_the_relay_unchanged()
    {
        using var response = await SendAsync("/_spar/otlp/v1/traces", Protobuf([1, 2, 3]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([1, 2, 3], _relay.Last.Body);
        Assert.Equal("application/x-protobuf", _relay.Last.ContentType);
    }

    [Fact]
    public async Task A_body_over_the_limit_is_413()
    {
        using var response = await SendAsync("/_spar/otlp/v1/traces", Protobuf(new byte[TelemetryRoute.MaxBody + 1]));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(0, _relay.Forwarded);
    }

    [Fact]
    public async Task A_get_is_405_naming_post()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/_spar/otlp/config");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _host.Token);
        using var response = await _http.SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal("POST", string.Join(",", response.Content.Headers.Allow));
    }

    private static ByteArrayContent Protobuf(byte[] body)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
        return content;
    }

    private async Task<HttpResponseMessage> SendAsync(string path, HttpContent? content)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _host.Token);
        return await _http.SendAsync(request, Token);
    }
}
