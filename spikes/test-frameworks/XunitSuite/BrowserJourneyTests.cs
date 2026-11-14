#if SPIKE_BROWSER
using Loop.Tests;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Spike.XunitSuite;

[Trait("Category", "Browser")]
// One class is one xUnit collection, so these two run one after the other by default.
public sealed class BrowserJourneyTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Saves_a_note_through_the_real_backend()
    {
        var token = TestContext.Current.CancellationToken;
        await using var app = await LoopbackApp.StartAsync(token);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync();
        await page.GotoAsync(app.Url);
        await Expect(page.GetByRole(AriaRole.Status)).ToHaveTextAsync("Ready");

        await page.GetByRole(AriaRole.Textbox, new() { Name = "Note" }).FillAsync("Buy milk");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Status)).ToHaveTextAsync("Saved version 1");
        await page.ReloadAsync();
        await Expect(page.GetByRole(AriaRole.List, new() { Name = "Saved notes" })).ToContainTextAsync("Buy milk (v1)");

        Assert.Contains(app.Output, l => l.Contains("notes.save ok", StringComparison.Ordinal));
        output.WriteLine($"browser {browser.Version}, app ready {app.ReadyAfter.TotalMilliseconds:F0} ms");
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

        var status = await page.EvaluateAsync<int>(
            "async () => (await fetch('/_spar/call/notes/list', { method: 'POST', headers: { " +
            "'Authorization': 'Bearer ' + sessionStorage.getItem('spar-token'), 'X-Spar-Contracts': 'stale-client' } })).status");

        Assert.Equal(409, status);
    }
}
#endif
