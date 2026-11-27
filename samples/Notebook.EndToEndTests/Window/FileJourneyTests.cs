using Spar.Testing;
using static Spar.Testing.SparAssertions;

namespace Notebook.EndToEndTests;

/// <summary>
/// Files in and out of the Notebook, in its real window (decision "M3c's decide-first questions", item 8): Markdown
/// files dropped from the OS on the list of pages become pages; a page exports through the save dialog and copies to
/// the clipboard as Markdown; an image dropped on the editor shows in the preview. The drops go through the engine's
/// own drag and drop; the dialogs and the clipboard are the test host's, so nothing reaches the machine's.
/// </summary>
[Trait("Surface", "Window")]
public sealed class FileJourneyTests
{
    private const string Pixel = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Markdown_files_dropped_on_the_list_become_pages()
    {
        await using var notebook = await NotebookWindow.OpenAsync();
        var folder = Outside(notebook);
        var recipes = Write(folder, "Recipes.md", "# Recipes\n\n- bread");
        var travel = Write(folder, "Travel plans.md", "# Travel plans");

        await notebook.Sidebar.DropFilesAsync([recipes, travel]);

        await Expect(notebook.Sidebar.GetByRole(SparRole.Button, new() { NameRegex = new("^Recipes") })).ToBeVisibleAsync();
        await Expect(notebook.Sidebar.GetByRole(SparRole.Button, new() { NameRegex = new("^Travel plans") })).ToBeVisibleAsync();
        await Expect(notebook.PageHeading("Recipes")).ToBeVisibleAsync();
        await Expect(notebook.Editor).ToHaveValueAsync("# Recipes\n\n- bread");
        Assert.True(File.Exists(Path.Combine(notebook.App.Folders.Data, "pages", "travel-plans.md")));
        notebook.AssertClean();
    }

    [Fact]
    public async Task A_file_that_is_not_Markdown_is_refused_with_its_name()
    {
        await using var notebook = await NotebookWindow.OpenAsync();
        var sheet = Write(Outside(notebook), "budget.xlsx", "not Markdown");

        await notebook.Sidebar.DropFilesAsync([sheet]);

        await Expect(notebook.Page.GetByText("budget.xlsx is not a Markdown file.")).ToBeVisibleAsync();
        await Expect(notebook.Sidebar.GetByText("No pages yet.")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task A_page_exports_through_the_save_dialog_and_copies_as_Markdown()
    {
        await using var notebook = await NotebookWindow.OpenAsync();
        await notebook.CreateAsync("Groceries");
        await notebook.TypeAtEndAsync("\n- oat milk");
        await notebook.ExpectStatusAsync("Saved");
        var target = Path.Combine(Outside(notebook), "groceries export.md");
        notebook.App.FileDialogs.Choose(target);

        await notebook.Page.GetByRole(SparRole.Button, new() { Name = "Page actions" }).ClickAsync();
        await notebook.Page.GetByRole(SparRole.Menuitem, new() { Name = "Export…" }).ClickAsync();
        await Expect(notebook.Page.GetByText("Exported to groceries export.md.")).ToBeVisibleAsync();
        await notebook.Page.GetByRole(SparRole.Button, new() { Name = "Dismiss" }).ClickAsync();
        await notebook.Page.GetByRole(SparRole.Button, new() { Name = "Page actions" }).ClickAsync();
        await notebook.Page.GetByRole(SparRole.Menuitem, new() { Name = "Copy as Markdown" }).ClickAsync();
        await Expect(notebook.Page.GetByText("Copied as Markdown.")).ToBeVisibleAsync();

        Assert.Equal("# Groceries\n\n- oat milk", (await File.ReadAllTextAsync(target, Token)).TrimEnd('\n'));
        var shown = Assert.Single(notebook.App.FileDialogs.Shown);
        Assert.Equal(("save", "groceries.md"), (shown.Kind, shown.SuggestedName));
        Assert.Equal("# Groceries\n\n- oat milk", notebook.App.Clipboard.Text.TrimEnd('\n'));
        notebook.AssertClean();
    }

    [Fact]
    public async Task An_image_dropped_on_the_editor_shows_in_the_preview()
    {
        await using var notebook = await NotebookWindow.OpenAsync();
        await notebook.CreateAsync("Groceries");
        var shelf = Path.Combine(Outside(notebook), "shelf.png");
        await File.WriteAllBytesAsync(shelf, Convert.FromBase64String(Pixel), Token);

        await notebook.Editor.DropFilesAsync([shelf]);

        var image = notebook.Preview.GetByRole(SparRole.Img, new() { Name = "shelf" });
        await Expect(image).ToBeVisibleAsync();
        await notebook.ExpectStatusAsync("Saved");
        Assert.Single(Directory.GetFiles(Path.Combine(notebook.App.Folders.Data, "pages", "groceries.images"), "*.png"));
        notebook.AssertClean();
    }

    /// <summary>A folder outside the notebook's data, as a person's documents would be.</summary>
    private static string Outside(NotebookWindow notebook) =>
        Directory.CreateDirectory(Path.Combine(notebook.App.Folders.DataRoot, "documents")).FullName;

    private static string Write(string folder, string name, string text)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, text);
        return path;
    }
}
