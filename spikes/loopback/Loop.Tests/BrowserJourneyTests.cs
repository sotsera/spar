using System.Diagnostics;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Loop.Tests;

public sealed class BrowserJourneyTests(ITestOutputHelper output)
{
    public static TheoryData<string> Browsers => ["chromium-headless-shell", "msedge-headless"];

    [Theory]
    [MemberData(nameof(Browsers))]
    public async Task Saves_a_note_through_the_real_backend(string browserKind)
    {
        if (browserKind == "msedge-headless" && !OperatingSystem.IsWindows())
        {
            Assert.Skip("Microsoft Edge is only used on Windows.");
        }

        var token = TestContext.Current.CancellationToken;
        var total = Stopwatch.StartNew();
        await using var app = await LoopbackApp.StartAsync(token);

        using var playwright = await Playwright.CreateAsync();
        var launchStart = Stopwatch.StartNew();
        await using var browser = browserKind == "msedge-headless"
            ? await playwright.Chromium.LaunchAsync(new() { Channel = "msedge", Headless = true })
            : await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var launch = launchStart.Elapsed;

        var page = await browser.NewPageAsync();
        var pageStart = Stopwatch.StartNew();
        await page.GotoAsync(app.Url);
        await Expect(page.GetByRole(AriaRole.Status)).ToHaveTextAsync("Ready");
        var uiReady = await app.WaitForOutputAsync("SPAR_UI_READY", token);
        var pageReady = pageStart.Elapsed;

        var journey = Stopwatch.StartNew();
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Note" }).FillAsync("Buy milk");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Status)).ToHaveTextAsync("Saved version 1");
        await Expect(page.GetByRole(AriaRole.List, new() { Name = "Saved notes" })).ToContainTextAsync("Buy milk (v1)");

        // A reload keeps working: the token moved from the URL fragment into session storage.
        await page.ReloadAsync();
        await Expect(page.GetByRole(AriaRole.List, new() { Name = "Saved notes" })).ToContainTextAsync("Buy milk (v1)");
        var journeyTime = journey.Elapsed;

        Assert.Contains(app.Output, l => l.Contains("notes.save ok", StringComparison.Ordinal));
        var line =
            $"{DateTime.Now:HH:mm:ss} {browserKind} ({browser.Version}): app ready {app.ReadyAfter.TotalMilliseconds:F0} ms, " +
            $"browser launch {launch.TotalMilliseconds:F0} ms, page to ui-ready {pageReady.TotalMilliseconds:F0} ms ({uiReady}), " +
            $"journey {journeyTime.TotalMilliseconds:F0} ms, total {total.Elapsed.TotalMilliseconds:F0} ms";
        output.WriteLine(line);
        File.AppendAllLines(TimingsFile(), [line]);
    }

    // Spike evidence: Microsoft.Testing.Platform hides passing tests' output, so keep timings in artifacts/.
    private static string TimingsFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        var folder = Path.Combine(directory!.FullName, "artifacts", "loopback");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "browser-timings.txt");
    }

    [Fact]
    public async Task A_page_built_from_other_contracts_is_rejected()
    {
        var token = TestContext.Current.CancellationToken;
        await using var app = await LoopbackApp.StartAsync(token);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync();
        await page.GotoAsync(app.Url);
        await Expect(page.GetByRole(AriaRole.Status)).ToHaveTextAsync("Ready");

        // A stale page: the backend restarted with different contracts while the page kept its old client.
        var status = await page.EvaluateAsync<int>(
            "async () => (await fetch('/_spar/call/notes/list', { method: 'POST', headers: { 'Authorization': 'Bearer ' + sessionStorage.getItem('spar-token'), 'X-Spar-Contracts': 'stale-client' } })).status");

        Assert.Equal(409, status);
    }

    [Fact]
    public async Task The_bridge_rejects_calls_without_the_run_token()
    {
        var token = TestContext.Current.CancellationToken;
        await using var app = await LoopbackApp.StartAsync(token);
        using var http = new HttpClient();
        var response = await http.PostAsync($"{app.BaseAddress}/_spar/call/notes/list", new StringContent(""), token);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
