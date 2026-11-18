using Spar.Testing;
using static Spar.Testing.SparAssertions;

namespace Notebook.EndToEndTests;

/// <summary>
/// The Notebook started from its own <c>Program.cs</c> in its real window (the Window surface): its own packed page in
/// WebView2, offscreen, driven through <c>app.Page</c>.
/// </summary>
internal sealed class NotebookWindow : IAsyncDisposable
{
    private NotebookWindow(SparTestApp app)
    {
        App = app;
    }

    public SparTestApp App { get; }

    public SparPage Page => App.Page;

    public SparLocator Sidebar => Page.GetByRole(SparRole.Navigation, new() { Name = "Pages" });

    public SparLocator Editor => Page.GetByRole(SparRole.Textbox, new() { NameRegex = new("^Markdown of ") });

    public SparLocator Status => Page.GetByRole(SparRole.Status);

    public SparLocator Preview => Page.GetByRole(SparRole.Article, new() { Name = "Preview" });

    public static async Task<NotebookWindow> OpenAsync(Action<SparTestOptions>? configure = null)
    {
        // The Window surface runs on Windows until the Linux host arrives (M1c); elsewhere each journey is a skip.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The Window surface runs on Windows; the Linux host arrives in M1c.");
        var app = await SparTestApp.StartAsync<Program>(options =>
        {
            options.Surface = SparTestSurface.Window;
            configure?.Invoke(options);
        }, TestContext.Current.CancellationToken);
        return new NotebookWindow(app);
    }

    /// <summary>Creates a page through the dialog, as a person would.</summary>
    public async Task CreateAsync(string title)
    {
        await Page.GetByRole(SparRole.Button, new() { Name = "New page" }).First.ClickAsync();
        var dialog = Page.GetByRole(SparRole.Dialog, new() { Name = "New page" });
        await dialog.GetByLabel("Title").FillAsync(title);
        await dialog.GetByRole(SparRole.Button, new() { Name = "Create page" }).ClickAsync();
    }

    /// <summary>Types at the end of the page's Markdown, where a person clicking below the text would.</summary>
    public async Task TypeAtEndAsync(string text)
    {
        await Editor.PressAsync("Control+End");
        await Page.Keyboard.TypeAsync(text);
    }

    /// <summary>Nothing refused or dropped by the bridge, no CSP violation, no page error, nothing on the console.</summary>
    public void AssertClean()
    {
        Assert.True(App.Problems.Count == 0, string.Join(Environment.NewLine, App.Problems));
        Assert.Empty(Page.PageErrors);
        Assert.DoesNotContain(Page.ConsoleMessages, message => message.Type is "error" or "warning");
    }

    /// <summary>A screenshot of the window under <c>artifacts/test-results/</c>, for reviewing how the page looks.</summary>
    public Task ScreenshotAsync(string name) =>
        Page.ScreenshotAsync(new() { Path = Path.Combine(ResultsFolder(), $"window-{name}.png") });

    public ValueTask DisposeAsync() => App.DisposeAsync();

    /// <summary>The page's current title heading, the second level under the notebook's own.</summary>
    public SparLocator PageHeading(string title) => Page.GetByRole(SparRole.Heading, new() { Name = title, Level = 2 });

    /// <summary>Waits until the page's save status reads <paramref name="text"/>.</summary>
    public Task ExpectStatusAsync(string text) => Expect(Status).ToHaveTextAsync(text, new() { Timeout = 5_000 });

    private static string ResultsFolder()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Spar.slnx")))
            {
                var folder = Path.Combine(directory.FullName, "artifacts", "test-results", "Notebook.EndToEndTests", "screenshots");
                Directory.CreateDirectory(folder);
                return folder;
            }
        }

        throw new InvalidOperationException("These tests run from a build inside the Spar repository.");
    }
}
