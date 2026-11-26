using Spar.Testing;
using static Spar.Testing.SparAssertions;

namespace Notebook.EndToEndTests;

/// <summary>The evaluation's hidden check of the Duplicate button.</summary>
[Trait("Surface", "Window")]
public sealed class EvalDuplicateWindowTests
{
    [Fact]
    public async Task The_duplicate_button_copies_the_open_page()
    {
        await using var notebook = await NotebookWindow.OpenAsync();
        await notebook.WaitForReadyAsync();
        await notebook.CreateAsync("Plan");

        await notebook.Page.GetByRole(SparRole.Button, new() { Name = "Duplicate" }).ClickAsync();

        await Expect(notebook.Sidebar.GetByRole(SparRole.Button, new() { NameRegex = new(@"^Plan \(copy\)") })).ToBeVisibleAsync();
    }
}
