using System.Diagnostics;
using Loop.Tests;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace React.Tests;

public sealed class NotesJourneyTests
{
    public static TheoryData<string> Browsers => ["chromium-headless-shell", "msedge-headless"];

    [Theory]
    [MemberData(nameof(Browsers))]
    public async Task Adds_searches_and_removes_notes(string browserKind)
    {
        if (browserKind == "msedge-headless" && !OperatingSystem.IsWindows())
        {
            Assert.Skip("Microsoft Edge is only used on Windows.");
        }

        var token = TestContext.Current.CancellationToken;
        var total = Stopwatch.StartNew();
        await using var app = await LoopbackApp.StartAsync(token);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchAsync(playwright, browserKind);
        var page = await browser.NewPageAsync();

        var loaded = Stopwatch.StartNew();
        await page.GotoAsync(app.Url);
        await Expect(page.GetByLabel("Note count")).ToHaveTextAsync("0 notes");
        var pageReady = loaded.Elapsed;

        var journey = Stopwatch.StartNew();
        await AddNoteAsync(page, "Buy milk", "shopping");
        await AddNoteAsync(page, "Call Anna", "family, phone");
        await AddNoteAsync(page, "Buy stamps", "shopping");
        await Expect(page.GetByLabel("Note count")).ToHaveTextAsync("3 notes");

        // Backend filtering: by text and by tag.
        await page.GetByRole(AriaRole.Searchbox, new() { Name = "Search" }).FillAsync("buy");
        await Expect(page.GetByRole(AriaRole.List, new() { Name = "Notes" }).GetByRole(AriaRole.Listitem)).ToHaveCountAsync(2);
        await page.GetByRole(AriaRole.Searchbox, new() { Name = "Search" }).FillAsync("family");
        await Expect(page.GetByRole(AriaRole.Listitem)).ToHaveCountAsync(1);
        await Expect(page.GetByRole(AriaRole.Listitem)).ToContainTextAsync("Call Anna #family #phone");

        await page.GetByRole(AriaRole.Button, new() { Name = "Remove Call Anna" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Status)).ToHaveTextAsync("Removed");
        await Expect(page.GetByLabel("Note count")).ToHaveTextAsync("2 notes");

        // Client-side validation never reaches the backend.
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Status)).ToHaveTextAsync("Enter a note.");
        var journeyTime = journey.Elapsed;

        Assert.Equal(3, app.Output.Count(l => l.Contains("notes.save ok", StringComparison.Ordinal)));
        Record($"{browserKind} ({browser.Version}): app ready {app.ReadyAfter.TotalMilliseconds:F0} ms, page ready {pageReady.TotalMilliseconds:F0} ms, " +
            $"journey (3 saves, 2 searches, 1 remove, validation) {journeyTime.TotalMilliseconds:F0} ms, total {total.Elapsed.TotalMilliseconds:F0} ms");
    }

    [Fact]
    public async Task The_latest_search_wins_even_when_older_answers_arrive_later()
    {
        var token = TestContext.Current.CancellationToken;
        await using var app = await LoopbackApp.StartAsync(token);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchAsync(playwright, "chromium-headless-shell");
        var page = await browser.NewPageAsync();
        await page.GotoAsync(app.Url.Replace("/#", "/?slow#", StringComparison.Ordinal));
        await AddNoteAsync(page, "alpha", "");
        await AddNoteAsync(page, "alphabet", "");
        await AddNoteAsync(page, "alpine", "");

        // "a" answers after 600 ms, "al" after 300 ms, "alp" at once: the older, slower answers arrive last.
        var search = page.GetByRole(AriaRole.Searchbox, new() { Name = "Search" });
        await search.PressSequentiallyAsync("alph", new() { Delay = 30 });
        await page.WaitForTimeoutAsync(1200);

        await Expect(page.GetByRole(AriaRole.List, new() { Name = "Notes" })).ToHaveAttributeAsync("aria-busy", "false");
        await Expect(page.GetByRole(AriaRole.Listitem)).ToHaveCountAsync(2);
        Assert.Contains(app.Output, l => l.Contains("notes.search", StringComparison.Ordinal));
    }

    private static async Task<IBrowser> LaunchAsync(IPlaywright playwright, string browserKind) => browserKind == "msedge-headless"
        ? await playwright.Chromium.LaunchAsync(new() { Channel = "msedge", Headless = true })
        : await playwright.Chromium.LaunchAsync(new() { Headless = true });

    private static async Task AddNoteAsync(IPage page, string text, string tags)
    {
        var count = page.GetByLabel("Note count");
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Note" }).FillAsync(text);
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Tags" }).FillAsync(tags);
        await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("Saved version");
        await Expect(page.GetByRole(AriaRole.Textbox, new() { Name = "Note" })).ToHaveValueAsync("");
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
