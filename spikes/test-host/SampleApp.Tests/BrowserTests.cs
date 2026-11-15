#if SPIKE_BROWSER
using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using SampleApp.Notes;
using Spar.Testing;

[assembly: AssemblyFixture(typeof(SampleApp.Tests.SharedBrowser))]

namespace SampleApp.Tests;

/// <summary>One Chromium for the whole test run; each test gets its own context (cookies, storage, cache).</summary>
public sealed class SharedBrowser : IAsyncLifetime
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public TimeSpan LaunchedAfter { get; private set; }

    public IBrowser Browser => _browser ?? throw new InvalidOperationException("Not started.");

    public IBrowserType Chromium => _playwright!.Chromium;

    public async ValueTask InitializeAsync()
    {
        var clock = Stopwatch.StartNew();
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
        LaunchedAfter = clock.Elapsed;
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.DisposeAsync();
        }

        _playwright?.Dispose();
    }
}

/// <summary>Browser level: the app's backend in this process, its page in Chromium over loopback.</summary>
public sealed class BrowserTests(SharedBrowser shared, ITestOutputHelper output)
{
    [Fact]
    public async Task A_journey_in_chromium()
    {
        await using var app = await TestApps.StartAsync(SparTestSurface.Loopback, orders: 5);
        await using var context = await shared.Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        await JourneyAsync(app, page, "from chromium");

        Assert.Equal("5", await page.Locator("#orders").TextContentAsync());
        Assert.Equal(["from chromium"], app.Services.GetRequiredService<INoteStore>().All().Select(n => n.Text));
        output.WriteLine($"SPIKE browser launched once in {shared.LaunchedAfter.TotalMilliseconds:F0}ms");
    }

    [Fact]
    public async Task Ten_journeys_on_one_browser()
    {
        var timings = new List<double>();
        for (var i = 0; i < 10; i++)
        {
            var clock = Stopwatch.StartNew();
            await using var app = await TestApps.StartAsync(SparTestSurface.Loopback);
            await using var context = await shared.Browser.NewContextAsync();
            await JourneyAsync(app, await context.NewPageAsync(), $"note {i}");
            timings.Add(clock.Elapsed.TotalMilliseconds);
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"SPIKE shared browser, app+context+journey ms: first={timings[0]:F0} median-rest={CommandTests.Median(timings.Skip(1)):F0} max-rest={timings.Skip(1).Max():F0}"));
    }

    [Fact]
    public async Task Five_journeys_with_a_browser_each()
    {
        var timings = new List<double>();
        for (var i = 0; i < 5; i++)
        {
            var clock = Stopwatch.StartNew();
            await using var app = await TestApps.StartAsync(SparTestSurface.Loopback);
            await using var browser = await shared.Chromium.LaunchAsync(new() { Headless = true });
            var page = await browser.NewPageAsync();
            await JourneyAsync(app, page, $"note {i}");
            timings.Add(clock.Elapsed.TotalMilliseconds);
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"SPIKE browser per test, app+launch+journey ms: median={CommandTests.Median(timings):F0} max={timings.Max():F0}"));
    }

    private static async Task JourneyAsync(SparTestApp app, IPage page, string text)
    {
        await page.GotoAsync(app.Url!);
        Assert.Equal("loopback", await page.EvaluateAsync<string>("() => window.sparReady"));
        await page.GetByLabel("Note text").FillAsync(text);
        await page.GetByRole(AriaRole.Button, new() { Name = "Add" }).ClickAsync();
        await page.Locator("#notes li", new() { HasText = text }).WaitForAsync();
    }
}
#endif
