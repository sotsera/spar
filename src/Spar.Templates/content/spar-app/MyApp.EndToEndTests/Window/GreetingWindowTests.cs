using Spar.Testing;
using static Spar.Testing.SparAssertions;

namespace MyApp.EndToEndTests;

/// <summary>
/// The app's real window, in this process: shown offscreen and never activated (on Linux, on a private display), so it
/// never takes the focus of the person at the computer.
/// </summary>
[Trait("Surface", "Window")]
public sealed class GreetingWindowTests
{
    [Fact]
    public async Task The_window_greets_a_name()
    {
        await using var app = await SparTestApp.StartAsync<Program>(
            o => o.Surface = SparTestSurface.Window,
            TestContext.Current.CancellationToken);
        var page = app.Page;

        await page.GetByLabel("Name").FillAsync("Ada");
        await page.GetByRole(SparRole.Button, new() { Name = "Greet" }).ClickAsync();

        await Expect(page.GetByRole(SparRole.Status)).ToHaveTextAsync("Hello, Ada!");
        Assert.Empty(app.Problems);
        Assert.Empty(page.PageErrors);
    }
}
