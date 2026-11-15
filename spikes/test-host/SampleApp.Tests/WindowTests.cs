using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using SampleApp.Notes;
using Spar.Testing;

namespace SampleApp.Tests;

[CollectionDefinition(nameof(RealWindows), DisableParallelization = true)]
public sealed class RealWindows;

/// <summary>The app's real window (WebView2 or WebKitGTK), created by this test process on its UI thread.</summary>
[Collection(nameof(RealWindows))]
public sealed class WindowTests(ITestOutputHelper output)
{
    private Spar.MemorySparLog? _log;

    private static string ExpectedTransport => OperatingSystem.IsWindows() ? "\"webview2\"" : "\"webkitgtk\"";

    [Fact]
    public async Task A_journey_in_the_real_window()
    {
        var clock = Stopwatch.StartNew();
        await using (var app = await TestApps.StartAsync(SparTestSurface.Window, orders: 7))
        {
            var opened = clock.Elapsed;
            Assert.Equal(ExpectedTransport, await app.EvaluateAsync("return await window.sparReady;", TestContext.Current.CancellationToken));
            var ready = clock.Elapsed;
            Assert.Equal("\"7\"", await app.EvaluateAsync("return document.getElementById('orders').textContent;", TestContext.Current.CancellationToken));

            await AddThroughPageAsync(app, "from the window");

            Assert.Equal(["from the window"], app.Services.GetRequiredService<INoteStore>().All().Select(n => n.Text));
            app.Log.Write("spike", "marker");
            _log = app.Log;
            output.WriteLine($"SPIKE window journey: open={opened.TotalMilliseconds:F0}ms ready={ready.TotalMilliseconds:F0}ms done={clock.Elapsed.TotalMilliseconds:F0}ms engine-pids=[{string.Join(",", app.EngineProcessIds)}] descendants={EngineProcesses.Descendants().Count}");
        }

        var closed = clock.Elapsed;
        var left = await EngineProcesses.WaitUntilNoneAsync(TimeSpan.FromSeconds(10));
        output.WriteLine($"SPIKE dispose {SparTestApp.LastDisposeTimings}");
        output.WriteLine($"SPIKE window closed={closed.TotalMilliseconds:F0}ms engine-processes-left={left.Count} after={clock.Elapsed.TotalMilliseconds:F0}ms");
        output.WriteLine("SPIKE " + string.Join(" | ", _log!.Entries.Where(e => e.Category == "spike").Select(e => e.Message)));
        Assert.Empty(left);
    }

    /// <summary>Diagnostics only: what the page loaded and ran.</summary>
    [Fact]
    public async Task Page_facts()
    {
        await using var app = await TestApps.StartAsync(SparTestSurface.Window);
        var facts = await app.EvaluateAsync(
            """
            const script = await fetch('app.js').then(r => r.status + ' ' + r.headers.get('content-type')).catch(e => 'fetch failed: ' + e);
            return {
              href: location.href, origin: location.origin, readyState: document.readyState,
              spar: typeof window.spar, ready: typeof window.sparReady, scripts: [...document.scripts].map(s => s.src),
              resources: performance.getEntriesByType('resource').map(e => e.name), script,
              webkit: typeof window.webkit, webview: typeof window.chrome,
            };
            """,
            TestContext.Current.CancellationToken);
        output.WriteLine("SPIKE page facts: " + facts);
    }

    [Fact]
    public async Task Fifty_windows_in_sequence()
    {
        var timings = new List<(double Ready, double Total)>();
        var memory = new List<string>();
        for (var i = 0; i < 50; i++)
        {
            var clock = Stopwatch.StartNew();
            await using (var app = await TestApps.StartAsync(SparTestSurface.Window))
            {
                await app.EvaluateAsync("return await window.sparReady;", TestContext.Current.CancellationToken);
                var ready = clock.Elapsed.TotalMilliseconds;
                await AddThroughPageAsync(app, $"note {i}");
                Assert.Single(app.Services.GetRequiredService<INoteStore>().All());
                timings.Add((ready, 0));
            }

            timings[^1] = (timings[^1].Ready, clock.Elapsed.TotalMilliseconds);
            if (i is 0 or 9 or 24 or 49)
            {
                memory.Add($"{i + 1}:{Environment.WorkingSet / (1024 * 1024)}MB");
            }
        }

        var left = await EngineProcesses.WaitUntilNoneAsync(TimeSpan.FromSeconds(10));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"SPIKE 50 windows: ready first={timings[0].Ready:F0} median={CommandTests.Median(timings.Skip(1).Select(t => t.Ready)):F0} max={timings.Skip(1).Max(t => t.Ready):F0}; open+journey+close median={CommandTests.Median(timings.Skip(1).Select(t => t.Total)):F0} max={timings.Skip(1).Max(t => t.Total):F0}; engine-processes-left={left.Count}; working-set after window {string.Join(" ", memory)}"));
        Assert.Empty(left);
    }

    [Fact]
    public async Task Four_windows_at_once()
    {
        var clock = Stopwatch.StartNew();
        var apps = await Task.WhenAll(Enumerable.Range(1, 4).Select(i => TestApps.StartAsync(SparTestSurface.Window, orders: i)));
        var opened = clock.Elapsed;
        try
        {
            await Task.WhenAll(apps.Select(async (app, index) =>
            {
                await app.EvaluateAsync("return await window.sparReady;", TestContext.Current.CancellationToken);
                Assert.Equal($"\"{index + 1}\"", await app.EvaluateAsync("return document.getElementById('orders').textContent;", TestContext.Current.CancellationToken));
                await AddThroughPageAsync(app, $"window {index}");
            }));

            for (var index = 0; index < apps.Length; index++)
            {
                Assert.Equal([$"window {index}"], apps[index].Services.GetRequiredService<INoteStore>().All().Select(n => n.Text));
            }

            output.WriteLine($"SPIKE 4 windows: all-open={opened.TotalMilliseconds:F0}ms all-journeys={clock.Elapsed.TotalMilliseconds:F0}ms descendants={EngineProcesses.Descendants().Count}");
        }
        finally
        {
            await Task.WhenAll(apps.Select(app => app.DisposeAsync().AsTask()));
        }

        Assert.Empty(await EngineProcesses.WaitUntilNoneAsync(TimeSpan.FromSeconds(10)));
    }

    /// <summary>Types into the page's form and submits it (synthetic events; trusted input is a different level).</summary>
    private static async Task AddThroughPageAsync(SparTestApp app, string text)
    {
        var script = $$"""
            const input = document.getElementById('text');
            input.value = {{System.Text.Json.JsonSerializer.Serialize(text, SampleJson.Default.String)}};
            document.querySelector('#add button').click();
            const deadline = Date.now() + 5000;
            while (![...document.querySelectorAll('#notes li')].some(li => li.textContent === input.dataset.expected)) {
              if (Date.now() > deadline) throw new Error('note did not appear');
              await new Promise(resolve => setTimeout(resolve, 10));
            }
            return true;
            """;
        script = script.Replace("input.dataset.expected", System.Text.Json.JsonSerializer.Serialize(text, SampleJson.Default.String), StringComparison.Ordinal);
        Assert.Equal("true", await app.EvaluateAsync(script, TestContext.Current.CancellationToken));
    }
}
