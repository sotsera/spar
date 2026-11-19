using Spar.Testing;
using static Spar.Testing.SparAssertions;

namespace SqlTool.EndToEndTests;

// Window tests carry this trait, so they run as their own invocation: a native engine abort ends only that run.
[Trait("Surface", "Window")]
public sealed class ConnectionsWindowTests
{
    [Fact]
    public async Task Removing_a_connection_asks_first_and_takes_it_off_the_list()
    {
        await using var app = await SparTestApp.StartAsync<Program>(
            o => o.Surface = SparTestSurface.Window,
            TestContext.Current.CancellationToken);
        var page = app.Page;
        var connections = page.GetByRole(SparRole.List, new() { Name = "Connections" });

        await connections.GetByRole(SparRole.Listitem).Filter(new() { HasText = "Production" })
            .GetByRole(SparRole.Button, new() { Name = "Remove" }).ClickAsync();
        await page.GetByRole(SparRole.Alertdialog).GetByRole(SparRole.Button, new() { Name = "Remove" }).ClickAsync();

        await Expect(connections.GetByRole(SparRole.Listitem)).ToHaveCountAsync(1);
        await Expect(page.GetByRole(SparRole.Status)).ToHaveTextAsync("Production removed");
        Assert.Empty(app.Problems);
        Assert.Empty(page.PageErrors);
    }
}
