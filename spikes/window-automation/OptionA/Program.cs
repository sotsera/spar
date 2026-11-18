using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.Playwright;
using Spar.WindowAutomation;

[assembly: SupportedOSPlatform("windows10.0")]

// Option A: window tests driven by Microsoft.Playwright, attached with ConnectOverCDPAsync to the WebView2 browser that
// the test process started with --remote-debugging-port=0 (Test variant only). Verbs:
//   journey --runs N [--screenshots] [--hold-ms M]   the orders journey, one InPrivate controller per test
//   coverage                                          the library's features against WebView2, isolation, interference
//   browser --runs N                                  the same journey on the Browser surface (headless Chromium)
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var processStarted = Process.GetCurrentProcess().StartTime.ToUniversalTime();
var options = new Options(args.Skip(1));
View.PageFolder = Paths.Page;
Directory.CreateDirectory(Paths.Output);
var verb = args.FirstOrDefault();
if (verb == "browser")
{
    return await BrowserSurface.RunAsync(options);
}

Ui.Start();
var clock = Stopwatch.StartNew();
var root = Paths.NewRoot("a");
var env = await Env.CreateAsync(root, remoteDebugging: true);
var envMs = clock.Elapsed.TotalMilliseconds;
var port = await env.DevToolsPortAsync(TimeSpan.FromSeconds(10));
var portMs = clock.Elapsed.TotalMilliseconds;
var playwright = await Playwright.CreateAsync();
var driverMs = clock.Elapsed.TotalMilliseconds;
var browser = await playwright.Chromium.ConnectOverCDPAsync($"http://127.0.0.1:{port}");
var connectMs = clock.Elapsed.TotalMilliseconds;
Log.Write("SETUP", $"env+anchor={envMs:F1}ms port-file={portMs - envMs:F1}ms driver-start={driverMs - portMs:F1}ms connect={connectMs - driverMs:F1}ms " +
    $"total={connectMs:F1}ms since-process-start={(DateTime.UtcNow - processStarted).TotalMilliseconds:F0}ms port={port} contexts={browser.Contexts.Count} " +
    $"version={browser.Version}");
Log.Write("MEMORY", $"driver={DriverMemory()} test-process={Process.GetCurrentProcess().PrivateMemorySize64 / 1048576.0:F1}MB");
var exitCode = 0;
try
{
    exitCode = verb switch
    {
        "journey" => await JourneyRunsAsync(),
        "actions" => await ActionsAsync(),
        "coverage" => await Coverage.RunAsync(env, browser, port, options.Has("new-targets")),
        _ => 64,
    };
}
finally
{
    var disposeStarted = Stopwatch.GetTimestamp();
    playwright.Dispose();
    Log.Write("TEARDOWN", $"playwright disposed in {Stopwatch.GetElapsedTime(disposeStarted).TotalMilliseconds:F1}ms; browser exited already={env.Exited.Task.IsCompleted}");
    await env.CloseAsync();
    await env.Exited.Task.WaitAsync(TimeSpan.FromSeconds(10)).ContinueWith(_ => { });
    var portFile = Path.Combine(root, "EBWebView", "DevToolsActivePort");
    Log.Write("TEARDOWN", $"browser exited={env.Exited.Task.IsCompleted} leftover={Native.BrowserProcesses(env.Marker).Count} port-file-left={File.Exists(portFile)}");
    if (!options.Has("keep-root"))
    {
        Log.Write("TEARDOWN", $"root deleted={await Paths.DeleteRootAsync(root)}");
    }

    Ui.Stop();
}

return exitCode;

// The library's per-action costs on the same page, for comparison with Option B's breakdown.
async Task<int> ActionsAsync()
{
    var view = await View.CreateAsync(env);
    var token = Guid.NewGuid().ToString("N");
    await view.NavigateAsync($"{View.Origin}/?test={token}");
    var page = await Attach.FindPageAsync(browser, token);
    await page.EvaluateAsync("new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))");
    var t = new Timings();
    var actions = page.GetByRole(AriaRole.Button, new() { Name = "Actions" });
    for (var i = 0; i < 100; i++)
    {
        await t.TimeAsync("count by role", () => actions.CountAsync());
        await t.TimeAsync("count by label", () => page.GetByLabel("Customer name").CountAsync());
        await t.TimeAsync("mouse moved", () => page.Mouse.MoveAsync(5, 5));
        await t.TimeAsync("mouse click (empty area)", () => page.Mouse.ClickAsync(5, 5));
        await t.TimeAsync("key press", () => page.Keyboard.PressAsync("Escape"));
        await t.TimeAsync("evaluate 1+1", () => page.EvaluateAsync("1+1"));
        await t.TimeAsync("full ClickAsync (open+close menu)", async () =>
        {
            await actions.ClickAsync();
            await page.Keyboard.PressAsync("Escape");
        });
        await t.TimeAsync("fill", () => page.GetByLabel("Customer name").FillAsync("x" + i));
        await t.TimeAsync("expect text (already true)", () => Microsoft.Playwright.Assertions.Expect(page.GetByRole(AriaRole.Status)).ToHaveTextAsync("Draft"));
        await t.TimeAsync("screenshot (viewport)", () => page.ScreenshotAsync());
        await t.TimeAsync("aria snapshot (form)", () => page.GetByRole(AriaRole.Form).AriaSnapshotAsync());
    }

    t.Report("ACTION");
    await view.CloseAsync();
    return 0;
}

async Task<int> JourneyRunsAsync()
{
    var runs = options.Int("runs", 10);
    var steps = new Timings();
    var test = new Timings();
    var failures = 0;
    for (var run = 0; run < runs; run++)
    {
        var started = Stopwatch.GetTimestamp();
        var view = await test.TimeAsync("create controller", () => View.CreateAsync(env));
        try
        {
            var token = Guid.NewGuid().ToString("N");
            await test.TimeAsync("navigate", () => view.NavigateAsync($"{View.Origin}/?test={token}"));
            var page = await test.TimeAsync("find page", () => Attach.FindPageAsync(browser, token));
            var errors = new List<string>();
            page.PageError += (_, error) => errors.Add(error);
            var screenshot = options.Has("screenshots") && run == 0 ? Path.Combine(Paths.Output, "a-settings.png") : null;
            await test.TimeAsync("journey", () => Journey.RunAsync(page, steps, screenshot));
            if (errors.Count > 0)
            {
                throw new InvalidOperationException("page errors: " + string.Join(" | ", errors));
            }

            if (options.Int("hold-ms", 0) is > 0 and var hold)
            {
                Log.Write("HOLD", $"port={port} holding {hold} ms with the page open");
                await Task.Delay(hold);
            }
        }
        catch (Exception exception)
        {
            failures++;
            Log.Write("FAIL", $"run={run} {exception.GetType().Name}: {exception.Message.ReplaceLineEndings(" ")}");
        }
        finally
        {
            await test.TimeAsync("close", view.CloseAsync);
            test.Add("per test", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        if (run == 0)
        {
            Log.Write("MEMORY", $"browser={Native.BrowserPrivateBytes(env.Marker) / 1048576.0:F1}MB driver={DriverMemory()} test-process={Process.GetCurrentProcess().PrivateMemorySize64 / 1048576.0:F1}MB");
        }
    }

    test.Report("TEST");
    steps.Report("STEP");
    Log.Write("RESULT", $"runs={runs} failures={failures} pages-left={browser.Contexts.Sum(c => c.Pages.Count)}");
    return failures == 0 ? 0 : 1;
}

static string DriverMemory()
{
    var parts = new List<string>();
    foreach (var node in Process.GetProcessesByName("node"))
    {
        using (node)
        {
            try
            {
                if (node.MainModule?.FileName.Contains(".playwright", StringComparison.OrdinalIgnoreCase) == true && node.StartTime > Process.GetCurrentProcess().StartTime)
                {
                    parts.Add($"node pid={node.Id} private={node.PrivateMemorySize64 / 1048576.0:F1}MB ws={node.WorkingSet64 / 1048576.0:F1}MB");
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
        }
    }

    return parts.Count == 0 ? "none found" : string.Join("; ", parts);
}

/// <summary>Finding the test's own page among the browser's pages.</summary>
internal static class Attach
{
    /// <summary>By the unique token the host put in the URL; polls until the library has attached to the target.</summary>
    public static async Task<IPage> FindPageAsync(IBrowser browser, string token, int timeoutMs = 10000)
    {
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            foreach (var context in browser.Contexts)
            {
                foreach (var page in context.Pages)
                {
                    if (page.Url.Contains(token, StringComparison.Ordinal))
                    {
                        return page;
                    }
                }
            }

            if (Stopwatch.GetElapsedTime(started).TotalMilliseconds > timeoutMs)
            {
                throw new TimeoutException("The library never saw the test's page.");
            }

            await Task.Delay(2);
        }
    }

    /// <summary>The page's DevTools target id, through a CDP session of the library's own.</summary>
    public static async Task<string> TargetIdAsync(IPage page)
    {
        var session = await page.Context.NewCDPSessionAsync(page);
        var info = await session.SendAsync("Target.getTargetInfo");
        await session.DetachAsync();
        return info!.Value.GetProperty("targetInfo").GetProperty("targetId").GetString()!;
    }
}
