using Spar;
using Spar.Testing;

namespace SqlTool.EndToEndTests;

// Black-box tests launch processes and take seconds: they run as their own invocation, like window tests.
[Trait("Surface", "BlackBox")]
public sealed class BlackBoxTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_shipped_executable_starts_and_closes_cleanly()
    {
        await using var app = await SparBlackBox.LaunchAsync(Projects.SqlTool, cancellationToken: Token);

        await app.Window.GetByRole(SparRole.List, new() { Name = "Connections" }).WaitForAsync();
        await app.Window.GetByRole(SparRole.Button, new() { Name = "Add connection" }).InvokeAsync();
        await app.Window.GetByRole(SparRole.Dialog, new() { Name = "Add connection" }).WaitForAsync();

        Assert.Equal(0, await app.CloseAsync(Token));
        Assert.True(File.Exists(app.LogFile));
        Assert.True(await app.WaitForProcessTreeExitAsync());
    }

    [Fact]
    public async Task A_second_launch_hands_its_recipe_to_the_running_app()
    {
        await using var first = await SparBlackBox.LaunchAsync(Projects.SqlTool, cancellationToken: Token);

        await using var second = await SparBlackBox.LaunchAsync(Projects.SqlTool, o =>
        {
            o.DataRoot = first.DataRoot;
            o.Arguments.Add("--recipe");
            o.Arguments.Add("nightly-copy.json");
            o.WaitForReady = false;
        }, Token);

        Assert.Equal(0, await second.WaitForExitAsync(Token));
    }

    [Fact]
    public async Task A_relaunch_the_running_app_never_answers_says_so_and_exits_8()
    {
        await using var first = await SparBlackBox.LaunchAsync(Projects.SqlTool, cancellationToken: Token);
        Assert.Equal(0, await first.CloseAsync(Token));

        // The lock of an instance that serves nobody: the next launch waits for it, then gives up.
        var lockFile = Path.Combine(first.AppFolder, "instance.lock");
        await using var held = new FileStream(lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        await using var relaunch = await SparBlackBox.LaunchAsync(Projects.SqlTool, o =>
        {
            o.DataRoot = first.DataRoot;
            o.WaitForReady = false;
        }, Token);

        var dialog = await relaunch.WaitForDialogAsync(cancellationToken: Token);
        Assert.Contains("SqlTool can't start", dialog.Text, StringComparison.Ordinal);
        await dialog.PressAsync("Close");
        Assert.Equal(8, await relaunch.WaitForExitAsync(Token));
    }
}
