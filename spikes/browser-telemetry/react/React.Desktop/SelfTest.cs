#if SPAR_SELFTEST
using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using React.Desktop.Interop;

namespace React.Desktop;

/// <summary>
/// Spike self-test (Debug, or Release with -p:SparSelfTest=true): drives the real page through in-process CDP,
/// checks caching, compares the two bridge transports, samples memory, then closes the window.
/// Output lines start with <c>SELFTEST</c>.
/// </summary>
internal static class SelfTest
{
    public static async Task RunAsync(DesktopHost host)
    {
        try
        {
            await JourneyAsync(host);
            if (Environment.GetCommandLineArgs().Contains("--telemetry"))
            {
                await TelemetryAsync(host);
                host.Close();
                return;
            }

            if (!host.IsDevServer)
            {
                await CachingAsync(host);
            }
            await BridgeBenchAsync(host);

            SampleMemory();
            await PreviewAsync(host);
            await WaitHeadingAsync(host);
        }
        catch (Exception exception)
        {
            Console.WriteLine("SELFTEST failed: " + exception);
            host.ExitCode = 1;
        }

        host.Close();
    }


    public static async Task DiagnoseAsync(DesktopHost host)
    {
        var page = await EvaluateAsync(host, "JSON.stringify({ url: location.href, title: document.title, html: document.documentElement.outerHTML.slice(0, 300), scripts: document.scripts.length })");
        Console.WriteLine("SELFTEST ui-ready timeout; page=" + page);
    }

    /// <summary>--wait-heading "text": announce readiness, then report when the page's h1 shows the text (Fast Refresh probe).</summary>
    private static async Task WaitHeadingAsync(DesktopHost host)
    {
        var args = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(args, "--wait-heading");
        if (index < 0 || index + 1 >= args.Length)
        {
            return;
        }

        Console.WriteLine("SELFTEST waiting-heading");
        var text = args[index + 1].Replace("'", "\'", StringComparison.Ordinal);
        await EvaluateAsync(host, WaitFor($"document.querySelector('h1')?.textContent.includes('{text}')", "true").Replace("5000", "60000", StringComparison.Ordinal), timeoutSeconds: 70);
        Console.WriteLine($"SELFTEST heading-seen epoch={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
    }

    /// <summary>--preview URL: calls links.preview from the page, through the real bridge (outbound HTTPS check).</summary>
    private static async Task PreviewAsync(DesktopHost host)
    {
        var args = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(args, "--preview");
        if (index < 0 || index + 1 >= args.Length)
        {
            return;
        }

        var timer = Stopwatch.StartNew();
        var result = await EvaluateAsync(host, $$"""
            fetch("/_spar/call/links/preview", { method: "POST", headers: { "X-Spar-Contracts": "{{host.Fingerprint}}", "Content-Type": "application/json" },
              body: JSON.stringify({ url: "{{args[index + 1]}}" }) }).then(r => r.status + " " + r.statusText).then(async s => s)
            """, timeoutSeconds: 20);
        var body = await EvaluateAsync(host, $$"""
            fetch("/_spar/call/links/preview", { method: "POST", headers: { "X-Spar-Contracts": "{{host.Fingerprint}}", "Content-Type": "application/json" },
              body: JSON.stringify({ url: "{{args[index + 1]}}" }) }).then(r => r.text())
            """, timeoutSeconds: 20);
        Console.WriteLine($"SELFTEST preview http={result} body={body} ms={timer.Elapsed.TotalMilliseconds:F0}");
    }
    /// <summary>
    /// --telemetry: reports how page telemetry started, clicks Preview (links.preview: page click, call, backend command,
    /// outbound HttpClient), optionally benchmarks calls with tracing on and off (--telemetry-bench), then flushes the
    /// page's exporters unless --no-flush (to see what a close loses).
    /// </summary>
    private static async Task TelemetryAsync(DesktopHost host)
    {
        var args = Environment.GetCommandLineArgs();
        Console.WriteLine("SELFTEST telemetry-startup " + await EvaluateAsync(host, "JSON.stringify(window.sparTelemetryStartup)"));
        var timer = Stopwatch.StartNew();
        await EvaluateAsync(host, "[...document.querySelectorAll('button')].find(b => b.textContent === 'Preview').click()");
        var preview = await EvaluateAsync(host, WaitFor(
            "(document.querySelector('output')?.textContent ?? '').length > 0 && !document.querySelector('output').textContent.startsWith('Loading')",
            "document.querySelector('output').textContent").Replace("5000", "20000", StringComparison.Ordinal), timeoutSeconds: 25);
        Console.WriteLine($"SELFTEST telemetry-preview \"{preview}\" ms={timer.Elapsed.TotalMilliseconds:F0} stats={await EvaluateAsync(host, "JSON.stringify(window.sparTelemetry?.stats ?? null)")}");

        if (args.Contains("--telemetry-bench"))
        {
            for (var round = 1; round <= 3; round++)
            {
                foreach (var transport in new[] { "fetch", "message" })
                {
                    foreach (var tracing in new[] { true, false })
                    {
                        var enabled = await EvaluateAsync(host, $"(() => {{ if (!window.sparTelemetry) return 'off'; window.sparTelemetry.setTracing({(tracing ? "true" : "false")}); return 'on'; }})()");
                        if (enabled == "off" && tracing)
                        {
                            continue;
                        }

                        var result = await EvaluateAsync(host, $"window.sparCallBench({{ transport: '{transport}', n: 300 }}).then(JSON.stringify)", timeoutSeconds: 60);
                        Console.WriteLine($"SELFTEST telemetry-bench round={round} transport={transport} tracing={(enabled == "off" ? "page-telemetry-off" : tracing ? "on" : "off")} {result}");
                    }
                }
            }

            await EvaluateAsync(host, "window.sparTelemetry?.setTracing(true)");
        }

        if (!args.Contains("--no-flush"))
        {
            timer.Restart();
            var flushed = await EvaluateAsync(host, "(window.sparTelemetry?.flush() ?? Promise.resolve(null)).then(String)", timeoutSeconds: 60);
            Console.WriteLine($"SELFTEST telemetry-flush ok={flushed} ms={timer.Elapsed.TotalMilliseconds:F1}");
        }

        var resources = await EvaluateAsync(host, "JSON.stringify(performance.getEntriesByType('resource').map(e => new URL(e.name).pathname).filter(p => p.startsWith('/assets/')))");
        Console.WriteLine($"SELFTEST telemetry-assets {resources}");
    }

    private static async Task JourneyAsync(DesktopHost host)
    {
        var timer = Stopwatch.StartNew();
        await EvaluateAsync(host, "[...document.querySelectorAll('label')].find(l => l.textContent.startsWith('Note')).querySelector('input').focus()");
        await CdpAsync(host, "Input.insertText", Json(w => w.WriteString("text"u8, "Buy milk")));
        await EvaluateAsync(host, "document.querySelector('button[type=submit]').click()");
        var item = await EvaluateAsync(host, WaitFor(
            "document.querySelector('ul[aria-label=Notes] li')?.textContent.includes('Buy milk')",
            "document.querySelector('ul[aria-label=Notes] li').textContent"));
        var status = await EvaluateAsync(host, "document.querySelector('[role=status]').textContent");
        Console.WriteLine($"SELFTEST journey ok={item.Contains("Buy milk", StringComparison.Ordinal)} ms={timer.Elapsed.TotalMilliseconds:F1} item=\"{item}\" status=\"{status}\"");
    }

    private static async Task CachingAsync(DesktopHost host)
    {
        var before = Snapshot(host);
        Console.WriteLine($"SELFTEST first-load requested=[{string.Join(",", before.Keys.Order())}]");
        var ready = host.NextUiReady();
        var timer = Stopwatch.StartNew();
        await CdpAsync(host, "Page.reload", "{}");
        await ready;
        var reload = timer.Elapsed.TotalMilliseconds;
        var after = Snapshot(host);
        var requested = after.Where(a => a.Value > before.GetValueOrDefault(a.Key)).Select(a => a.Key).Order().ToList();
        var notRequested = after.Keys.Except(requested).Order().ToList();
        var entries = await EvaluateAsync(host,
            "JSON.stringify(performance.getEntriesByType('resource').concat(performance.getEntriesByType('navigation')).map(e => [new URL(e.name).pathname, e.transferSize, e.deliveryType]))");
        Console.WriteLine($"SELFTEST reload ms={reload:F1} assets-requested-again=[{string.Join(",", requested)}] served-without-request=[{string.Join(",", notRequested)}]");
        Console.WriteLine($"SELFTEST resource-timing {entries}");
    }

    /// <summary>
    /// --bridge-bench [all|streams|cancel|push|unary|reload]: runs the bench page's measurements through the desktop host's
    /// transports and appends JSON lines to artifacts/react/bridge-bench.jsonl.
    /// </summary>
    private static async Task BridgeBenchAsync(DesktopHost host)
    {
        var args = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(args, "--bridge-bench");
        if (index < 0)
        {
            return;
        }

        var part = index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal) ? args[index + 1] : "all";
        bool Wants(string name) => part == "all" || part == name;
        var runs = args.Contains("--runs") ? int.Parse(args[Array.IndexOf(args, "--runs") + 1], System.Globalization.CultureInfo.InvariantCulture) : 3;
        await OpenBenchAsync(host);
        await CdpAsync(host, "Performance.enable", "{}");

        if (Wants("unary"))
        {
            foreach (var size in new[] { 0, 1024, 65536 })
            {
                foreach (var transport in new[] { "fetch", "message" })
                {
                    await RecordAsync(host, "unary", $"{{transport:'{transport}',n:300,size:{size}}}", metrics: false);
                }
            }
        }

        if (Wants("streams"))
        {
            foreach (var transport in new[] { "fetch", "channel" })
            {
                foreach (var batch in new[] { 1, 100, 1000 })
                {
                    for (var run = 0; run < runs; run++)
                    {
                        await RecordAsync(host, "stream", $"{{rows:100000,batch:{batch},transport:'{transport}'}}", metrics: true);
                    }
                }
            }

            await RecordAsync(host, "stream-keep", "{rows:100000,batch:1000,transport:'channel',keep:true}", metrics: true);
            // Not "fetch": WebView2 buffers the whole response, so a 1M-row stream has no back-pressure at all.
            foreach (var transport in new[] { "channel" })
            {
                foreach (var batch in new[] { 1, 100, 1000 })
                {
                    await RecordAsync(host, "backpressure", $"{{batch:{batch},transport:'{transport}'}}", metrics: false);
                }
            }

            lock (PipeStream.Calls)
            {
                Console.WriteLine($"SELFTEST istream calls=[{string.Join(",", PipeStream.Calls.Select(c => c.Key + "=" + c.Value))}] " +
                    $"reads-on-sta={PipeStream.ReadsOnSta} reads-that-waited={PipeStream.BlockedReads}");
            }
        }

        if (part == "probe")
        {
            await RecordAsync(host, "cancel-stream", "{transport:'fetch',how:'abort',honor:true}", metrics: false);
            await RecordAsync(host, "stream", "{rows:20000,batch:100,transport:'fetch'}", metrics: false);

            // 2,000 rows with a 20 ms pause every 100: at least 380 ms. Incremental delivery shows a first row long before the end.
            await RecordAsync(host, "stream-delayed", "{rows:2000,batch:10,transport:'fetch',delayEveryRows:100,delayMs:20}", metrics: false);
            await RecordAsync(host, "stream-delayed", "{rows:2000,batch:10,transport:'channel',delayEveryRows:100,delayMs:20}", metrics: false);
            lock (PipeStream.Calls)
            {
                Console.WriteLine($"SELFTEST istream calls=[{string.Join(",", PipeStream.Calls.Select(c => c.Key + "=" + c.Value))}] " +
                    $"reads-on-sta={PipeStream.ReadsOnSta} reads-that-waited={PipeStream.BlockedReads}");
            }
        }

        if (Wants("cancel"))
        {
            for (var run = 0; run < 5; run++)
            {
                await RecordAsync(host, "cancel-unary", "{transport:'fetch',honor:true}", metrics: false);
                await RecordAsync(host, "cancel-unary", "{transport:'message',honor:true}", metrics: false);
                await RecordAsync(host, "cancel-stream", "{transport:'channel',how:'abort',honor:true}", metrics: false);
                await RecordAsync(host, "cancel-stream", "{transport:'channel',how:'break',honor:true}", metrics: false);
            }

            await RecordAsync(host, "cancel-unary", "{transport:'message',honor:false,confirmMs:1000}", metrics: false);
            await RecordAsync(host, "cancel-stream", "{transport:'channel',how:'abort',honor:false,tickMs:3000,confirmMs:1000}", metrics: false);
            await RecordAsync(host, "cancel-stream", "{transport:'channel',how:'abort',honor:false,tickMs:1}", metrics: false);
        }

        if (Wants("push"))
        {
            // Not "fetch": a subscription never ends, and WebView2 delivers a response only at its end.
            foreach (var transport in new[] { "channel" })
            {
                foreach (var rate in new[] { 1000, 5000, 20000 })
                {
                    await RecordAsync(host, "push", $"{{transport:'{transport}',rate:{rate},seconds:5}}", metrics: true);
                }

                await RecordAsync(host, "push-slow", $"{{transport:'{transport}',rate:2000,seconds:3,slowConsumerMs:200}}", metrics: false);
            }
        }

        if (Wants("reload"))
        {
            await ReloadAsync(host);
        }
    }

    private static async Task OpenBenchAsync(DesktopHost host)
    {
        await EvaluateAsync(host, "location.href = location.origin + '/bench.html'");
        await Task.Delay(200);
        await EvaluateAsync(host, WaitFor("window.sparBenchReady === true", "true"));
    }

    /// <summary>A subscription, a reload, a new subscription: the old document's subscription must end.</summary>
    private static async Task ReloadAsync(DesktopHost host)
    {
        await EvaluateAsync(host, "window.sparBench.watchForever({transport:'channel',rate:100,resubscribe:false})");
        await EvaluateAsync(host, WaitFor("window.sparBench.watchStatus().events > 10", "true"));
        var before = await EvaluateAsync(host, "window.sparBench.stats()");
        var timer = Stopwatch.StartNew();
        await CdpAsync(host, "Page.reload", "{}");
        await Task.Delay(100);
        await EvaluateAsync(host, WaitFor("window.sparBenchReady === true", "true"));
        var reloaded = timer.Elapsed.TotalMilliseconds;
        var afterReload = await EvaluateAsync(host, "window.sparBench.stats()");
        await EvaluateAsync(host, "window.sparBench.watchForever({transport:'channel',rate:100,resubscribe:false})");
        await EvaluateAsync(host, WaitFor("window.sparBench.watchStatus().events > 10", "true"));
        var resubscribed = timer.Elapsed.TotalMilliseconds;
        var after = await EvaluateAsync(host, "window.sparBench.stats()");
        Append("reload", $"{{\"statsBefore\":{before},\"statsAfterReload\":{afterReload},\"statsAfterResubscribe\":{after}," +
            $"\"reloadToReadyMs\":{reloaded:F0},\"reloadToEventsMs\":{resubscribed:F0}}}", null);
    }

    private static async Task RecordAsync(DesktopHost host, string kind, string options, bool metrics)
    {
        var function = kind switch
        {
            "stream-keep" or "stream-delayed" => "stream",
            "push-slow" => "push",
            "cancel-unary" => "cancelUnary",
            "cancel-stream" => "cancelStream",
            _ => kind,
        };
        var start = metrics ? await MetricsAsync(host) : null;
        var result = await EvaluateAsync(host, $"window.sparBench.{function}({options})", timeoutSeconds: 300);
        string? page = null;
        if (start is not null)
        {
            var end = await MetricsAsync(host);
            double Delta(string name) => (end.GetValueOrDefault(name) - start.GetValueOrDefault(name)) * 1000;
            using var heap = JsonDocument.Parse(await CdpAsync(host, "Runtime.getHeapUsage", "{}"));
            page = $"{{\"taskMs\":{Delta("TaskDuration"):F0},\"scriptMs\":{Delta("ScriptDuration"):F0},\"wallMs\":{Delta("Timestamp"):F0}," +
                $"\"heapUsedMB\":{heap.RootElement.GetProperty("usedSize").GetDouble() / 1048576:F1}}}";
        }

        Append(kind, result, page);
    }

    private static async Task<Dictionary<string, double>> MetricsAsync(DesktopHost host)
    {
        using var metrics = JsonDocument.Parse(await CdpAsync(host, "Performance.getMetrics", "{}"));
        return metrics.RootElement.GetProperty("metrics").EnumerateArray()
            .ToDictionary(m => m.GetProperty("name").GetString()!, m => m.GetProperty("value").GetDouble());
    }

    private static void Append(string kind, string result, string? page)
    {
#if DEBUG
        const string Build = "jit-debug";
#else
        const string Build = "aot-release";
#endif
        var line = $"{{\"kind\":\"{kind}\",\"host\":\"desktop\",\"build\":\"{Build}\",\"result\":{result}{(page is null ? "" : ",\"page\":" + page)}}}";
        Console.WriteLine("SELFTEST bridge " + line);
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")))
            {
                var folder = Path.Combine(directory.FullName, "artifacts", "react");
                Directory.CreateDirectory(folder);
                File.AppendAllLines(Path.Combine(folder, "bridge-bench.jsonl"), [line]);
                return;
            }
        }
    }

    private static void SampleMemory()
    {
        using var self = Process.GetCurrentProcess();
        var started = self.StartTime;
        long working = self.WorkingSet64, privateBytes = self.PrivateMemorySize64;
        var browsers = 0;
        foreach (var process in Process.GetProcessesByName("msedgewebview2"))
        {
            using (process)
            {
                try
                {
                    if (process.StartTime >= started)
                    {
                        browsers++;
                        working += process.WorkingSet64;
                        privateBytes += process.PrivateMemorySize64;
                    }
                }
                catch (InvalidOperationException)
                {
                    // Exited while sampling.
                }
            }
        }

        Console.WriteLine($"SELFTEST memory app-working-set={self.WorkingSet64 / 1024}KB app-private={self.PrivateMemorySize64 / 1024}KB " +
            $"webview-processes={browsers} total-working-set={working / 1024}KB total-private={privateBytes / 1024}KB");
    }

    private static Dictionary<string, int> Snapshot(DesktopHost host)
    {
        lock (host.AssetHits)
        {
            return new Dictionary<string, int>(host.AssetHits);
        }
    }

    private static string WaitFor(string condition, string value) => $$"""
        new Promise((resolve, reject) => {
          const start = performance.now();
          const tick = () => {
            if ({{condition}}) resolve({{value}});
            else if (performance.now() - start > 5000) reject(new Error("timeout: {{condition.Replace("\"", "'", StringComparison.Ordinal)}}"));
            else setTimeout(tick, 2);
          };
          tick();
        })
        """;

    private static async Task<string> EvaluateAsync(DesktopHost host, string expression, int timeoutSeconds = 10)
    {
        var json = await CdpAsync(host, "Runtime.evaluate", Json(w =>
        {
            w.WriteString("expression"u8, expression);
            w.WriteBoolean("awaitPromise"u8, true);
            w.WriteBoolean("returnByValue"u8, true);
        })).WaitAsync(TimeSpan.FromSeconds(timeoutSeconds));
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("exceptionDetails", out var exception))
        {
            throw new InvalidOperationException("Page script failed: " + exception.GetRawText());
        }

        var result = document.RootElement.GetProperty("result");
        return result.TryGetProperty("value", out var value) ? value.ToString() : "";
    }

    private static Task<string> CdpAsync(DesktopHost host, string method, string parameters)
    {
        var handler = new DevToolsCallHandler(method);
        host.WebView.CallDevToolsProtocolMethod(method, parameters, handler);
        return handler.Task;
    }

    private static string Json(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            write(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
#endif
