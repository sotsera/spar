using Microsoft.Playwright;
using Notebook.Pages;
using Spar;
using static Microsoft.Playwright.Assertions;

namespace Notebook.EndToEndTests;

/// <summary>What a person does with the Notebook, in its real page against its real backend.</summary>
[Trait("Surface", "Browser")]
public sealed class JourneyTests(SharedBrowser shared) : IClassFixture<SharedBrowser>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_notebook_invites_the_first_page_and_creates_it()
    {
        await using var notebook = await NotebookPage.OpenAsync(shared);
        await Expect(notebook.Page.GetByRole(AriaRole.Heading, new() { Name = "Start your notebook" })).ToBeVisibleAsync();
        await notebook.ScreenshotAsync("empty-light");

        await notebook.CreateAsync("Groceries");

        await Expect(notebook.Page.GetByRole(AriaRole.Heading, new() { Name = "Groceries", Level = 2 })).ToBeVisibleAsync();
        await Expect(notebook.Editor).ToHaveValueAsync("# Groceries\n");
        await Expect(notebook.Page.GetByRole(AriaRole.Article, new() { Name = "Preview" }).GetByRole(AriaRole.Heading, new() { Name = "Groceries", Level = 1 })).ToBeVisibleAsync();
        await Expect(notebook.Sidebar.GetByRole(AriaRole.Button, new() { NameRegex = new("^Groceries") })).ToBeVisibleAsync();
        notebook.AssertClean();
    }

    [Fact]
    public async Task Typing_saves_by_itself_and_the_preview_follows()
    {
        await using var notebook = await NotebookPage.OpenAsync(shared);
        await notebook.CreateAsync("Groceries");

        await notebook.TypeAtEndAsync("\n- [ ] oat milk\n- [x] **coffee**\n");

        await Expect(notebook.Status).ToHaveTextAsync("Saved", new() { Timeout = 5_000 });
        var preview = notebook.Page.GetByRole(AriaRole.Article, new() { Name = "Preview" });
        await Expect(preview.GetByRole(AriaRole.Checkbox)).ToHaveCountAsync(2);
        await Expect(preview.Locator("strong")).ToHaveTextAsync("coffee");
        var file = await File.ReadAllTextAsync(Path.Combine(notebook.App.Folders.Data, "pages", "groceries.md"), Token);
        Assert.EndsWith("# Groceries\n\n- [ ] oat milk\n- [x] **coffee**\n", file, StringComparison.Ordinal);
        await notebook.ScreenshotAsync("page-light");
        notebook.AssertClean();
    }

    [Fact]
    public async Task An_image_pasted_into_a_page_shows_in_its_preview_and_the_view_is_remembered()
    {
        await using var notebook = await NotebookPage.OpenAsync(shared);
        await notebook.CreateAsync("Groceries");

        // A paste of a 1×1 PNG, as an image editor's copy makes it. A drop from the OS is the window journeys': it gives
        // the page grants, which a page in a browser has no host to make.
        await notebook.Editor.EvaluateAsync("""
            (editor) => {
              const bytes = Uint8Array.from(atob("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="), c => c.charCodeAt(0));
              const data = new DataTransfer();
              data.items.add(new File([bytes], "shelf.png", { type: "image/png" }));
              editor.dispatchEvent(new ClipboardEvent("paste", { clipboardData: data, bubbles: true, cancelable: true }));
            }
            """);

        var image = notebook.Page.GetByRole(AriaRole.Article, new() { Name = "Preview" }).GetByRole(AriaRole.Img, new() { Name = "shelf" });
        await Expect(image).ToBeVisibleAsync();
        await Expect(image).ToHaveJSPropertyAsync("naturalWidth", 1);
        await Expect(notebook.Status).ToHaveTextAsync("Saved", new() { Timeout = 5_000 });
        Assert.Single(Directory.GetFiles(Path.Combine(notebook.App.Folders.Data, "pages", "groceries.images"), "*.png"));

        await notebook.Page.GetByRole(AriaRole.Button, new() { Name = "Preview", Exact = true }).ClickAsync();
        await notebook.Page.ReloadAsync();

        // The page reloaded as at the next start: the same page, in the view chosen last.
        await Expect(notebook.Page.GetByRole(AriaRole.Heading, new() { Name = "Groceries", Level = 2 })).ToBeVisibleAsync();
        await Expect(notebook.Page.GetByRole(AriaRole.Button, new() { Name = "Preview", Exact = true })).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(notebook.Editor).ToHaveCountAsync(0);
        await Expect(image).ToBeVisibleAsync();
        notebook.AssertClean();
    }

    [Fact]
    public async Task A_taken_or_invalid_title_is_explained_in_the_form()
    {
        await using var notebook = await NotebookPage.OpenAsync(shared);
        await notebook.CreateAsync("Ideas");

        await notebook.CreateAsync("ideas");
        var dialog = notebook.Page.GetByRole(AriaRole.Dialog, new() { Name = "New page" });
        await Expect(dialog.GetByRole(AriaRole.Alert)).ToHaveTextAsync("There is already a page called “ideas”.");

        await dialog.GetByLabel("Title").FillAsync("   ");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Create page" }).ClickAsync();
        await Expect(dialog.GetByRole(AriaRole.Alert)).Not.ToHaveTextAsync("There is already a page called “ideas”.");
        await Expect(dialog.GetByRole(AriaRole.Alert)).ToBeVisibleAsync();
        await notebook.ScreenshotAsync("title-taken");

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();
        notebook.AssertClean();
    }

    [Fact]
    public async Task A_page_changed_in_another_window_is_never_overwritten_silently()
    {
        await using var notebook = await NotebookPage.OpenAsync(shared);
        await notebook.CreateAsync("Plans");
        await Expect(notebook.Status).ToHaveTextAsync("Saved");

        // This page has unsaved typing when another window saves, through the app's own commands: the live changes show
        // the conflict at once, before this page's autosave would have found it.
        await notebook.TypeAtEndAsync("\nFrom this window\n");
        var pages = new PagesClient(notebook.App);
        var page = Value(await pages.GetAsync(new PageRef("plans"), Token));
        Value(await pages.SaveAsync(new SavePage("plans", "# Plans\n\nFrom the other window\n", page.Revision), Token));
        var banner = notebook.Page.GetByRole(AriaRole.Alert).Filter(new() { HasText = "changed in another window" });
        await Expect(banner).ToBeVisibleAsync(new() { Timeout = 5_000 });
        await Expect(notebook.Status).ToHaveTextAsync("Not saved: changed elsewhere");
        await notebook.ScreenshotAsync("conflict");

        await banner.GetByRole(AriaRole.Button, new() { Name = "Keep mine" }).ClickAsync();

        await Expect(notebook.Status).ToHaveTextAsync("Saved", new() { Timeout = 5_000 });
        Assert.Equal("# Plans\n\nFrom this window\n", Value(await pages.GetAsync(new PageRef("plans"), Token)).Markdown);
        notebook.AssertClean();
    }

    [Fact]
    public async Task Deleting_a_page_asks_first_and_removes_its_file()
    {
        await using var notebook = await NotebookPage.OpenAsync(shared);
        await notebook.CreateAsync("Old notes");
        await Expect(notebook.Page.GetByRole(AriaRole.Heading, new() { Name = "Old notes", Level = 2 })).ToBeVisibleAsync();
        var file = Path.Combine(notebook.App.Folders.Data, "pages", "old-notes.md");
        Assert.True(File.Exists(file));

        await notebook.Page.GetByRole(AriaRole.Button, new() { Name = "Page actions" }).ClickAsync();
        await notebook.Page.GetByRole(AriaRole.Menuitem, new() { Name = "Delete page…" }).ClickAsync();
        var confirm = notebook.Page.GetByRole(AriaRole.Alertdialog);
        await Expect(confirm).ToContainTextAsync("This cannot be undone.");
        await confirm.GetByRole(AriaRole.Button, new() { Name = "Delete page" }).ClickAsync();

        await Expect(notebook.Page.GetByRole(AriaRole.Heading, new() { Name = "Start your notebook" })).ToBeVisibleAsync();
        Assert.False(File.Exists(file));
        notebook.AssertClean();
    }

    [Fact]
    public async Task The_search_box_narrows_the_list_and_slash_focuses_it()
    {
        await using var notebook = await NotebookPage.OpenAsync(shared);
        var pages = new PagesClient(notebook.App);
        foreach (var title in new[] { "Reading list", "Recipes", "Travel" })
        {
            Value(await pages.CreateAsync(new CreatePage(title, null), Token));
        }

        await notebook.Page.ReloadAsync();
        await Expect(notebook.Sidebar.GetByRole(AriaRole.Listitem)).ToHaveCountAsync(3);

        await notebook.Page.Keyboard.PressAsync("/");
        await notebook.Page.Keyboard.TypeAsync("re");

        await Expect(notebook.Sidebar.GetByRole(AriaRole.Listitem)).ToHaveCountAsync(2);
        await notebook.Page.Keyboard.TypeAsync("zzz");
        await Expect(notebook.Sidebar).ToContainTextAsync("Nothing matches “rezzz”.");
        notebook.AssertClean();
    }

    [Fact]
    public async Task The_dark_theme_follows_the_system()
    {
        await using var notebook = await NotebookPage.OpenAsync(shared, colorScheme: "dark");
        var pages = new PagesClient(notebook.App);
        Value(await pages.CreateAsync(new CreatePage("Night shift", "# Night shift\n\nA quiet page with `code`, a [link](https://example.org) and a table:\n\n| Day | Hours |\n|---|---|\n| Mon | 8 |\n"), Token));

        await notebook.Page.ReloadAsync();
        await notebook.Sidebar.GetByRole(AriaRole.Button, new() { NameRegex = new("^Night shift") }).ClickAsync();

        await Expect(notebook.Page.GetByRole(AriaRole.Article, new() { Name = "Preview" }).GetByRole(AriaRole.Table)).ToBeVisibleAsync();
        var background = await notebook.Page.EvaluateAsync<string>("() => getComputedStyle(document.body).backgroundColor");
        Assert.Equal("rgb(22, 23, 27)", background);
        await notebook.ScreenshotAsync("page-dark");
        notebook.AssertClean();
    }

    private static Page Value(Result<Page, PageError> result) => result switch
    {
        Page page => page,
        PageError error => throw new InvalidOperationException($"Expected a page, got {error}."),
    };
}
