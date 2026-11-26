using Spar.Testing;
using static Spar.Testing.SparAssertions;

namespace Pantry.EndToEndTests;

/// <summary>The evaluation's hidden check of the page's note form.</summary>
[Trait("Surface", "Window")]
public sealed class EvalNotesWindowTests
{
    [Fact]
    public async Task The_page_adds_a_note_and_explains_a_blank_one()
    {
        await using var app = await SparTestApp.StartAsync<Program>(
            o => o.Surface = SparTestSurface.Window,
            TestContext.Current.CancellationToken);
        var page = app.Page;
        var add = page.GetByRole(SparRole.Button, new() { Name = "Add note" });

        await add.ClickAsync();
        await Expect(page.GetByText("A note needs text.")).ToBeVisibleAsync();

        await page.GetByLabel("Note").FillAsync("oat milk");
        await add.ClickAsync();
        await Expect(page.GetByRole(SparRole.Listitem).GetByText("oat milk")).ToBeVisibleAsync();
    }
}
