using System.Text.Json;
using WireShapes.Tests;

namespace WireShapes.EndToEndTests;

/// <summary>
/// Streams and subscriptions from the page over the loopback bridge, through the generated module and the SDK's
/// iterator (decision "M3a's decide-first questions"): every row in order, the in-band end, a confirmed break and abort,
/// and the push budget of 20,000 events a second.
/// </summary>
[Trait("Surface", "Browser")]
public sealed class StreamBrowserTests(SharedBrowser shared)
{
    [Fact]
    public async Task A_stream_brings_every_row_in_order()
    {
        await using var wire = await WirePage.OpenAsync(shared);

        using var outcome = JsonDocument.Parse(await wire.CallAsync("count => window.wire.rows(count)", 100_000));

        Assert.Equal(100_000, outcome.RootElement.GetProperty("received").GetInt32());
        Assert.True(outcome.RootElement.GetProperty("ordered").GetBoolean());
        await wire.AssertCleanAsync();
        Budgets.Record("100,000 stream rows over loopback, timed by the page", $"{outcome.RootElement.GetProperty("ms").GetDouble():0.0} ms");
    }

    [Fact]
    public async Task A_declared_error_is_the_streams_last_item()
    {
        await using var wire = await WirePage.OpenAsync(shared);

        var seen = await wire.CallAsync("failAt => window.wire.readUntil(failAt)", 3);

        Assert.Equal("[0,1,2,\"unreadable 3\"]", seen);
        await wire.AssertCleanAsync();
    }

    [Fact]
    public async Task Leaving_the_loop_early_waits_for_the_backend_to_stop()
    {
        await using var wire = await WirePage.OpenAsync(shared);

        Assert.Equal("10", await wire.CallAsync("take => window.wire.breakEarly(take)", 10));
        await wire.AssertCleanAsync();
    }

    [Fact]
    public async Task An_aborted_subscription_ends_with_the_confirmed_cancel()
    {
        await using var wire = await WirePage.OpenAsync(shared);

        var seen = await wire.CallAsync("() => window.wire.ticksThenAbort()", null);

        Assert.Equal("[0,1,2,\"cancelled cancelled\"]", seen);
        await wire.AssertCleanAsync();
    }

    [Fact]
    public async Task Twenty_thousand_events_a_second_arrive_whole()
    {
        await using var wire = await WirePage.OpenAsync(shared);

        using var outcome = JsonDocument.Parse(await wire.CallAsync("perSecond => window.wire.burst(perSecond)", 20_000));

        Assert.Equal(20_000, outcome.RootElement.GetProperty("received").GetInt32());
        Assert.Equal(0, outcome.RootElement.GetProperty("gaps").GetInt32());
        await wire.AssertCleanAsync();
        Budgets.Record("20,000 subscription events in one second over loopback, all delivered, timed by the page", $"{outcome.RootElement.GetProperty("ms").GetDouble():0.0} ms");
    }

    [Fact]
    public async Task A_subscription_resumes_on_a_new_channel_after_the_old_one_dropped()
    {
        await using var wire = await WirePage.OpenAsync(shared);
        var firstDocument = await wire.CallAsync("() => window.wire.document()", null);
        await wire.Page.EvaluateAsync("() => window.startTicks()");
        await wire.Page.WaitForFunctionAsync("() => window.ticksSeen.length >= 2");

        wire.App.DropChannel();
        await wire.Page.WaitForFunctionAsync("() => window.ticksSeen.includes('reconnecting 2')");
        wire.App.RestoreChannel();
        await wire.Page.WaitForFunctionAsync("() => window.ticksSeen.includes('resumed') && typeof window.ticksSeen.at(-1) === 'number'");
        await wire.Page.EvaluateAsync("() => window.stopTicks()");

        using var seen = JsonDocument.Parse(await wire.Page.EvaluateAsync<string>("() => JSON.stringify(window.ticksSeen)"));
        var events = seen.RootElement.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()).ToList();
        var reset = events.IndexOf("reset");
        Assert.True(reset > 0, string.Join(", ", events));
        Assert.Equal("reconnecting 1", events[0]);
        Assert.Equal("resumed", events[reset + 1]);
        Assert.NotEqual(firstDocument, await wire.CallAsync("() => window.wire.document()", null));
    }
}
