using System.Text;
using System.Text.Json;
using Spar.Testing;

namespace WireShapes.Tests;

/// <summary>
/// The rest of the wire-rules table (plan section 2) at run time, through real dispatch: the smaller integers,
/// <c>ulong</c>, <c>float</c>, <c>decimal</c>, <c>char</c>, the dates and times, and <c>byte[]</c>; each value's one
/// spelling, and every malformed one refused before the command runs.
/// </summary>
public sealed class KindsTests : IAsyncLifetime
{
    private const string Valid =
        "{\"b\":255,\"sb\":-128,\"s\":-32768,\"us\":65535,\"ui\":4294967295,\"ul\":\"18446744073709551615\",\"f\":0.1," +
        "\"m\":\"-12.50\",\"c\":\"\\u00e9\",\"day\":\"2026-02-28\",\"time\":\"08:30:00.25\",\"span\":\"-1.02:30:00\"," +
        "\"data\":\"AQID/w==\",\"maybe\":null,\"ids\":[\"0\",null],\"price\":null}";

    private SparTestApp _app = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _app = await SparTestApp.StartAsync<Program>(cancellationToken: Token);

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    [Fact]
    public async Task Every_kind_round_trips_through_the_typed_client()
    {
        var kinds = new Kinds(
            byte.MaxValue, sbyte.MinValue, short.MinValue, ushort.MaxValue, uint.MaxValue, ulong.MaxValue, 0.1f,
            79228162514264337593543950335m, '\ud83d', new DateOnly(1, 1, 1), new TimeOnly(23, 59, 59, 999, 999),
            TimeSpan.MinValue, [0, 1, 254, 255], [], [ulong.MaxValue, null], -0.000001m);

        var echoed = await new KindsClient(_app).EchoAsync(kinds, Token);

        Assert.Equal(
            (kinds.B, kinds.Sb, kinds.S, kinds.Us, kinds.Ui, kinds.Ul, kinds.F, kinds.M, kinds.Day, kinds.Time, kinds.Span, kinds.Price),
            (echoed.B, echoed.Sb, echoed.S, echoed.Us, echoed.Ui, echoed.Ul, echoed.F, echoed.M, echoed.Day, echoed.Time, echoed.Span, echoed.Price));
        Assert.Equal('\ufffd', echoed.C); // a lone surrogate becomes U+FFFD, as in any string
        Assert.Equal(kinds.Data, echoed.Data);
        Assert.Equal(kinds.Maybe, echoed.Maybe);
        Assert.Equal(kinds.Ids, echoed.Ids);
    }

    [Fact]
    public async Task Each_kind_has_one_spelling_on_the_wire()
    {
        var output = await EchoAsync(Valid.Replace("\"maybe\":null", "\"maybe\":\"\"", StringComparison.Ordinal)
            .Replace("\"price\":null", "\"price\":\"0.000\"", StringComparison.Ordinal));

        Assert.Equal(
            Valid.Replace("\"maybe\":null", "\"maybe\":\"\"", StringComparison.Ordinal)
                .Replace("\"price\":null", "\"price\":\"0.000\"", StringComparison.Ordinal)
                .Replace("\\u00e9", "\\u00E9", StringComparison.Ordinal),
            output);
    }

    [Fact]
    public async Task A_decimal_keeps_its_scale_and_a_time_drops_trailing_zeros()
    {
        var output = await EchoAsync(Valid.Replace("\"-12.50\"", "\"1.000\"", StringComparison.Ordinal)
            .Replace("\"08:30:00.25\"", "\"08:30:00.2500000\"", StringComparison.Ordinal));

        using var json = JsonDocument.Parse(output);
        Assert.Equal("1.000", json.RootElement.GetProperty("m").GetString());
        Assert.Equal("08:30:00.25", json.RootElement.GetProperty("time").GetString());
    }

    [Theory]
    [InlineData("\"b\":255", "\"b\":256")]
    [InlineData("\"b\":255", "\"b\":-1")]
    [InlineData("\"sb\":-128", "\"sb\":1.5")]
    [InlineData("\"s\":-32768", "\"s\":\"1\"")]
    [InlineData("\"us\":65535", "\"us\":65536")]
    [InlineData("\"ui\":4294967295", "\"ui\":4294967296")]
    [InlineData("\"ul\":\"18446744073709551615\"", "\"ul\":\"18446744073709551616\"")]
    [InlineData("\"ul\":\"18446744073709551615\"", "\"ul\":\"-1\"")]
    [InlineData("\"ul\":\"18446744073709551615\"", "\"ul\":\"01\"")]
    [InlineData("\"ul\":\"18446744073709551615\"", "\"ul\":1")]
    [InlineData("\"f\":0.1", "\"f\":1e39")]
    [InlineData("\"m\":\"-12.50\"", "\"m\":\"+12.50\"")]
    [InlineData("\"m\":\"-12.50\"", "\"m\":\"012.5\"")]
    [InlineData("\"m\":\"-12.50\"", "\"m\":\"1e3\"")]
    [InlineData("\"m\":\"-12.50\"", "\"m\":\"-0\"")]
    [InlineData("\"m\":\"-12.50\"", "\"m\":12.5")]
    [InlineData("\"m\":\"-12.50\"", "\"m\":\"0.123456789012345678901234567890123\"")]
    [InlineData("\"m\":\"-12.50\"", "\"m\":\"79228162514264337593543950336\"")]
    [InlineData("\"c\":\"\\u00e9\"", "\"c\":\"ab\"")]
    [InlineData("\"c\":\"\\u00e9\"", "\"c\":\"\"")]
    [InlineData("\"day\":\"2026-02-28\"", "\"day\":\"2026-02-29\"")]
    [InlineData("\"day\":\"2026-02-28\"", "\"day\":\"2026-2-28\"")]
    [InlineData("\"time\":\"08:30:00.25\"", "\"time\":\"8:30:00\"")]
    [InlineData("\"time\":\"08:30:00.25\"", "\"time\":\"08:30\"")]
    [InlineData("\"span\":\"-1.02:30:00\"", "\"span\":\"1:2:3\"")]
    [InlineData("\"span\":\"-1.02:30:00\"", "\"span\":\"26:30:00\"")]
    [InlineData("\"data\":\"AQID/w==\"", "\"data\":\"AQID/w\"")]
    [InlineData("\"data\":\"AQID/w==\"", "\"data\":\"AQ ID/w==\"")]
    [InlineData("\"data\":\"AQID/w==\"", "\"data\":\"AQID\\n/w==\"")]
    [InlineData("\"data\":\"AQID/w==\"", "\"data\":\"AQI\\u0044/w==\"")]
    [InlineData("\"data\":\"AQID/w==\"", "\"data\":[1,2,3]")]
    public async Task A_malformed_value_is_refused_before_the_command_runs(string valid, string malformed)
    {
        var json = Valid.Replace(valid, malformed, StringComparison.Ordinal);
        Assert.NotEqual(Valid, json);

        var outcome = await _app.InvokeAsync("kinds", "echo", Encoding.UTF8.GetBytes(json), Token);

        Assert.Equal(("rejected", "invalid-request"), (outcome.Failure?.Kind, outcome.Failure?.Code));
    }

    [Fact]
    public async Task A_byte_array_is_one_base64_string_as_a_request()
    {
        var length = await new KindsClient(_app).LengthAsync([1, 2, 3], Token);
        var asList = await _app.InvokeAsync("kinds", "length", "[1,2,3]"u8.ToArray(), Token);

        Assert.Equal(3, length);
        Assert.Equal(("rejected", "invalid-request"), (asList.Failure?.Kind, asList.Failure?.Code));
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public async Task A_float_the_wire_cannot_carry_is_a_writer_failure(string text)
    {
        var outcome = await _app.InvokeAsync("kinds", "single", Encoding.UTF8.GetBytes($"\"{text}\""), Token);

        Assert.Equal(("response", "serialization", "completed"), (outcome.Failure?.Kind, outcome.Failure?.Code, outcome.Failure?.Execution));
    }

    private async Task<string> EchoAsync(string json)
    {
        var outcome = await _app.InvokeAsync("kinds", "echo", Encoding.UTF8.GetBytes(json), Token);
        Assert.True(outcome.Failure is null, $"{outcome.Failure}");
        return Encoding.UTF8.GetString(outcome.Payload.Span);
    }
}
