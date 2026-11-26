using Spar.Testing;
using static Spar.Testing.SparAssertions;

namespace Notebook.EndToEndTests;

/// <summary>The evaluation's hidden check: the search box comes after the page list.</summary>
[Trait("Surface", "Window")]
public sealed class EvalSearchBelowWindowTests
{
    [Fact]
    public async Task The_search_box_follows_the_list()
    {
        await using var notebook = await NotebookWindow.OpenAsync();
        await notebook.WaitForReadyAsync();
        await Expect(notebook.Sidebar).ToBeVisibleAsync();

        // Node.DOCUMENT_POSITION_FOLLOWING (4): the search box comes after the list in the document.
        var position = await notebook.Page.EvaluateAsync(
            "document.querySelector('nav[aria-label=\"Pages\"]').compareDocumentPosition(document.querySelector('input[type=\"search\"]'))");

        Assert.True((position.GetInt32() & 4) != 0, $"The search box is before the list (position {position.GetInt32()}).");
    }
}
