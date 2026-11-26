using Spar.Testing;
using static Spar.Testing.SparAssertions;

namespace Notebook.EndToEndTests;

/// <summary>The evaluation's hidden check: the sidebar's logo loads under the app's own policy.</summary>
[Trait("Surface", "Window")]
public sealed class EvalLogoWindowTests
{
    [Fact]
    public async Task The_logo_is_drawn()
    {
        await using var notebook = await NotebookWindow.OpenAsync();
        await notebook.WaitForReadyAsync();
        await Expect(notebook.Page.GetByAltText("Notebook logo")).ToBeVisibleAsync();

        // -1 while the image is still loading; 0 once it failed.
        var width = -1;
        for (var attempt = 0; attempt < 50 && width < 0; attempt++)
        {
            var answer = await notebook.Page.EvaluateAsync(
                "(() => { const logo = document.querySelector('img[alt=\"Notebook logo\"]'); return logo && logo.complete ? logo.naturalWidth : -1; })()");
            width = answer.GetInt32();
            if (width < 0)
            {
                await Task.Delay(200, TestContext.Current.CancellationToken);
            }
        }

        Assert.True(width > 0, $"The logo did not load (natural width {width}).");
    }
}
