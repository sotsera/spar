using System.Diagnostics;
using Microsoft.Playwright;

// Spike driver. Output lines start with DRIVER.
//   loopback <app.exe> [--bench]   runs the app with --loopback, opens it in headless Chromium, clicks Preview, saves a
//                                  note, optionally benchmarks calls with tracing on and off, flushes, closes.
//   screenshot <url> <out.png> [--click text]... [--wait text] [--full]   a headless screenshot (the Aspire dashboard).
return args[0] switch
{
    "loopback" => await LoopbackAsync(args[1], args.Contains("--bench"), args.Contains("--no-flush")),
    "screenshot" => await ScreenshotAsync(args[1], args[2], args),
    _ => 2,
};

static async Task<int> LoopbackAsync(string app, bool bench, bool noFlush)
{
    var start = new ProcessStartInfo(Path.GetFullPath(app), "--loopback --exit-with-stdin")
    {
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        UseShellExecute = false,
    };
    using var process = Process.Start(start)!;
    var ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var otlpLines = 0;
    process.OutputDataReceived += (_, e) =>
    {
        if (e.Data is null)
        {
            return;
        }

        if (e.Data.StartsWith("SPAR_READY url=", StringComparison.Ordinal))
        {
            ready.TrySetResult(e.Data["SPAR_READY url=".Length..].Split(' ')[0]);
        }

        if (e.Data.StartsWith("SPAR_OTLP", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref otlpLines);
            Console.WriteLine("APP " + e.Data);
        }
        else if (e.Data.StartsWith("SPAR_OTLP_RELAY_FAILED", StringComparison.Ordinal))
        {
            Console.WriteLine("APP " + e.Data);
        }
    };
    process.BeginOutputReadLine();
    var url = await ready.Task.WaitAsync(TimeSpan.FromSeconds(30));

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
    var page = await browser.NewPageAsync();
    var requests = new List<string>();
    page.Request += (_, request) => requests.Add($"{request.Method} {new Uri(request.Url).AbsolutePath}");
    var timer = Stopwatch.StartNew();
    await page.GotoAsync(url);
    await page.WaitForFunctionAsync("() => window.sparTelemetryStartup !== undefined");
    var startup = await page.EvaluateAsync<string>("() => JSON.stringify(window.sparTelemetryStartup)");
    Console.WriteLine($"DRIVER startup {startup} at={timer.ElapsedMilliseconds}ms");
    await page.Locator("p[role=status]").WaitForAsync();

    timer.Restart();
    await page.GetByRole(AriaRole.Button, new() { Name = "Preview" }).ClickAsync();
    await page.WaitForFunctionAsync("() => { const t = document.querySelector('output')?.textContent ?? ''; return t.length > 0 && !t.startsWith('Loading'); }",
        null, new() { Timeout = 20_000 });
    var preview = await page.Locator("output").TextContentAsync();
    var stats = await page.EvaluateAsync<string>("() => JSON.stringify(window.sparTelemetry?.stats ?? null)");
    Console.WriteLine($"DRIVER preview \"{preview}\" ms={timer.ElapsedMilliseconds} stats={stats}");

    await page.GetByRole(AriaRole.Textbox, new() { Name = "Note", Exact = true }).FillAsync("Buy milk");
    await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
    await page.Locator("ul[aria-label=Notes] li", new() { HasText = "Buy milk" }).WaitForAsync();
    stats = await page.EvaluateAsync<string>("() => JSON.stringify(window.sparTelemetry?.stats ?? null)");
    Console.WriteLine($"DRIVER saved stats={stats}");

    if (bench && await page.EvaluateAsync<bool>("() => typeof window.sparCallBench === 'function'"))
    {
        for (var round = 1; round <= 3; round++)
        {
            foreach (var tracing in new[] { true, false })
            {
                var hasTelemetry = await page.EvaluateAsync<bool>($"() => {{ if (!window.sparTelemetry) return false; window.sparTelemetry.setTracing({(tracing ? "true" : "false")}); return true; }}");
                if (!hasTelemetry && tracing)
                {
                    continue;
                }

                var result = await page.EvaluateAsync<string>("async () => JSON.stringify(await window.sparCallBench({ transport: 'fetch', n: 300 }))");
                Console.WriteLine($"DRIVER bench round={round} transport=fetch tracing={(hasTelemetry ? tracing : "page-telemetry-off")} {result}");
            }
        }

        await page.EvaluateAsync("() => window.sparTelemetry?.setTracing(true)");
    }

    if (!noFlush)
    {
        timer.Restart();
        await page.EvaluateAsync("async () => { await window.sparTelemetry?.flush(); }");
        Console.WriteLine($"DRIVER flush ms={timer.ElapsedMilliseconds}");
    }
    var otlpRequests = requests.Count(r => r.Contains("/_spar/otlp/", StringComparison.Ordinal));
    Console.WriteLine($"DRIVER assets [{string.Join(", ", requests.Where(r => r.StartsWith("GET", StringComparison.Ordinal)).Distinct())}]");
    Console.WriteLine($"DRIVER page-requests total={requests.Count} otlp={otlpRequests} [{string.Join(", ", requests.Where(r => r.Contains("/_spar/otlp", StringComparison.Ordinal)).GroupBy(r => r).Select(g => $"{g.Key} x{g.Count()}"))}]");

    // Without a flush, closing the tab is the browser SDK's own case: it flushes on pagehide/visibilitychange.
    if (noFlush && Environment.GetEnvironmentVariable("DRIVER_NAVIGATE_AWAY") == "1")
    {
        // Navigating away fires pagehide and visibilitychange, which the SDK flushes on with keepalive fetches.
        await page.GotoAsync("about:blank");
        await Task.Delay(1000);
    }

    await page.CloseAsync(new() { RunBeforeUnload = noFlush });
    await Task.Delay(noFlush ? 1000 : 0);
    await browser.CloseAsync();
    process.StandardInput.Close();
    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
    Console.WriteLine($"DRIVER app-exit code={process.ExitCode} host-otlp-lines={otlpLines}");
    return 0;
}

static async Task<int> ScreenshotAsync(string url, string output, string[] args)
{
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
    var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1600, Height = 1000 } });
    await page.GotoAsync(url);
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == "--click")
        {
            await page.GetByText(args[i + 1]).First.ClickAsync(new() { Timeout = 15_000 });
            await page.WaitForTimeoutAsync(1500);
        }
        else if (args[i] == "--wait")
        {
            await page.GetByText(args[i + 1]).First.WaitForAsync(new() { Timeout = 30_000 });
        }
    }

    await page.WaitForTimeoutAsync(1500);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    await page.ScreenshotAsync(new() { Path = output, FullPage = args.Contains("--full") });
    Console.WriteLine($"DRIVER screenshot {output} title=\"{await page.TitleAsync()}\" url={page.Url}");
    var text = await page.InnerTextAsync("body");
    Console.WriteLine("DRIVER page-text " + text.Replace('\n', ' ')[..Math.Min(3000, text.Length)]);
    return 0;
}
