using System.Text;
using System.Text.Json;
using Spar;
using Spar.Runtime;
using Spar.Testing;
using WireShapes;

namespace WireShapes.Tests;

/// <summary>
/// The generated serializers at run time, through real dispatch: the wire-rules table of plan section 2 and the
/// serializer requirements R0–R4 (decision "Serializer differential fuzzing").
/// </summary>
public sealed class WireShapesTests : IAsyncLifetime
{
    private const string Valid =
        "{\"text\":\"h\\u00e9llo \\u2713\",\"note\":null,\"flag\":true,\"count\":-3,\"big\":\"-9223372036854775808\",\"ratio\":0.25," +
        "\"key\":\"0f8fad5b-d9cb-469f-a165-70867728950e\",\"at\":\"2026-11-16T08:30:00+01:00\",\"maybe\":7,\"tags\":[\"a\"]," +
        "\"cells\":[null,\"x\"],\"items\":[{\"name\":\"i\"}],\"parent\":null}";

    private SparTestApp _app = null!;

    public async ValueTask InitializeAsync() => _app = await SparTestApp.StartAsync<Program>(cancellationToken: Token);

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    [Fact]
    public async Task Every_supported_type_round_trips()
    {
        var parent = new Shapes("p", null, false, 0, 0, 0, Guid.Empty, DateTimeOffset.UnixEpoch, null, [], [], [], null);
        var shapes = new Shapes(
            "héllo ✓", "note", true, int.MinValue, long.MaxValue, 1e-300, Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"),
            new DateTimeOffset(2026, 11, 16, 8, 30, 0, TimeSpan.FromHours(1)), 7, ["a", "b"], [null, "x"], [new Item("i")], parent);

        var echoed = await new EchoClient(_app).ShapesAsync(shapes, Token);

        Assert.Equal((shapes.Text, shapes.Note, shapes.Flag, shapes.Count, shapes.Big, shapes.Ratio, shapes.Key, shapes.At, shapes.Maybe),
            (echoed.Text, echoed.Note, echoed.Flag, echoed.Count, echoed.Big, echoed.Ratio, echoed.Key, echoed.At, echoed.Maybe));
        Assert.Equal(shapes.Tags, echoed.Tags);
        Assert.Equal(shapes.Cells, echoed.Cells);
        Assert.Equal(shapes.Items, echoed.Items);
        Assert.Equal("p", echoed.Parent!.Text);
    }

    [Fact]
    public async Task The_wire_form_follows_the_rules()
    {
        var output = await EchoAsync(Valid);

        using var json = JsonDocument.Parse(output);
        var root = json.RootElement;
        Assert.Equal(JsonValueKind.String, root.GetProperty("big").ValueKind); // long as a canonical string
        Assert.Equal("-9223372036854775808", root.GetProperty("big").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("note").ValueKind); // nullable values are written explicitly
        Assert.Equal("0f8fad5b-d9cb-469f-a165-70867728950e", root.GetProperty("key").GetString());
        Assert.Equal("2026-11-16T08:30:00+01:00", root.GetProperty("at").GetString());
        Assert.Equal("héllo ✓", root.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Missing_optional_values_are_null()
    {
        var output = await EchoAsync(Without(Valid, "note", "maybe", "parent"));

        using var json = JsonDocument.Parse(output);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("note").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("maybe").ValueKind);
    }

    [Fact]
    public async Task Unknown_properties_are_skipped_without_being_decoded()
    {
        var output = await EchoAsync(Valid.Replace("{\"text\"", "{\"future\":{\"x\":[1,{\"y\":\"\\ud800\"}]},\"text\"", StringComparison.Ordinal));

        Assert.StartsWith("{\"text\":", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("a required value is missing", "text")]
    [InlineData("a property is given twice", "twice")]
    [InlineData("there is content after the value", "trailing")]
    [InlineData("an int arrives as a string", "count-as-string")]
    [InlineData("a long arrives as a number", "big-as-number")]
    [InlineData("a long has a plus sign", "big-plus")]
    [InlineData("a long has a leading zero", "big-leading-zero")]
    [InlineData("a long is minus zero", "big-minus-zero")]
    [InlineData("a double overflows to infinity", "ratio-infinite")]
    [InlineData("a date has no offset", "at-without-offset")]
    [InlineData("a GUID is not one", "key-invalid")]
    [InlineData("the values nest deeper than 64", "too-deep")]
    public async Task A_malformed_request_is_rejected_before_the_command_runs(string why, string variant)
    {
        var request = variant switch
        {
            "text" => Without(Valid, "text"),
            "twice" => Valid.Replace("\"flag\":true", "\"flag\":true,\"flag\":false", StringComparison.Ordinal),
            "trailing" => Valid + " {}",
            "count-as-string" => Valid.Replace("\"count\":-3", "\"count\":\"-3\"", StringComparison.Ordinal),
            "big-as-number" => Valid.Replace("\"big\":\"-9223372036854775808\"", "\"big\":1", StringComparison.Ordinal),
            "big-plus" => Valid.Replace("\"-9223372036854775808\"", "\"+1\"", StringComparison.Ordinal),
            "big-leading-zero" => Valid.Replace("\"-9223372036854775808\"", "\"007\"", StringComparison.Ordinal),
            "big-minus-zero" => Valid.Replace("\"-9223372036854775808\"", "\"-0\"", StringComparison.Ordinal),
            "ratio-infinite" => Valid.Replace("\"ratio\":0.25", "\"ratio\":1e400", StringComparison.Ordinal),
            "at-without-offset" => Valid.Replace("2026-11-16T08:30:00+01:00", "2026-11-16T08:30:00", StringComparison.Ordinal),
            "key-invalid" => Valid.Replace("0f8fad5b-d9cb-469f-a165-70867728950e", "not-a-guid", StringComparison.Ordinal),
            _ => Nested(70),
        };

        var outcome = await _app.InvokeAsync("echo", "shapes", Encoding.UTF8.GetBytes(request), Token);

        Assert.True(outcome.Kind == SparOutcomeKind.Failure, why);
        Assert.Equal(("rejected", "invalid-request", "notStarted"), (outcome.Failure!.Kind, outcome.Failure.Code, outcome.Failure.Execution));
    }

    [Fact]
    public async Task Nesting_within_the_limit_is_accepted()
    {
        var outcome = await _app.InvokeAsync("echo", "shapes", Encoding.UTF8.GetBytes(Nested(30)), Token);

        Assert.Equal(SparOutcomeKind.Value, outcome.Kind);
    }

    [Fact]
    public async Task Declared_errors_round_trip_with_and_without_a_payload()
    {
        var echo = new EchoClient(_app);

        var refused = await echo.CheckAsync(new Item("no"), Token);
        var empty = await echo.CheckAsync(new Item(""), Token);
        var fine = await echo.CheckAsync(new Item("yes"), Token);

        Assert.Equal(new EchoError.Refused("said no"), refused.Error);
        Assert.Equal(new EchoError.Empty(), empty.Error);
        Assert.Equal(new Item("yes"), fine.Value);
    }

    [Fact]
    public async Task A_result_that_cannot_be_written_is_a_response_failure_after_the_command_ran()
    {
        var outcome = await _app.InvokeAsync("echo", "ratio", Encoding.UTF8.GetBytes("{\"name\":\"NaN\"}"), Token);

        Assert.Equal(("response", "serialization", "completed"), (outcome.Failure!.Kind, outcome.Failure.Code, outcome.Failure.Execution));
    }

    [Fact]
    public async Task The_extreme_longs_survive_as_strings()
    {
        var echo = new EchoClient(_app);

        Assert.Equal(long.MinValue, await echo.BigAsync(new Item("-9223372036854775808"), Token));
        Assert.Equal(long.MaxValue, await echo.BigAsync(new Item("9223372036854775807"), Token));
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<string> EchoAsync(string request)
    {
        var outcome = await _app.InvokeAsync("echo", "shapes", Encoding.UTF8.GetBytes(request), Token);
        Assert.True(outcome.Kind == SparOutcomeKind.Value, outcome.ToString());
        return Encoding.UTF8.GetString(outcome.Payload.Span);
    }

    /// <summary>The valid request with some properties removed.</summary>
    private static string Without(string json, params string[] names)
    {
        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in document.RootElement.EnumerateObject().Where(p => !names.Contains(p.Name)))
            {
                property.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>A request whose parents nest <paramref name="depth"/> levels deep.</summary>
    private static string Nested(int depth)
    {
        var inner = "null";
        for (var i = 0; i < depth; i++)
        {
            inner = Valid.Replace("\"parent\":null", "\"parent\":" + inner, StringComparison.Ordinal);
        }

        return inner;
    }
}
