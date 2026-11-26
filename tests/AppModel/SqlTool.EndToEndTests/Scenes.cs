using Spar.Testing;
using static Spar.Testing.SparAssertions;

namespace SqlTool.EndToEndTests;

public static class Scenes
{
    // spar snap connections: the list of saved connections.
    [SparScene("connections")]
    public static async Task Connections(SparScene scene)
    {
        var app = await scene.StartAsync<Program>();
        await Expect(app.Page.GetByRole(SparRole.List, new() { Name = "Connections" })).ToBeVisibleAsync();
    }

    // spar snap remove-connection: the question before a connection is removed.
    [SparScene("remove-connection")]
    public static async Task RemoveConnection(SparScene scene)
    {
        var app = await scene.StartAsync<Program>();
        await app.Page.GetByRole(SparRole.List, new() { Name = "Connections" })
            .GetByRole(SparRole.Listitem).Filter(new() { HasText = "Production" })
            .GetByRole(SparRole.Button, new() { Name = "Remove" }).ClickAsync();
        await Expect(app.Page.GetByRole(SparRole.Alertdialog)).ToBeVisibleAsync();
    }
}
