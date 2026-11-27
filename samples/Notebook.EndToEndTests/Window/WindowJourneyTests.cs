using Notebook.Pages;
using Spar;
using Spar.Testing;
using static Spar.Testing.SparAssertions;

namespace Notebook.EndToEndTests;

/// <summary>
/// What a person does with the Notebook, in its real window: the app's own packed page in WebView2 against its real
/// backend, with trusted input. The same journeys as the browser's, written with Spar's window API (decision "Step 8's
/// shape: window tests driven by Spar's own API"); they run as their own invocation (trait Surface=Window), and also in
/// the NativeAOT lane.
/// </summary>
[Trait("Surface", "Window")]
public sealed class WindowJourneyTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_notebook_invites_the_first_page_and_creates_it()
    {
        await using var notebook = await NotebookWindow.OpenAsync();
        await Expect(notebook.Page.GetByRole(SparRole.Heading, new() { Name = "Start your notebook" })).ToBeVisibleAsync();
        await notebook.ScreenshotAsync("empty");

        await notebook.CreateAsync("Groceries");

        await Expect(notebook.PageHeading("Groceries")).ToBeVisibleAsync();
        await Expect(notebook.Editor).ToHaveValueAsync("# Groceries\n");
        await Expect(notebook.Preview.GetByRole(SparRole.Heading, new() { Name = "Groceries", Level = 1 })).ToBeVisibleAsync();
        await Expect(notebook.Sidebar.GetByRole(SparRole.Button, new() { NameRegex = new("^Groceries") })).ToBeVisibleAsync();
        await Expect(notebook.Sidebar.GetByRole(SparRole.Button, new() { NameRegex = new("^Groceries") }))
            .ToHaveAttributeAsync("aria-current", "page");
        notebook.AssertClean();
    }

    [Fact]
    public async Task Typing_saves_by_itself_and_the_preview_follows()
    {
        await using var notebook = await NotebookWindow.OpenAsync();
        await notebook.CreateAsync("Groceries");
        await Expect(notebook.Editor).ToHaveValueAsync("# Groceries\n");

        await notebook.TypeAtEndAsync("\n- [ ] oat milk\n- [x] **coffee**\n");

        await notebook.ExpectStatusAsync("Saved");
        await Expect(notebook.Preview.GetByRole(SparRole.Checkbox)).ToHaveCountAsync(2);
        await Expect(notebook.Preview.GetByRole(SparRole.Checkbox, new() { Checked = true })).ToHaveCountAsync(1);
        await Expect(notebook.Preview.Locator("strong")).ToHaveTextAsync("coffee");
        var file = await File.ReadAllTextAsync(Path.Combine(notebook.App.Folders.Data, "pages", "groceries.md"), Token);
        Assert.EndsWith("# Groceries\n\n- [ ] oat milk\n- [x] **coffee**\n", file, StringComparison.Ordinal);
        await notebook.ScreenshotAsync("page");
        notebook.AssertClean();
    }

    [Fact]
    public async Task A_taken_or_invalid_title_is_explained_in_the_form()
    {
        await using var notebook = await NotebookWindow.OpenAsync();
        await notebook.CreateAsync("Ideas");
        await Expect(notebook.PageHeading("Ideas")).ToBeVisibleAsync();

        await notebook.CreateAsync("ideas");
        var dialog = notebook.Page.GetByRole(SparRole.Dialog, new() { Name = "New page" });
        await Expect(dialog.GetByRole(SparRole.Alert)).ToHaveTextAsync("There is already a page called “ideas”.");

        await dialog.GetByLabel("Title").FillAsync("   ");
        await dialog.GetByRole(SparRole.Button, new() { Name = "Create page" }).ClickAsync();
        await Expect(dialog.GetByRole(SparRole.Alert)).Not.ToHaveTextAsync("There is already a page called “ideas”.");
        await Expect(dialog.GetByRole(SparRole.Alert)).ToBeVisibleAsync();
        await notebook.ScreenshotAsync("title-taken");

        await notebook.Page.Keyboard.PressAsync("Escape");
        await Expect(dialog).ToBeHiddenAsync();
        notebook.AssertClean();
    }

    [Fact]
    public async Task A_page_changed_in_another_window_is_never_overwritten_silently()
    {
        await using var notebook = await NotebookWindow.OpenAsync();
        await notebook.CreateAsync("Plans");
        await notebook.ExpectStatusAsync("Saved");

        // This window has unsaved typing when another window saves, through the app's own commands: the live changes
        // show the conflict at once, before this window's autosave would have found it.
        await notebook.TypeAtEndAsync("\nFrom this window\n");
        var pages = new PagesClient(notebook.App);
        var page = Value(await pages.GetAsync(new PageRef("plans"), Token));
        Value(await pages.SaveAsync(new SavePage("plans", "# Plans\n\nFrom the other window\n", page.Revision), Token));

        var banner = notebook.Page.GetByRole(SparRole.Alert).Filter(new() { HasText = "changed in another window" });
        await Expect(banner).ToBeVisibleAsync(new() { Timeout = 5_000 });
        await notebook.ExpectStatusAsync("Not saved: changed elsewhere");
        await notebook.ScreenshotAsync("conflict");

        await banner.GetByRole(SparRole.Button, new() { Name = "Keep mine" }).ClickAsync();

        await notebook.ExpectStatusAsync("Saved");
        Assert.Equal("# Plans\n\nFrom this window\n", Value(await pages.GetAsync(new PageRef("plans"), Token)).Markdown);
        notebook.AssertClean();
    }

    [Fact]
    public async Task A_page_saved_in_another_window_shows_its_new_text_and_a_new_page_joins_the_list()
    {
        await using var notebook = await NotebookWindow.OpenAsync();
        await notebook.CreateAsync("Plans");
        await notebook.ExpectStatusAsync("Saved");

        // Nothing is unsaved here, so the open page follows the other window's save, through the subscription.
        var pages = new PagesClient(notebook.App);
        var page = Value(await pages.GetAsync(new PageRef("plans"), Token));
        Value(await pages.SaveAsync(new SavePage("plans", "# Plans\n\nFrom the other window\n", page.Revision), Token));
        await Expect(notebook.Editor).ToHaveValueAsync("# Plans\n\nFrom the other window\n", new() { Timeout = 5_000 });
        await notebook.ExpectStatusAsync("Saved");

        Value(await pages.CreateAsync(new CreatePage("Ideas", Markdown: null), Token));
        var list = notebook.Page.GetByRole(SparRole.Navigation, new() { Name = "Pages" });
        await Expect(list.GetByRole(SparRole.Button).Filter(new() { HasText = "Ideas" })).ToBeVisibleAsync(new() { Timeout = 5_000 });
        await notebook.ScreenshotAsync("live-changes");
        notebook.AssertClean();
    }

    [Fact]
    public async Task Deleting_a_page_asks_first_and_removes_its_file()
    {
        await using var notebook = await NotebookWindow.OpenAsync();
        await notebook.CreateAsync("Old notes");
        await Expect(notebook.PageHeading("Old notes")).ToBeVisibleAsync();
        var file = Path.Combine(notebook.App.Folders.Data, "pages", "old-notes.md");
        Assert.True(File.Exists(file));

        await notebook.Page.GetByRole(SparRole.Button, new() { Name = "Page actions" }).ClickAsync();
        await notebook.Page.GetByRole(SparRole.Menuitem, new() { Name = "Delete page…" }).ClickAsync();
        var confirm = notebook.Page.GetByRole(SparRole.Alertdialog);
        await Expect(confirm).ToContainTextAsync("This cannot be undone.");
        await confirm.GetByRole(SparRole.Button, new() { Name = "Delete page" }).ClickAsync();

        await Expect(notebook.Page.GetByRole(SparRole.Heading, new() { Name = "Start your notebook" })).ToBeVisibleAsync();
        Assert.False(File.Exists(file));
        notebook.AssertClean();
    }

    [Fact]
    public async Task The_search_box_narrows_the_list_and_slash_focuses_it()
    {
        await using var notebook = await NotebookWindow.OpenAsync();

        // The first page has connected, has its (empty) list and has reported ready before the reload replaces it: a
        // page reloaded while it is still starting sends its first call and `ready` as it goes, and the host ignores
        // them with a warning. `ready` waits for the splash, so the visible list alone is not enough.
        await Expect(notebook.Page.GetByRole(SparRole.Heading, new() { Name = "Start your notebook" })).ToBeVisibleAsync();
        await notebook.WaitForReadyAsync();
        var pages = new PagesClient(notebook.App);
        foreach (var title in new[] { "Reading list", "Recipes", "Travel" })
        {
            Value(await pages.CreateAsync(new CreatePage(title, null), Token));
        }

        await notebook.Page.ReloadAsync();
        await Expect(notebook.Sidebar.GetByRole(SparRole.Listitem)).ToHaveCountAsync(3);

        await notebook.Page.Keyboard.PressAsync("/");
        await Expect(notebook.Page.GetByRole(SparRole.Searchbox, new() { Name = "Search pages" })).ToBeFocusedAsync();
        await notebook.Page.Keyboard.TypeAsync("re");

        await Expect(notebook.Sidebar.GetByRole(SparRole.Listitem)).ToHaveCountAsync(2);
        await notebook.Page.Keyboard.TypeAsync("zzz");
        await Expect(notebook.Sidebar).ToContainTextAsync("Nothing matches “rezzz”.");
        notebook.AssertClean();
    }

    [Fact]
    public async Task The_shortcuts_open_a_new_page_and_switch_the_view()
    {
        await using var notebook = await NotebookWindow.OpenAsync();
        await notebook.CreateAsync("Shortcuts");
        await Expect(notebook.Editor).ToHaveValueAsync("# Shortcuts\n");

        var view = notebook.Page.GetByRole(SparRole.Group, new() { Name = "View" });
        await view.GetByRole(SparRole.Button, new() { Name = "Preview", Exact = true }).ClickAsync();
        await Expect(view.GetByRole(SparRole.Button, new() { Name = "Preview", Exact = true, Pressed = true })).ToBeVisibleAsync();
        await Expect(notebook.Editor).ToHaveCountAsync(0);

        await notebook.Page.Keyboard.PressAsync("Control+n");
        var dialog = notebook.Page.GetByRole(SparRole.Dialog, new() { Name = "New page" });
        await Expect(dialog).ToBeVisibleAsync();
        await Expect(dialog.GetByLabel("Title")).ToBeFocusedAsync();
        await notebook.Page.Keyboard.PressAsync("Escape");
        await Expect(dialog).ToBeHiddenAsync();
        notebook.AssertClean();
    }

    private static Page Value(Result<Page, PageError> result) => result switch
    {
        Page page => page,
        PageError error => throw new InvalidOperationException($"Expected a page, got {error}."),
    };
}
