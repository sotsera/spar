using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SampleApp.Notes;
using Spar.Testing;

namespace SampleApp.Tests;

/// <summary>Command level: the app's real composition and bridge, no page.</summary>
public sealed class CommandTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Adding_a_note_goes_through_the_real_bridge()
    {
        await using var app = await TestApps.StartAsync();
        var notes = new NotesClient(app);

        var added = await notes.AddAsync("  buy milk ");

        Assert.True(added.IsSuccess);
        Assert.Equal("buy milk", added.Value!.Text);
        Assert.Equal(TestApps.Now, added.Value.CreatedAt); // the test's TimeProvider replaced the app's
        Assert.Equal(["buy milk"], (await notes.ListAsync()).Select(n => n.Text));

        // The test can inspect the backend directly.
        Assert.Single(app.Services.GetRequiredService<INoteStore>().All());
    }

    [Fact]
    public async Task A_declared_error_round_trips()
    {
        await using var app = await TestApps.StartAsync();

        var added = await new NotesClient(app).AddAsync("   ");

        Assert.False(added.IsSuccess);
        Assert.Equal("empty", added.Error!.Code);
    }

    [Fact]
    public async Task The_replaced_order_database_answers()
    {
        await using var app = await TestApps.StartAsync(orders: 42);

        var reply = await new NotesClient(app).OrderCountAsync();

        Assert.Equal(("ok", "42"), (reply.Status, reply.Payload));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(8)]
    public async Task Apps_started_together_stay_apart(int count)
    {
        var clock = Stopwatch.StartNew();
        var apps = await Task.WhenAll(Enumerable.Range(1, count).Select(i => TestApps.StartAsync(orders: i)));
        var startedAll = clock.Elapsed;
        try
        {
            await Task.WhenAll(apps.Select(async (app, index) =>
            {
                var notes = new NotesClient(app);
                for (var n = 0; n <= index; n++)
                {
                    await notes.AddAsync($"app {index} note {n}");
                }
            }));

            for (var index = 0; index < count; index++)
            {
                var notes = new NotesClient(apps[index]);
                var texts = (await notes.ListAsync()).Select(n => n.Text).ToList();
                Assert.Equal(index + 1, texts.Count);
                Assert.All(texts, text => Assert.StartsWith($"app {index} ", text, StringComparison.Ordinal));
                Assert.Equal((index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), (await notes.OrderCountAsync()).Payload);
                Assert.Single(apps[index].Log.Entries, e => e.Message == Spar.SparTestSession.Marker);
            }

            Assert.Equal(count, apps.Select(a => a.Folders.DataRoot).Distinct().Count());
            output.WriteLine($"SPIKE parallel-start count={count} all-started={startedAll.TotalMilliseconds:F1}ms each=[{string.Join(", ", apps.Select(a => a.StartedAfter.TotalMilliseconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)))}]");
        }
        finally
        {
            foreach (var app in apps)
            {
                await app.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task Start_cost_in_sequence()
    {
        var timings = new List<double>();
        for (var i = 0; i < 20; i++)
        {
            var clock = Stopwatch.StartNew();
            await using (var app = await TestApps.StartAsync())
            {
                await new NotesClient(app).AddAsync("x");
            }

            timings.Add(clock.Elapsed.TotalMilliseconds);
        }

        output.WriteLine($"SPIKE sequential start+call+dispose ms: first={timings[0]:F1} median-rest={Median(timings.Skip(1)):F2} max-rest={timings.Skip(1).Max():F2}");
    }

    internal static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[(sorted.Length / 2) - 1] + sorted[sorted.Length / 2]) / 2;
    }
}
