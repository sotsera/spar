using Spar.Testing;
using static Spar.Testing.SparAssertions;

namespace Notebook.EndToEndTests;

/// <summary>
/// The Notebook's scenes: the states worth a look, captured by <c>dotnet spar snap &lt;name&gt;</c> (a PNG, the
/// accessibility tree, the bridge calls) and each run as a window test by <c>dotnet test</c>. Every scene starts at the
/// same instant, in the light theme, with the page at 1280 × 800.
/// </summary>
public static class NotebookScenes
{
    /// <summary>A new notebook: the invitation to write the first page.</summary>
    [SparScene("empty")]
    public static async Task Empty(SparScene scene)
    {
        var app = await scene.StartAsync<Program>();
        await Expect(app.Page.GetByRole(SparRole.Heading, new() { Name = "Start your notebook" })).ToBeVisibleAsync();
    }

    /// <summary>The new-page dialog with a title typed in.</summary>
    [SparScene("new-page-dialog")]
    public static async Task NewPageDialog(SparScene scene)
    {
        var app = await scene.StartAsync<Program>();
        await app.Page.GetByRole(SparRole.Button, new() { Name = "New page" }).First.ClickAsync();
        var dialog = app.Page.GetByRole(SparRole.Dialog, new() { Name = "New page" });
        await dialog.GetByLabel("Title").FillAsync("Groceries");
    }

    /// <summary>A page with a checklist, saved, its preview beside the Markdown.</summary>
    [SparScene("groceries")]
    public static async Task Groceries(SparScene scene)
    {
        var app = await scene.StartAsync<Program>();
        var page = app.Page;
        await page.GetByRole(SparRole.Button, new() { Name = "New page" }).First.ClickAsync();
        var dialog = page.GetByRole(SparRole.Dialog, new() { Name = "New page" });
        await dialog.GetByLabel("Title").FillAsync("Groceries");
        await dialog.GetByRole(SparRole.Button, new() { Name = "Create page" }).ClickAsync();

        var editor = page.GetByRole(SparRole.Textbox, new() { NameRegex = new("^Markdown of ") });
        await Expect(editor).ToHaveValueAsync("# Groceries\n");
        await editor.PressAsync("Control+End");
        await page.Keyboard.TypeAsync("\n- [ ] oat milk\n- [x] **coffee**\n- [ ] apples\n");
        await Expect(page.GetByRole(SparRole.Status)).ToHaveTextAsync("Saved", new() { Timeout = 5_000 });
    }
}
