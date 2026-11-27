using System.Diagnostics;
using System.Text.Json;
using WireShapes.Tests;

namespace WireShapes.EndToEndTests;

/// <summary>
/// The browser budgets (plan, "Provisional budgets"), on the WireShapes page: an app with its page in a new context and
/// one call, as a browser test starts; and small calls from the page over the loopback bridge. Release builds give the
/// budget's numbers (<see cref="Budgets"/>); the Notebook's own journeys are the real app's view.
/// </summary>
[Trait("Surface", "Browser")]
public sealed class BudgetTests(SharedBrowser shared)
{
    private const int Journeys = 10;

    private const int RoundTrips = 200;

    private const int StreamRuns = 5;

    private const int PushRuns = 3;

    [Fact]
    public async Task Browser_test_app_context_and_journey()
    {
        var journeys = new List<double>(Journeys);
        for (var i = 0; i < Journeys; i++)
        {
            var clock = Stopwatch.StartNew();
            await using (var wire = await WirePage.OpenAsync(shared))
            {
                Assert.Equal("{\"value\":\"hello\"}", await wire.CallAsync("name => window.wire.check(name)", "hello"));
                await wire.AssertCleanAsync();
            }

            journeys.Add(clock.Elapsed.TotalMilliseconds);
        }

        Budgets.Record("Browser test, first app + context + journey", Budgets.Summary(journeys[..1]));
        Budgets.Record("Browser test, app + context + journey (budget 250 ms)", Budgets.Summary(journeys[1..]));
    }

    [Fact]
    public async Task Small_unary_round_trip_over_loopback()
    {
        await using var wire = await WirePage.OpenAsync(shared);

        var json = await wire.CallAsync("count => window.wire.roundTrips(count, 20)", RoundTrips);

        var durations = JsonDocument.Parse(json).RootElement.EnumerateArray().Select(d => d.GetDouble()).ToList();
        Assert.Equal(RoundTrips, durations.Count);
        await wire.AssertCleanAsync();
        Budgets.Record("Small unary round trip over loopback, timed by the page (budget 2 ms)", Budgets.Summary(durations));
    }

    [Fact]
    public async Task Hundred_thousand_stream_rows_over_loopback()
    {
        await using var wire = await WirePage.OpenAsync(shared);

        var durations = new List<double>(StreamRuns);
        for (var run = 0; run <= StreamRuns; run++)
        {
            using var outcome = JsonDocument.Parse(await wire.CallAsync("count => window.wire.rows(count)", 100_000));
            Assert.Equal(100_000, outcome.RootElement.GetProperty("received").GetInt32());
            Assert.True(outcome.RootElement.GetProperty("ordered").GetBoolean());
            if (run > 0)
            {
                durations.Add(outcome.RootElement.GetProperty("ms").GetDouble());
            }
        }

        await wire.AssertCleanAsync();
        Budgets.Record("100,000 stream rows over loopback, timed by the page, first run left out", Budgets.Summary(durations));
    }

    [Fact]
    public async Task Twenty_thousand_subscription_events_a_second_over_loopback()
    {
        await using var wire = await WirePage.OpenAsync(shared);

        var durations = new List<double>(PushRuns);
        for (var run = 0; run < PushRuns; run++)
        {
            using var outcome = JsonDocument.Parse(await wire.CallAsync("perSecond => window.wire.burst(perSecond)", 20_000));
            Assert.Equal(20_000, outcome.RootElement.GetProperty("received").GetInt32());
            Assert.Equal(0, outcome.RootElement.GetProperty("gaps").GetInt32());
            durations.Add(outcome.RootElement.GetProperty("ms").GetDouble());
        }

        await wire.AssertCleanAsync();
        Budgets.Record("20,000 subscription events offered over one second over loopback, all delivered: time to the last, timed by the page", Budgets.Summary(durations));
    }
}
