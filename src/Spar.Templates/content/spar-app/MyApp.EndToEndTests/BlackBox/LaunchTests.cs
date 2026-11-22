using Spar.Testing;

namespace MyApp.EndToEndTests;

/// <summary>
/// The app's exact Production executable, launched with nothing replaced and its per-user folders in a disposable data
/// root, read and driven through the platform's accessibility API on a desktop of this process's own.
/// </summary>
[Trait("Surface", "BlackBox")]
public sealed class LaunchTests
{
    [Fact]
    public async Task The_published_app_answers_its_page_and_closes_cleanly()
    {
        var token = TestContext.Current.CancellationToken;
        await using var app = await SparBlackBox.LaunchAsync(Projects.MyApp, cancellationToken: token);

        await app.Window.GetByRole(SparRole.Heading, new() { Name = "MyApp" }).WaitForAsync();
        await app.Window.GetByRole(SparRole.Button, new() { Name = "Greet" }).InvokeAsync();
        await app.Window.GetByText("Type a name first.").WaitForAsync();

        Assert.Equal(0, await app.CloseAsync(token));
        Assert.True(await app.WaitForProcessTreeExitAsync());
    }
}
