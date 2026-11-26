using Spar.Testing;
using static Spar.Testing.SparAssertions;

namespace Notebook.EndToEndTests;

/// <summary>The evaluation's hidden check: a deleted page leaves the list at once.</summary>
[Trait("Surface", "Window")]
public sealed class EvalDeleteWindowTests
{
    [Fact]
    public async Task A_deleted_page_leaves_the_list()
    {
        await using var notebook = await NotebookWindow.OpenAsync();
        await notebook.WaitForReadyAsync();
        await notebook.CreateAsync("Plan");
        await notebook.CreateAsync("Keep");
        await notebook.Sidebar.GetByRole(SparRole.Button, new() { NameRegex = new("^Plan") }).ClickAsync();

        await notebook.Page.GetByRole(SparRole.Button, new() { Name = "Page actions" }).ClickAsync();
        await notebook.Page.GetByRole(SparRole.Menuitem, new() { NameRegex = new("^Delete page") }).ClickAsync();
        await notebook.Page.GetByRole(SparRole.Alertdialog).GetByRole(SparRole.Button, new() { Name = "Delete page", Exact = true }).ClickAsync();

        await Expect(notebook.Sidebar.GetByRole(SparRole.Button, new() { NameRegex = new("^Plan") })).ToHaveCountAsync(0);
        await Expect(notebook.Sidebar.GetByRole(SparRole.Button, new() { NameRegex = new("^Keep") })).ToHaveCountAsync(1);
    }
}
