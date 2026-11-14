using System.Diagnostics;
using System.Text.Json;
using Loop.Tests;
using Microsoft.Playwright;

namespace React.Tests;

/// <summary>
/// Streaming, cancellation and push through the real loopback host and the generated SDK, driven in a headless
/// browser on the bench page. The functional checks run by default; the measurement matrix is explicit
/// (<c>dotnet test ... -- --filter-trait Category=Bench</c> or by name) and appends JSON lines to
/// <c>artifacts/react/bridge-bench.jsonl</c>.
/// </summary>
public sealed class BridgeTests
{
    [Theory]
    [InlineData("fetch")]
    [InlineData("channel")]
    public async Task A_stream_delivers_every_row_in_order(string transport)
    {
        await using var bench = await BenchPage.OpenAsync(TestContext.Current.CancellationToken);
        var result = await bench.RunAsync("stream", new { rows = 20_000, batch = 100, transport });
        Assert.Equal(20_000, result.GetProperty("rows").GetInt32());
        Assert.True(result.GetProperty("correct").GetBoolean());
    }

    [Theory]
    [InlineData("fetch", 1)]
    [InlineData("fetch", 100)]
    [InlineData("channel", 100)]
    public async Task A_consumer_that_stops_reading_stalls_the_producer(string transport, int batch)
    {
        await using var bench = await BenchPage.OpenAsync(TestContext.Current.CancellationToken);
        var result = await bench.RunAsync("backpressure", new { batch, transport });
        Assert.True(result.GetProperty("producerStalled").GetBoolean(), result.ToString());
        Assert.InRange(result.GetProperty("aheadOfConsumer").GetInt64(), 0, 200_000);
        Assert.True(result.GetProperty("closeOutcome").GetString() == "stopped", string.Join(" | ", bench.AppOutput) + " " + result);
    }

    [Theory]
    [InlineData("fetch")]
    public async Task Cancelling_a_unary_call_is_confirmed_and_the_work_stops(string transport)
    {
        await using var bench = await BenchPage.OpenAsync(TestContext.Current.CancellationToken);
        var result = await bench.RunAsync("cancelUnary", new { transport, honor = true });
        Assert.Equal("cancelled", result.GetProperty("outcome").GetString());
        Assert.Equal(0, result.GetProperty("counterMovedWithin300msAfterOutcome").GetInt64());
        Assert.Equal(0, result.GetProperty("workRunningAtOutcome").GetInt64());
    }

    [Fact]
    public async Task Work_that_ignores_cancellation_is_reported_as_outcome_unknown()
    {
        await using var bench = await BenchPage.OpenAsync(TestContext.Current.CancellationToken);
        var result = await bench.RunAsync("cancelUnary", new { transport = "fetch", honor = false, confirmMs = 500 });
        Assert.Equal("unknown:unconfirmed", result.GetProperty("outcome").GetString());
        Assert.True(result.GetProperty("counterMovedWithin300msAfterOutcome").GetInt64() > 0, "The work kept running, so the page must not be told it stopped.");
    }

    [Theory]
    [InlineData("fetch", "abort", "cancelled")]
    [InlineData("channel", "abort", "cancelled")]
    [InlineData("channel", "break", "stopped-by-break")]
    public async Task Cancelling_a_stream_is_confirmed_and_the_work_stops(string transport, string how, string expected)
    {
        await using var bench = await BenchPage.OpenAsync(TestContext.Current.CancellationToken);
        var result = await bench.RunAsync("cancelStream", new { transport, how, honor = true });
        Assert.Equal(expected, result.GetProperty("outcome").GetString());
        Assert.Equal(0, result.GetProperty("counterMovedWithin300msAfterOutcome").GetInt64());
        Assert.Equal(0, result.GetProperty("workRunningAtOutcome").GetInt64());
    }

    [Theory]
    [InlineData("channel")]
    // Not "fetch": a per-call streamed response hides overflow in OS and browser buffers (latency grows instead).
    public async Task A_slow_subscriber_sees_gaps_and_every_event_is_accounted_for(string transport)
    {
        await using var bench = await BenchPage.OpenAsync(TestContext.Current.CancellationToken);
        var result = await bench.RunAsync("push", new { transport, rate = 2000, seconds = 2, slowConsumerMs = 200 });
        Assert.True(result.GetProperty("gaps").GetInt32() > 0, result.ToString());
        Assert.True(result.GetProperty("accounted").GetBoolean(), result.ToString());
        Assert.Equal(0, result.GetProperty("outOfOrder").GetInt32());
    }

    [Fact]
    public async Task A_reload_ends_the_old_documents_subscription()
    {
        await using var bench = await BenchPage.OpenAsync(TestContext.Current.CancellationToken);
        await bench.RunAsync("watchForever", new { transport = "channel", rate = 100, resubscribe = false });
        await bench.WaitForAsync("(await window.sparBench.stats()).activeWatches === 1");

        await bench.Page.ReloadAsync();
        await bench.WaitReadyAsync();
        await bench.RunAsync("watchForever", new { transport = "channel", rate = 100, resubscribe = false });
        await bench.WaitForAsync("window.sparBench.watchStatus().events > 0");
        await bench.WaitForAsync("(await window.sparBench.stats()).activeWatches === 1");
    }

    // ---- Measurements (explicit) ----

    [Fact(Explicit = true)]
    [Trait("Category", "Bench")]
    public async Task Measure_streams()
    {
        foreach (var transport in new[] { "fetch", "channel" })
        {
            foreach (var batch in new[] { 1, 100, 1000 })
            {
                for (var run = 0; run < 3; run++)
                {
                    await using var bench = await BenchPage.OpenAsync(TestContext.Current.CancellationToken);
                    var metrics = await bench.StartMetricsAsync();
                    var result = await bench.RunAsync("stream", new { rows = 100_000, batch, transport }, timeoutSeconds: 300);
                    Record("stream", result, await bench.StopMetricsAsync(metrics), bench.Browser);
                }
            }
        }

        await using (var bench = await BenchPage.OpenAsync(TestContext.Current.CancellationToken))
        {
            // A grid that keeps every row: page memory for 100k rows.
            Record("stream-keep", await bench.RunAsync("stream", new { rows = 100_000, batch = 1000, transport = "channel", keep = true }, 300), null, bench.Browser);
            foreach (var transport in new[] { "fetch", "channel" })
            {
                foreach (var batch in new[] { 1, 100, 1000 })
                {
                    Record("backpressure", await bench.RunAsync("backpressure", new { batch, transport }), null, bench.Browser);
                }
            }
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "Bench")]
    public async Task Measure_cancellation()
    {
        await using var bench = await BenchPage.OpenAsync(TestContext.Current.CancellationToken);
        for (var run = 0; run < 5; run++)
        {
            Record("cancel-unary", await bench.RunAsync("cancelUnary", new { transport = "fetch", honor = true }), null, bench.Browser);
            Record("cancel-stream", await bench.RunAsync("cancelStream", new { transport = "fetch", how = "abort", honor = true }), null, bench.Browser);
            Record("cancel-stream", await bench.RunAsync("cancelStream", new { transport = "channel", how = "abort", honor = true }), null, bench.Browser);
            Record("cancel-stream", await bench.RunAsync("cancelStream", new { transport = "channel", how = "break", honor = true }), null, bench.Browser);
        }

        Record("cancel-unary", await bench.RunAsync("cancelUnary", new { transport = "fetch", honor = false, confirmMs = 1000 }), null, bench.Browser);
        Record("cancel-stream", await bench.RunAsync("cancelStream", new { transport = "channel", how = "abort", honor = false, tickMs = 3000, confirmMs = 1000 }), null, bench.Browser);
        Record("cancel-stream", await bench.RunAsync("cancelStream", new { transport = "channel", how = "abort", honor = false, tickMs = 1 }), null, bench.Browser);
    }

    [Fact(Explicit = true)]
    [Trait("Category", "Bench")]
    public async Task Measure_push()
    {
        foreach (var transport in new[] { "channel", "fetch" })
        {
            foreach (var rate in new[] { 1000, 5000, 20_000 })
            {
                await using var bench = await BenchPage.OpenAsync(TestContext.Current.CancellationToken);
                var metrics = await bench.StartMetricsAsync();
                var result = await bench.RunAsync("push", new { transport, rate, seconds = 5 }, 60);
                Record("push", result, await bench.StopMetricsAsync(metrics), bench.Browser);
            }
        }

        await using (var bench = await BenchPage.OpenAsync(TestContext.Current.CancellationToken))
        {
            Record("push-slow", await bench.RunAsync("push", new { transport = "channel", rate = 2000, seconds = 3, slowConsumerMs = 200 }), null, bench.Browser);

            Record("unary", await bench.RunAsync("unary", new { transport = "fetch", n = 300, size = 0 }), null, bench.Browser);
            Record("unary", await bench.RunAsync("unary", new { transport = "fetch", n = 300, size = 1024 }), null, bench.Browser);
        }
    }

[Theory(Explicit = true)]    [Trait("Category", "Bench")]    [InlineData("channel")]    [InlineData("fetch")]    public async Task Measure_connection_limit(string transport)    {        await using var bench = await BenchPage.OpenAsync(TestContext.Current.CancellationToken);        Record("connection-limit", await bench.RunAsync("connectionLimit", new { transport, subscriptions = 6 }, 30), null, bench.Browser);    }
    /// <summary>A development session: the backend restarts on the same port and token; an opted-in subscription resumes.</summary>
    [Fact(Explicit = true)]
    [Trait("Category", "Bench")]
    public async Task Measure_resubscribe_after_backend_restart()
    {
        var token = TestContext.Current.CancellationToken;
        var session = Path.Combine(Path.GetTempPath(), "spar-bridge-" + Guid.NewGuid().ToString("N")[..8] + ".json");
        try
        {
            var first = await LoopbackApp.StartAsync(token, "--dev-session", session);
            await using var bench = await BenchPage.OpenAsync(first, token);
            await bench.RunAsync("watchForever", new { transport = "channel", rate = 100, resubscribe = true });
            await bench.WaitForAsync("window.sparBench.watchStatus().events > 10");

            var stopped = Stopwatch.StartNew();
            await first.KillAsync();
            await using var second = await LoopbackApp.StartAsync(token, "--dev-session", session);
            var backendReady = stopped.Elapsed;
            Assert.Equal(new Uri(first.BaseAddress).Port, new Uri(second.BaseAddress).Port);
            await bench.WaitForAsync("window.sparBench.watchStatus().resets === 1 && window.sparBench.watchStatus().events > 20", 20);
            var resumed = stopped.Elapsed;
            var status = await bench.RunAsync("watchStatus", new { });
            Record("resubscribe", status, null, bench.Browser, $"\"killToBackendReadyMs\":{backendReady.TotalMilliseconds:F0},\"killToResumedMs\":{resumed.TotalMilliseconds:F0}");
        }
        finally
        {
            File.Delete(session);
        }
    }

    private static void Record(string kind, JsonElement result, string? metrics, string browser, string? extra = null)
    {
        var line = $"{{\"kind\":\"{kind}\",\"host\":\"loopback\",\"browser\":\"{browser}\",\"result\":{result.GetRawText()}"
            + (metrics is null ? "" : $",\"page\":{metrics}") + (extra is null ? "" : "," + extra) + "}";
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory!.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        var folder = Path.Combine(directory.FullName, "artifacts", "react");
        Directory.CreateDirectory(folder);
        File.AppendAllLines(Path.Combine(folder, "bridge-bench.jsonl"), [line]);
        TestContext.Current.SendDiagnosticMessage(line);
    }

    /// <summary>The app, a headless Chromium and the bench page, opened with the run's token.</summary>
    private sealed class BenchPage : IAsyncDisposable
    {
        private readonly LoopbackApp? _ownedApp;
        private readonly IPlaywright _playwright;
        private readonly IBrowser _browser;
        private readonly ICDPSession _cdp;

        private BenchPage(LoopbackApp? ownedApp, IPlaywright playwright, IBrowser browser, IPage page, ICDPSession cdp)
        {
            _ownedApp = ownedApp;
            _playwright = playwright;
            _browser = browser;
            Page = page;
            _cdp = cdp;
        }

        public IPage Page { get; }

        public IReadOnlyList<string> AppOutput => _ownedApp?.Output.Where(l => !l.Contains("lab.stats", StringComparison.Ordinal)).TakeLast(20).ToList() ?? [];

        public string Browser => "chromium-headless-shell " + _browser.Version;

        public static async Task<BenchPage> OpenAsync(CancellationToken cancellationToken) =>
            await OpenAsync(await LoopbackApp.StartAsync(cancellationToken), cancellationToken, owned: true);

        public static Task<BenchPage> OpenAsync(LoopbackApp app, CancellationToken cancellationToken) => OpenAsync(app, cancellationToken, owned: false);

        private static async Task<BenchPage> OpenAsync(LoopbackApp app, CancellationToken cancellationToken, bool owned)
        {
            var playwright = await Playwright.CreateAsync();
            var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
            var page = await browser.NewPageAsync();
            var cdp = await page.Context.NewCDPSessionAsync(page);
            await page.GotoAsync(app.Url.Replace("/#", "/bench.html#", StringComparison.Ordinal));
            var bench = new BenchPage(owned ? app : null, playwright, browser, page, cdp);
            await bench.WaitReadyAsync();
            return bench;
        }

        public Task WaitReadyAsync() => Page.WaitForFunctionAsync("window.sparBenchReady === true");

        public async Task<JsonElement> RunAsync(string function, object options, int timeoutSeconds = 60)
        {
            try
            {
                return await Page.EvaluateAsync<JsonElement>(
                    "([f, o]) => window.sparBench[f](o)", new object[] { function, options }).WaitAsync(TimeSpan.FromSeconds(timeoutSeconds));
            }
            catch (Exception exception) when (_ownedApp is not null && exception is PlaywrightException or TimeoutException)
            {
                var output = string.Join('\n', _ownedApp.Output.Where(l => !l.Contains("lab.stats", StringComparison.Ordinal)).TakeLast(15));
                throw new InvalidOperationException($"{function} failed: {exception.Message}\nApp output:\n{output}", exception);
            }
        }

        /// <summary>Polls an (optionally async) page expression until it is true. A promise is awaited, not taken as truthy.</summary>
        public async Task WaitForAsync(string condition, int timeoutSeconds = 10)
        {
            var deadline = Stopwatch.StartNew();
            while (!await Page.EvaluateAsync<bool>($"(async () => !!({condition}))()"))
            {
                if (deadline.Elapsed > TimeSpan.FromSeconds(timeoutSeconds))
                {
                    throw new TimeoutException($"Not true after {timeoutSeconds} s: {condition} | {string.Join(" | ", AppOutput)}");
                }

                await Task.Delay(25);
            }
        }

        /// <summary>CDP Performance metrics of the page's main thread (task and script time).</summary>
        public async Task<Dictionary<string, double>> StartMetricsAsync()
        {
            await _cdp.SendAsync("Performance.enable");
            return await MetricsAsync();
        }

        public async Task<string> StopMetricsAsync(Dictionary<string, double> start)
        {
            var end = await MetricsAsync();
            double Delta(string name) => (end.GetValueOrDefault(name) - start.GetValueOrDefault(name)) * 1000;
            var heap = await _cdp.SendAsync("Runtime.getHeapUsage");
            return FormattableString.Invariant(
                $"{{\"taskMs\":{Delta("TaskDuration"):F0},\"scriptMs\":{Delta("ScriptDuration"):F0},\"wallMs\":{Delta("Timestamp"):F0},\"heapUsedMB\":{heap!.Value.GetProperty("usedSize").GetDouble() / 1048576:F1}}}");
        }

        private async Task<Dictionary<string, double>> MetricsAsync()
        {
            var metrics = await _cdp.SendAsync("Performance.getMetrics");
            return metrics!.Value.GetProperty("metrics").EnumerateArray()
                .ToDictionary(m => m.GetProperty("name").GetString()!, m => m.GetProperty("value").GetDouble());
        }

        public async ValueTask DisposeAsync()
        {
            await _browser.DisposeAsync();
            _playwright.Dispose();
            if (_ownedApp is not null)
            {
                await _ownedApp.DisposeAsync();
            }
        }
    }
}
