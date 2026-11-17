using Microsoft.Playwright;
using Spar.Testing;

[assembly: AssemblyFixture(typeof(Notebook.EndToEndTests.SharedBrowser))]

namespace Notebook.EndToEndTests;

/// <summary>One headless Chromium for the run; each test opens a context of its own.</summary>
public sealed class SharedBrowser : IAsyncLifetime
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public IBrowser Browser => _browser ?? throw new InvalidOperationException("The browser has not started.");

    public async ValueTask InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
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

/// <summary>
/// The Notebook started from its own <c>Program.cs</c> with its page open in a fresh browser context, and what the page
/// reported on its console.
/// </summary>
internal sealed class NotebookPage : IAsyncDisposable
{
    /// <summary>
    /// Chromium's warning for a feature Spar's <c>Permissions-Policy</c> header denies but this browser build does not
    /// have: Linux's headless Chromium has no Web Bluetooth. The only console message the journeys accept.
    /// </summary>
    private const string UnknownDeniedFeature = "Error with Permissions-Policy header: Unrecognized feature: 'bluetooth'.";

    private readonly List<string> _console = [];

    private NotebookPage(SparTestApp app, IBrowserContext context, IPage page)
    {
        App = app;
        Context = context;
        Page = page;
        page.Console += (_, message) =>
        {
            if (message.Type is "error" or "warning" && message.Text != UnknownDeniedFeature)
            {
                lock (_console)
                {
                    _console.Add($"{message.Type}: {message.Text}");
                }
            }
        };
        page.PageError += (_, error) =>
        {
            lock (_console)
            {
                _console.Add($"page error: {error}");
            }
        };
    }

    public SparTestApp App { get; }

    public IBrowserContext Context { get; }

    public IPage Page { get; }

    public static async Task<NotebookPage> OpenAsync(SharedBrowser shared, Action<SparTestOptions>? configure = null, string colorScheme = "light")
    {
        var app = await SparTestApp.StartAsync<Program>(options =>
        {
            options.Surface = SparTestSurface.Browser;
            options.Page();
            configure?.Invoke(options);
        });

        var scheme = colorScheme == "dark" ? ColorScheme.Dark : ColorScheme.Light;
        var context = await shared.Browser.NewContextAsync(new() { ViewportSize = new() { Width = 1280, Height = 800 }, ColorScheme = scheme });
        var notebook = new NotebookPage(app, context, await context.NewPageAsync());
        await notebook.Page.GotoAsync(app.PageUrl().ToString());
        return notebook;
    }

    public ILocator Sidebar => Page.GetByRole(AriaRole.Navigation, new() { Name = "Pages" });

    public ILocator Editor => Page.GetByRole(AriaRole.Textbox, new() { NameRegex = new("^Markdown of ") });

    public ILocator Status => Page.GetByRole(AriaRole.Status);

    /// <summary>Creates a page through the dialog, as a person would.</summary>
    public async Task CreateAsync(string title)
    {
        await Page.GetByRole(AriaRole.Button, new() { Name = "New page" }).First.ClickAsync();
        var dialog = Page.GetByRole(AriaRole.Dialog, new() { Name = "New page" });
        await dialog.GetByLabel("Title").FillAsync(title);
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Create page" }).ClickAsync();
    }

    /// <summary>Types at the end of the page's Markdown, where a person clicking below the text would.</summary>
    public async Task TypeAtEndAsync(string text)
    {
        await Editor.FocusAsync();
        await Page.Keyboard.PressAsync("Control+End");
        await Editor.PressSequentiallyAsync(text);
    }

    /// <summary>Nothing refused or dropped by the bridge, no CSP violation, nothing on the console.</summary>
    public void AssertClean()
    {
        Assert.True(App.Problems.Count == 0, string.Join(Environment.NewLine, App.Problems));
        lock (_console)
        {
            Assert.True(_console.Count == 0, string.Join(Environment.NewLine, _console));
        }
    }

    /// <summary>A screenshot under <c>artifacts/test-results/</c>, for reviewing how the page looks.</summary>
    public Task ScreenshotAsync(string name) =>
        Page.ScreenshotAsync(new() { Path = Path.Combine(Built.Results, $"{name}.png") });

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        await App.DisposeAsync();
    }
}

/// <summary>Folders of this repository the tests write to.</summary>
internal static class Built
{
    private static readonly Lazy<string> Root = new(FindRoot);

    public static string Results
    {
        get
        {
            var folder = Path.Combine(Root.Value, "artifacts", "test-results", "Notebook.EndToEndTests", "screenshots");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Spar.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("These tests run from a build inside the Spar repository.");
    }
}
