#:package Microsoft.Playwright
#:property PublishAot=false
// Checks whether Playwright for .NET can attach to D8.App launched with --remote-debugging-port, how the port is
// discovered, and which races exist. Usage: dotnet run playwright-attach.cs -- <path to D8.App.exe> <scenario>
// Scenarios: attach (port=0, fresh profile, measure), stale (port file left by a previous run),
//            shared (two apps, one profile), fixed (two apps, two profiles, same fixed port).
using System.Diagnostics;
using System.Net.Http;
using Microsoft.Playwright;

var exe = Path.GetFullPath(args[0]);
var scenario = args.Length > 1 ? args[1] : "attach";
var spikeRoot = Path.GetDirectoryName(Path.GetFullPath("playwright-attach.cs"))!;
var profiles = Path.Combine(spikeRoot, "artifacts", "webview2-profiles");
var launched = new List<Process>();

try
{
    switch (scenario)
    {
        case "attach":
            await Attach();
            break;
        case "stale":
            await Stale();
            break;
        case "shared":
            await Shared();
            break;
        case "fixed":
            await Fixed();
            break;
        default:
            throw new ArgumentException(scenario);
    }
}
finally
{
    foreach (var process in launched)
    {
        Close(process);
    }
}

async Task Attach()
{
    var profile = NewProfile("pw-attach");
    var clock = Stopwatch.StartNew();
    var app = Launch(profile, "--remote-debugging-port=0");
    var (port, discoveredMs, reads) = await WaitForPort(profile, null, clock);
    Console.WriteLine($"DevToolsActivePort: port {port} after {discoveredMs:F0} ms ({reads} polls)");

    using var http = new HttpClient();
    var version = await http.GetStringAsync($"http://127.0.0.1:{port}/json/version");
    Console.WriteLine("unauthenticated /json/version: " + version.ReplaceLineEndings(" ")[..Math.Min(200, version.Length)]);

    using var playwright = await Playwright.CreateAsync();
    var start = Stopwatch.GetTimestamp();
    var browser = await playwright.Chromium.ConnectOverCDPAsync($"http://127.0.0.1:{port}");
    Console.WriteLine($"ConnectOverCDP: {Ms(start):F1} ms; contexts={browser.Contexts.Count}");

    var page = await FindPage(browser);
    Console.WriteLine($"page found: '{await page.TitleAsync()}' at {clock.ElapsedMilliseconds} ms since launch");

    var fill = new List<double>();
    var click = new List<double>();
    var wait = new List<double>();
    var shot = new List<double>();
    var aria = new List<double>();
    for (var i = 0; i < 20; i++)
    {
        var text = $"pw-{i}";
        start = Stopwatch.GetTimestamp();
        await page.GetByLabel("Name").FillAsync(text);
        fill.Add(Ms(start));
        start = Stopwatch.GetTimestamp();
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        click.Add(Ms(start));
        start = Stopwatch.GetTimestamp();
        await page.GetByRole(AriaRole.Status).GetByText($": {text} (").WaitForAsync();
        wait.Add(Ms(start));
        start = Stopwatch.GetTimestamp();
        await page.ScreenshotAsync(new() { Path = Path.Combine(spikeRoot, "artifacts", "results", "playwright.png") });
        shot.Add(Ms(start));
        start = Stopwatch.GetTimestamp();
        await page.Locator("body").AriaSnapshotAsync();
        aria.Add(Ms(start));
    }

    Console.WriteLine("status: " + await page.GetByRole(AriaRole.Status).TextContentAsync());
    Report("fill", fill);
    Report("click", click);
    Report("waitFor", wait);
    Report("screenshot", shot);
    Report("ariaSnapshot", aria);

    await browser.CloseAsync();
    Close(app);
    Console.WriteLine($"after exit, DevToolsActivePort exists: {File.Exists(PortFile(profile))}");
}

async Task Stale()
{
    // Run 1 leaves the port file behind (if Chromium does not delete it). Run 2 reuses the profile; a client that
    // reads the file before the new browser rewrites it gets the old port.
    var profile = NewProfile("pw-stale");
    var clock = Stopwatch.StartNew();
    var first = Launch(profile, "--remote-debugging-port=0");
    {
        var (port, ms, _) = await WaitForPort(profile, null, clock);
        Console.WriteLine($"run 1: port {port} after {ms:F0} ms");
        Close(first);
    }

    var stale = File.Exists(PortFile(profile)) ? File.ReadAllLines(PortFile(profile))[0] : null;
    Console.WriteLine($"after run 1 exit: port file present={stale is not null} content={stale}");

    clock.Restart();
    var second = Launch(profile, "--remote-debugging-port=0");
    var immediate = File.Exists(PortFile(profile)) ? File.ReadAllLines(PortFile(profile))[0] : null;
    Console.WriteLine($"run 2, immediately after launch: file says {immediate ?? "(none)"}");
    if (immediate is not null)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        try
        {
            await http.GetStringAsync($"http://127.0.0.1:{immediate}/json/version");
            Console.WriteLine("  stale port answered (something else is listening there!)");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"  stale port probe failed: {exception.GetType().Name}");
        }
    }

    var (newPort, newMs, polls) = await WaitForPort(profile, immediate, clock);
    Console.WriteLine($"run 2: new port {newPort} after {newMs:F0} ms ({polls} polls)");
    Close(second);
}

async Task Shared()
{
    // Two app processes, one user-data folder: WebView2 shares one browser process between them.
    var profile = NewProfile("pw-shared");
    var clock = Stopwatch.StartNew();
    var a = Launch(profile, "--remote-debugging-port=0");
    var (port, _, _) = await WaitForPort(profile, null, clock);
    var b = Launch(profile, "--remote-debugging-port=0");
    await Task.Delay(3000);
    var after = File.ReadAllLines(PortFile(profile))[0];
    Console.WriteLine($"app A port {port}; after app B started the file says {after}; B exited early: {b.HasExited}");
    using var playwright = await Playwright.CreateAsync();
    var browser = await playwright.Chromium.ConnectOverCDPAsync($"http://127.0.0.1:{port}");
    var pages = browser.Contexts.SelectMany(c => c.Pages).ToList();
    Console.WriteLine($"pages visible on one port: {pages.Count} (both apps' pages share the endpoint)");
    await browser.CloseAsync();
    Close(b);
    Close(a);
}

async Task Fixed()
{
    // The previous experiment's shape: pick a fixed/free port up front and pass it. Two launches collide.
    const int port = 9333;
    var profileA = NewProfile("pw-fixed-a");
    var profileB = NewProfile("pw-fixed-b");
    var clock = Stopwatch.StartNew();
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    var a = Launch(profileA, $"--remote-debugging-port={port}");

    // A fixed port writes no DevToolsActivePort file, so the client can only probe the port it chose.
    string? browserA = null;
    while (browserA is null && clock.Elapsed < TimeSpan.FromSeconds(20))
    {
        try
        {
            browserA = await http.GetStringAsync($"http://127.0.0.1:{port}/json/version");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            await Task.Delay(50);
        }
    }

    Console.WriteLine($"A answers on {port} after {clock.ElapsedMilliseconds} ms; A port file: {Read(profileA) ?? "(none)"}");
    var b = Launch(profileB, $"--remote-debugging-port={port}");
    await Task.Delay(4000);
    var answering = await http.GetStringAsync($"http://127.0.0.1:{port}/json/version");
    Console.WriteLine($"B started: B exited={b.HasExited}; B port file: {Read(profileB) ?? "(none)"}; " +
        $"port {port} still served by A's browser: {answering == browserA}");
    var list = await http.GetStringAsync($"http://127.0.0.1:{port}/json/list");
    Console.WriteLine($"/json/list on {port} has {list.Split("\"type\": \"page\"").Length - 1} page(s): " +
        "B's page has no debugging endpoint and nothing tells B or the client that the bind failed");
    Close(a);
    await Task.Delay(1500);
    try
    {
        await http.GetStringAsync($"http://127.0.0.1:{port}/json/version");
        Console.WriteLine($"after A exits, {port} still answers (B does not take it over? check)");
    }
    catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
    {
        Console.WriteLine($"after A exits, {port} is dead although B is still running: {!b.HasExited}");
    }

    Close(b);

    static string? Read(string profile) => File.Exists(PortFile(profile)) ? File.ReadAllLines(PortFile(profile))[0] : null;
}

Process Launch(string profile, string browserArguments)
{
    var info = new ProcessStartInfo(exe)
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
    // Profile name is relative to <spike>/artifacts/webview2-profiles.
    foreach (var arg in (string[])["--mode", "show", "--window", "offscreen", "--profile", Path.GetFileName(profile)])
    {
        info.ArgumentList.Add(arg);
    }

    info.Environment["WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS"] = browserArguments;
    var process = Process.Start(info)!;
    launched.Add(process);
    process.OutputDataReceived += (_, _) => { };
    process.ErrorDataReceived += (_, _) => { };
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
    return process;
}

static void Close(Process process)
{
    process.Refresh();
    if (!process.HasExited)
    {
        process.CloseMainWindow();
        if (!process.WaitForExit(10_000))
        {
            process.Kill();
        }
    }
}

string NewProfile(string prefix)
{
    var path = Path.Combine(profiles, $"{prefix}-{DateTime.Now:HHmmssfff}");
    Directory.CreateDirectory(path);
    return path;
}

static string PortFile(string profile) => Path.Combine(profile, "EBWebView", "DevToolsActivePort");

static async Task<(int Port, double Ms, int Polls)> WaitForPort(string profile, string? notThis, Stopwatch clock)
{
    var polls = 0;
    while (clock.Elapsed < TimeSpan.FromSeconds(20))
    {
        polls++;
        try
        {
            if (File.Exists(PortFile(profile)))
            {
                var lines = await File.ReadAllLinesAsync(PortFile(profile));
                if (lines.Length > 0 && lines[0].Length > 0 && lines[0] != notThis)
                {
                    return (int.Parse(lines[0]), clock.Elapsed.TotalMilliseconds, polls);
                }
            }
        }
        catch (IOException)
        {
            // Being written.
        }

        await Task.Delay(10);
    }

    throw new TimeoutException("DevToolsActivePort did not appear under " + profile);
}

static async Task<IPage> FindPage(IBrowser browser)
{
    for (var i = 0; i < 200; i++)
    {
        foreach (var context in browser.Contexts)
        {
            foreach (var page in context.Pages)
            {
                if (await page.TitleAsync() == "D8 automation spike")
                {
                    return page;
                }
            }
        }

        await Task.Delay(25);
    }

    throw new TimeoutException("Spike page not visible over CDP.");
}

static double Ms(long start) => Stopwatch.GetElapsedTime(start).TotalMilliseconds;

static void Report(string name, List<double> samples)
{
    var sorted = samples.Order().ToArray();
    Console.WriteLine($"{name,-14} n={sorted.Length} min={sorted[0]:F2} median={sorted[sorted.Length / 2]:F2} " +
        $"p95={sorted[(int)Math.Ceiling(0.95 * sorted.Length) - 1]:F2} max={sorted[^1]:F2} (first={samples[0]:F2})");
}
