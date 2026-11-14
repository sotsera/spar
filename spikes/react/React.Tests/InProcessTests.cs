using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Playwright;
using React.App;
using static Microsoft.Playwright.Assertions;

namespace React.Tests;

public sealed class InProcessTests
{
    [Fact]
    public async Task Runs_the_real_app_in_process_with_a_fake_clock()
    {
        var token = TestContext.Current.CancellationToken;
        var total = Stopwatch.StartNew();
        await using var app = await InProcessApp.StartAsync(
            services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new FixedTime(new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero)))),
            token);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync();

        var pageStart = Stopwatch.StartNew();
        await page.GotoAsync(app.Url);
        await Expect(page.GetByLabel("Note count")).ToHaveTextAsync("0 notes");
        var pageReady = pageStart.Elapsed;

        var journey = Stopwatch.StartNew();
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Note" }).FillAsync("Buy milk");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Listitem)).ToContainTextAsync("2030-01-02");
        var journeyTime = journey.Elapsed;

        // In-process: the test reads backend state directly, without a command or a log line.
        var notes = app.Services.GetRequiredService<NotesService>();
        var stored = Assert.Single(await notes.SearchAsync(new(null, null), token));
        Assert.Equal("2030-01-02T03:04:05Z", stored.CreatedAt);

        Record($"in-process chromium-headless-shell: app ready {app.ReadyAfter.TotalMilliseconds:F0} ms, page ready {pageReady.TotalMilliseconds:F0} ms, " +
            $"journey (1 save + verify) {journeyTime.TotalMilliseconds:F0} ms, total {total.Elapsed.TotalMilliseconds:F0} ms");
    }

    [Fact]
    public async Task Two_apps_in_one_test_process_stay_isolated()
    {
        var token = TestContext.Current.CancellationToken;
        var first = InProcessApp.StartAsync(null, token);
        var second = InProcessApp.StartAsync(null, token);
        await using var a = await first;
        await using var b = await second;

        await a.Services.GetRequiredService<NotesService>().SaveAsync(new("only in A", []), token);

        Assert.NotEqual(new Uri(a.Url).Port, new Uri(b.Url).Port);
        Assert.Equal(1, await a.Services.GetRequiredService<NotesService>().CountAsync());
        Assert.Equal(0, await b.Services.GetRequiredService<NotesService>().CountAsync());
        Record($"in-process concurrent pair: app ready {a.ReadyAfter.TotalMilliseconds:F0} ms and {b.ReadyAfter.TotalMilliseconds:F0} ms");
    }

    private static void Record(string line)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        var folder = Path.Combine(directory!.FullName, "artifacts", "react");
        Directory.CreateDirectory(folder);
        File.AppendAllLines(Path.Combine(folder, "browser-timings.txt"), [$"{DateTime.Now:HH:mm:ss} {line}"]);
    }
}
