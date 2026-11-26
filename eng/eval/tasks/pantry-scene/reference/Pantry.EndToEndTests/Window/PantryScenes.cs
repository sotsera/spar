using Spar.Testing;
using static Spar.Testing.SparAssertions;

namespace Pantry.EndToEndTests;

public static class PantryScenes
{
    [SparScene("greeted")]
    public static async Task Greeted(SparScene scene)
    {
        var app = await scene.StartAsync<Program>();
        await app.Page.GetByLabel("Name").FillAsync("Ada");
        await app.Page.GetByRole(SparRole.Button, new() { Name = "Greet" }).ClickAsync();
        await Expect(app.Page.GetByRole(SparRole.Status)).ToHaveTextAsync("Hello, Ada!");
    }
}
