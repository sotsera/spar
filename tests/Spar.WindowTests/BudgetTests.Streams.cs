using System.Text.Json;
using Spar.Testing;
using WireShapes.Tests;

namespace Spar.WindowTests;

/// <summary>
/// The stream budgets on the desktop bridges (decision "M3a's choices accepted"): the same two measures as over loopback,
/// timed by WindowHost's page in its window: 100,000 rows from the call to the last row, and 20,000 subscription events
/// offered over one second, all delivered.
/// </summary>
public sealed partial class BudgetTests
{
    private const int StreamRuns = 5;
    private const int PushRuns = 3;

    [Fact]
    public async Task Hundred_thousand_stream_rows_cross_the_desktop_bridge()
    {
        await using var host = await HostPage.OpenAsync();

        var durations = new List<double>(StreamRuns);
        for (var run = 0; run <= StreamRuns; run++)
        {
            using var outcome = await StreamOutcomeAsync(host.Page, "window.hostStreams.rows(100000)");
            Assert.Equal(100_000, outcome.RootElement.GetProperty("received").GetInt32());
            Assert.True(outcome.RootElement.GetProperty("ordered").GetBoolean());
            if (run > 0)
            {
                durations.Add(outcome.RootElement.GetProperty("ms").GetDouble());
            }
        }

        Budgets.Record("Desktop bridge: 100,000 stream rows, timed by the page, first run left out", Budgets.Summary(durations));
        host.AssertClean();
    }

    [Fact]
    public async Task Twenty_thousand_subscription_events_a_second_cross_the_desktop_bridge()
    {
        await using var host = await HostPage.OpenAsync();

        var durations = new List<double>(PushRuns);
        for (var run = 0; run < PushRuns; run++)
        {
            using var outcome = await StreamOutcomeAsync(host.Page, "window.hostStreams.burst(20000)");
            Assert.Equal(20_000, outcome.RootElement.GetProperty("received").GetInt32());
            Assert.Equal(0, outcome.RootElement.GetProperty("gaps").GetInt32());
            durations.Add(outcome.RootElement.GetProperty("ms").GetDouble());
        }

        Budgets.Record(
            "Desktop bridge: 20,000 subscription events offered over one second, all delivered: time to the last, timed by the page",
            Budgets.Summary(durations));
        host.AssertClean();
    }

    /// <summary>Starts a measurement in the page and waits for the outcome it leaves in <c>window.hostStreamResult</c>.</summary>
    private static async Task<JsonDocument> StreamOutcomeAsync(SparPage page, string start)
    {
        await page.EvaluateAsync(start);
        var limit = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var text = await page.EvaluateAsync<string>("window.hostStreamResult");
            if (text.Length > 0)
            {
                return JsonDocument.Parse(text);
            }

            Assert.True(DateTime.UtcNow < limit, $"The page did not finish {start} within 30 s.");
            await Task.Delay(20, Token);
        }
    }
}
