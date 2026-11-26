using Spar.Testing;
using static Spar.Testing.SparAssertions;

namespace Pantry.EndToEndTests;

[Trait("Surface", "Window")]
public sealed class GreetingErrorWindowTests
{
    [Fact]
    public async Task Greet_without_a_name_asks_for_one()
    {
        await using var app = await SparTestApp.StartAsync<Program>(
            o => o.Surface = SparTestSurface.Window,
            TestContext.Current.CancellationToken);

        await app.Page.GetByRole(SparRole.Button, new() { Name = "Greet" }).ClickAsync();

        await Expect(app.Page.GetByRole(SparRole.Status)).ToHaveTextAsync("Type a name first.");
    }
}
