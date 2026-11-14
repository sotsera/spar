using Loop.Tests;
using Microsoft.Playwright;

namespace React.Tests;

public sealed class CachingTests
{
    [Fact]
    public async Task Packaged_assets_are_never_served_from_the_browser_cache()
    {
        var token = TestContext.Current.CancellationToken;
        await using var app = await LoopbackApp.StartAsync(token);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync();

        // Network.requestServedFromCache reports responses the browser took from its HTTP cache.
        var session = await page.Context.NewCDPSessionAsync(page);
        var fromCache = 0;
        session.Event("Network.requestServedFromCache").OnEvent += (_, _) => Interlocked.Increment(ref fromCache);
        await session.SendAsync("Network.enable");

        var assetHeaders = new List<string>();
        page.Response += (_, response) =>
        {
            if (!response.Url.Contains("/_spar/", StringComparison.Ordinal))
            {
                assetHeaders.Add($"{new Uri(response.Url).AbsolutePath} {response.Headers.GetValueOrDefault("cache-control")}");
            }
        };

        await page.GotoAsync(app.Url);
        await page.GetByLabel("Note count").WaitForAsync();
        await page.ReloadAsync();
        await page.GetByLabel("Note count").WaitForAsync();

        Assert.NotEmpty(assetHeaders);
        Assert.All(assetHeaders, h => Assert.EndsWith(" no-store", h, StringComparison.Ordinal));
        Assert.Equal(0, fromCache);
    }
}
