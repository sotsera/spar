using System.Diagnostics;
using System.Text.Json;
using WireShapes.Tests;

namespace WireShapes.EndToEndTests;

/// <summary>
/// The browser budgets (plan, "Provisional budgets"), on the WireShapes page: an app with its page in a new context and
/// one call, as a browser test starts; and small calls from the page over the loopback bridge. Release builds give the
/// budget's numbers (<see cref="Budgets"/>); the Notebook's own journeys are the real app's view.
/// </summary>
public sealed class BudgetTests(SharedBrowser shared)
{
    private const int Journeys = 10;

    private const int RoundTrips = 200;

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
}
