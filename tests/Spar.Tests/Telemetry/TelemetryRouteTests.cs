using System.Text;
using Spar.Hosting;
using Spar.Telemetry;

namespace Spar.Tests.Telemetry;

/// <summary>
/// The page's telemetry routes every host answers (decision "M2d's decide-first questions", item 4): what is refused
/// before a body is read, the size limit, the page's settings, and nothing at all without the layer or an endpoint.
/// </summary>
public sealed class TelemetryRouteTests
{
    private const string Protobuf = "application/x-protobuf";

    [Fact]
    public void Without_the_layer_traces_are_a_404_and_the_settings_a_204()
    {
        Assert.Equal(404, TelemetryRoute.None.Check(TelemetryRoute.TracesPath, "POST", Protobuf)!.Value.Status);
        Assert.Equal(404, TelemetryRoute.None.Check(TelemetryRoute.ConfigPath, "GET", null)!.Value.Status);

        // No failed request in the page's console while telemetry is off: loopback's page asks on every load.
        Assert.Equal(204, TelemetryRoute.None.Check(TelemetryRoute.ConfigPath, "POST", null)!.Value.Status);
        Assert.Null(TelemetryRoute.None.PageScript());
    }

    [Fact]
    public void With_the_layer_but_no_endpoint_traces_are_a_404_and_the_settings_a_204()
    {
        var route = Route(new FakeTelemetryRelay { Enabled = false });

        Assert.Equal(404, route.Check(TelemetryRoute.TracesPath, "POST", Protobuf)!.Value.Status);
        Assert.Equal(204, route.Check(TelemetryRoute.ConfigPath, "POST", null)!.Value.Status);
        Assert.Null(route.PageScript());
    }

    [Fact]
    public void Other_paths_under_the_prefix_are_404_and_other_methods_405()
    {
        var route = Route(new FakeTelemetryRelay());

        Assert.True(TelemetryRoute.Matches("/_spar/otlp/v1/metrics"));
        Assert.Equal(404, route.Check("/_spar/otlp/v1/metrics", "POST", Protobuf)!.Value.Status);
        Assert.Equal(404, route.Check("/_spar/otlp/v1/logs", "POST", Protobuf)!.Value.Status);
        Assert.Equal(405, route.Check(TelemetryRoute.TracesPath, "GET", null)!.Value.Status);
        Assert.Equal("POST", route.Check(TelemetryRoute.TracesPath, "GET", null)!.Value.Allow);
        Assert.Equal(405, route.Check(TelemetryRoute.ConfigPath, "GET", null)!.Value.Status);
    }

    [Theory]
    [InlineData("application/x-protobuf")]
    [InlineData("application/json")]
    [InlineData("application/json; charset=utf-8")]
    public void A_traces_request_in_protobuf_or_json_goes_on_to_be_read(string contentType)
    {
        Assert.Null(Route(new FakeTelemetryRelay()).Check(TelemetryRoute.TracesPath, "POST", contentType));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("text/plain")]
    [InlineData("application/grpc")]
    public void Any_other_body_is_a_415(string? contentType)
    {
        Assert.Equal(415, Route(new FakeTelemetryRelay()).Check(TelemetryRoute.TracesPath, "POST", contentType)!.Value.Status);
    }

    [Fact]
    public void Past_twenty_requests_at_once_the_page_gets_429()
    {
        var route = Route(new FakeTelemetryRelay());

        var answers = Enumerable.Range(0, TelemetryRoute.PerSecond + 1)
            .Select(_ => route.Check(TelemetryRoute.TracesPath, "POST", Protobuf)?.Status)
            .ToList();

        Assert.All(answers.Take(TelemetryRoute.PerSecond), Assert.Null);
        Assert.Equal(429, answers[^1]);
    }

    [Fact]
    public void The_config_route_answers_the_page_settings_as_json()
    {
        var answer = Route(new FakeTelemetryRelay()).Check(TelemetryRoute.ConfigPath, "POST", null)!.Value;

        Assert.Equal(200, answer.Status);
        Assert.Equal("application/json", answer.ContentType);
        Assert.Equal("{\"serviceName\":\"com.example.App-page\",\"delayMs\":5000}", Encoding.UTF8.GetString(answer.Body.Span));
    }

    [Fact]
    public void The_page_script_freezes_the_settings_and_escapes_the_name_for_a_script()
    {
        var script = Route(new FakeTelemetryRelay { PageServiceName = "a\"</script>-page" }).PageScript();

        Assert.Equal("globalThis.__sparTelemetry = Object.freeze({\"serviceName\":\"a\\u0022\\u003C/script\\u003E-page\",\"delayMs\":5000});", script);
    }

    [Fact]
    public async Task A_body_over_one_mebibyte_is_a_413_and_never_reaches_the_relay()
    {
        var relay = new FakeTelemetryRelay();
        var route = Route(relay);

        var answer = await route.ForwardAsync(new byte[TelemetryRoute.MaxBody + 1], Protobuf, TestContext.Current.CancellationToken);

        Assert.Equal(413, answer.Status);
        Assert.Equal(0, relay.Forwarded);
    }

    [Fact]
    public async Task A_body_within_the_limit_gets_the_relay_answer()
    {
        var relay = new FakeTelemetryRelay { Answer = new(503, "Service Unavailable") };

        var answer = await Route(relay).ForwardAsync(new byte[TelemetryRoute.MaxBody], Protobuf, TestContext.Current.CancellationToken);

        Assert.Equal(503, answer.Status);
        Assert.Equal(1, relay.Forwarded);
    }

    private static TelemetryRoute Route(ITelemetryRelay relay)
    {
        var builder = new SparBuilder([], "com.example.App") { TelemetryRelay = relay };
        return TelemetryRoute.For(builder);
    }
}
