using System.Diagnostics;
using Spar.Testing;

namespace Spar.WindowTests;

/// <summary>
/// The black-box fixture's own rules, on WindowHost's Production executable (<c>Projects.WindowHost</c>): the job
/// object that ends the app with its owner, the deadline that ends a hung app, a cleanup that cannot finish reported
/// as a failure, the ready timeout, the dialog API on a real failed relaunch (a suspended first instance), and the
/// options refused before anything starts.
/// </summary>
[Trait("Surface", "BlackBox")]
public sealed class BlackBoxFixtureTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Closing_the_job_ends_the_app_and_everything_it_started()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            Assert.Skip("The black-box fixture runs on Windows 10 1809 and later; the Linux host arrives in M1c.");
            return;
        }

        var root = TestDataRoots.Next("black-box");
        var folders = BlackBoxFolders.For(root);
        folders.Create();
        IBlackBoxProcess process = AppProcess.Start(
            Projects.WindowHost.ExecutablePath,
            [],
            SparBlackBox.Environment(new SparBlackBoxOptions(), folders),
            root,
            AutomationHost.For(SparBlackBoxPlacement.HiddenDesktop).Desktop,
            null);
        try
        {
            // The browser's processes too: wait until the app has started some.
            await Waits.UntilAsync(() => process.ProcessIds.Count >= 3, TimeSpan.FromSeconds(20), "the browser's processes");
            var started = process.ProcessIds;

            // What a crashed test process leaves: the job's handle closed by Windows, nothing else done.
            process.Dispose();

            await Waits.UntilAsync(() => started.All(Exited), TimeSpan.FromSeconds(10), "every process of the job to end");
        }
        finally
        {
            TestDataRoots.Delete(root);
        }
    }

    [Fact]
    public async Task A_hung_app_is_ended_after_its_deadline_and_reported()
    {
        WindowSupport.SkipUnlessSupported();
        var app = await SparBlackBox.LaunchAsync(Projects.WindowHost, o => o.ExitTimeout = TimeSpan.FromSeconds(1), Token);
        var started = app.ProcessIds;
        app.Process.SuspendProcess();

        await Assert.ThrowsAsync<TimeoutException>(() => app.WaitForExitAsync(Token));
        var clock = Stopwatch.StartNew();
        await app.DisposeAsync();

        var kept = Assert.Single(SparTestCleanup.End(TestContext.Current.Test!.UniqueID));
        Assert.Contains("did not exit within 1 s", kept.Message, StringComparison.Ordinal);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(8), $"Disposal took {clock.Elapsed.TotalSeconds:0.0} s.");
        Assert.All(started, id => Assert.True(Exited(id), $"Process {id} is still running."));
        Assert.False(Directory.Exists(app.DataRoot));
    }

    [Fact]
    public async Task A_data_root_that_cannot_be_deleted_is_a_reported_failure()
    {
        WindowSupport.SkipUnlessSupported();
        var app = await SparBlackBox.LaunchAsync(Projects.WindowHost, cancellationToken: Token);
        Assert.Equal(0, await app.CloseAsync(Token));

        var blocker = Path.Combine(app.DataRoot, "held.txt");
        using (new FileStream(blocker, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await app.DisposeAsync();
        }

        var kept = Assert.Single(SparTestCleanup.End(TestContext.Current.Test!.UniqueID));
        Assert.Contains("could not be deleted", kept.Message, StringComparison.Ordinal);
        TestDataRoots.Delete(app.DataRoot);
    }

    [Fact]
    public async Task An_app_not_ready_in_time_is_ended_and_its_root_deleted()
    {
        WindowSupport.SkipUnlessSupported();
        var probe = TestDataRoots.Next("probe");
        var run = Path.GetDirectoryName(probe)!;
        Directory.Delete(probe);
        var before = Directory.GetDirectories(run, "black-box-*").ToHashSet(StringComparer.OrdinalIgnoreCase);

        var failure = await Assert.ThrowsAsync<TimeoutException>(() =>
            SparBlackBox.LaunchAsync(Projects.WindowHost, o => o.ReadyTimeout = TimeSpan.FromMilliseconds(50), Token));

        Assert.Contains("was not ready within", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Directory.GetDirectories(run, "black-box-*"), root => !before.Contains(root));
    }

    [Fact]
    public async Task A_network_LOCALAPPDATA_is_refused_with_exit_70_and_nothing_written()
    {
        WindowSupport.SkipUnlessSupported();

        // A name that never resolves (.invalid, RFC 6761): the host refuses the path by its form, before any access.
        var real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Projects.WindowHost.AppId);
        var existedBefore = Directory.Exists(real);
        await using var app = await SparBlackBox.LaunchAsync(Projects.WindowHost, o =>
        {
            o.LocalAppData = @"\\spar-refused.invalid\share";
            o.WaitForReady = false;
        }, Token);

        var dialog = await app.WaitForDialogAsync(TimeSpan.FromSeconds(15), Token);
        Assert.Equal("WindowHost", dialog.Title);
        Assert.Contains(@"'\\spar-refused.invalid\share\" + Projects.WindowHost.AppId + "' is on a network drive", dialog.Text, StringComparison.Ordinal);
        await dialog.PressAsync("Close");

        Assert.Equal(70, await app.WaitForExitAsync(Token));
        Assert.Contains("The app's configuration stops its start", app.StandardError, StringComparison.Ordinal);
        Assert.Equal(existedBefore, Directory.Exists(real));
        Assert.False(Directory.Exists(app.LogsFolder), "Nothing was written for the refused data root.");
    }

    [Fact]
    public async Task A_relaunch_to_a_hung_first_instance_shows_its_message_and_exits_8()
    {
        WindowSupport.SkipUnlessSupported();
        await using var first = await SparBlackBox.LaunchAsync(Projects.WindowHost, cancellationToken: Token);
        first.Process.SuspendProcess();

        await using var second = await SparBlackBox.LaunchAsync(Projects.WindowHost, o =>
        {
            o.DataRoot = first.DataRoot;
            o.WaitForReady = false;
        }, Token);
        var dialog = await second.WaitForDialogAsync(TimeSpan.FromSeconds(15), Token);

        Assert.Equal("WindowHost", dialog.Title);
        Assert.Equal(["WindowHost can't start", "The app is already running but did not answer within 2 seconds. Wait a moment, then start it again, or end it in Task Manager."], dialog.Texts);
        Assert.Contains("Close", dialog.Buttons);
        Assert.DoesNotContain("Minimize", dialog.Buttons);
        await Assert.ThrowsAsync<ArgumentException>(() => dialog.PressAsync("OK"));

        await dialog.PressAsync("Close");
        Assert.Equal(8, await second.WaitForExitAsync(Token));
    }

    [Fact]
    public async Task The_window_is_read_and_driven_through_UI_Automation()
    {
        WindowSupport.SkipUnlessSupported();
        await using var app = await SparBlackBox.LaunchAsync(Projects.WindowHost, cancellationToken: Token);
        var window = app.Window;

        Assert.Equal("WindowHost", await window.TitleAsync());
        Assert.Equal("Window host", await window.GetByRole(SparRole.Heading, new() { Name = "window host" }).NameAsync());
        Assert.Equal(4, await window.GetByRole(SparRole.Heading, new() { NameRegex = new("^(Calls|Order|Timing|Scrolling)$") }).CountAsync());
        Assert.Equal(0, await window.GetByRole(SparRole.Heading, new() { Name = "window host", Exact = true }).CountAsync());
        Assert.Equal("Place order", await window.GetByRole(SparRole.Button, new() { Disabled = true }).NameAsync());
        Assert.True(await window.GetByText("No order yet").IsVisibleAsync());

        // An action needs one element: several is a mistake, at once; a disabled one is waited for until the deadline.
        await Assert.ThrowsAsync<InvalidOperationException>(() => window.GetByRole(SparRole.Button, new() { Name = "call" }).InvokeAsync());
        await Assert.ThrowsAsync<TimeoutException>(() => window.GetByRole(SparRole.Button, new() { Name = "Place order" }).InvokeAsync(new() { Timeout = 300 }));
        Assert.Throws<NotSupportedException>(() => window.GetByRole(SparRole.Heading, new() { Level = 1 }));

        await window.GetByRole(SparRole.Button, new() { Name = "Show the late button" }).InvokeAsync();
        await window.GetByRole(SparRole.Button, new() { Name = "Late button", Exact = true }).InvokeAsync();
        await window.GetByText("Late button clicked").WaitForAsync();
        Assert.Equal("", await window.GetByRole(SparRole.Textbox, new() { Name = "Notes" }).InputValueAsync());

        Assert.Equal(0, await app.CloseAsync(Token));
        Assert.True(await app.WaitForProcessTreeExitAsync());
    }

    [Fact]
    public async Task Options_the_fixture_cannot_honour_are_refused_before_anything_starts()
    {
        WindowSupport.SkipUnlessSupported();
        string[] refused = ["LOCALAPPDATA", "appdata", "TEMP", "TMP", "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", "SPAR_AUTOMATED", "A=B"];
        foreach (var name in refused)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => SparBlackBox.LaunchAsync(Projects.WindowHost, o => o.Environment[name] = "x", Token));
        }

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => SparBlackBox.LaunchAsync(Projects.WindowHost, o => o.ReadyTimeout = TimeSpan.Zero, Token));
        await Assert.ThrowsAsync<ArgumentException>(() => SparBlackBox.LaunchAsync(Projects.WindowHost, o => o.DataRoot = Path.GetTempPath(), Token));
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            SparBlackBox.LaunchAsync(new SparProject("Missing", "com.example.Missing", "Missing.exe", ""), cancellationToken: Token));
    }

    [Fact]
    public void The_environment_points_into_the_root_and_leaves_out_the_browser_and_Spar_variables()
    {
        var folders = BlackBoxFolders.For(Path.Combine(Path.GetTempPath(), "root"));
        var options = new SparBlackBoxOptions();
        options.Environment["APP_SETTING"] = "on";

        var environment = SparBlackBox.Environment(options, folders);

        Assert.Equal(folders.LocalAppData, environment["LOCALAPPDATA"]);
        Assert.Equal(folders.RoamingAppData, environment["APPDATA"]);
        Assert.Equal(folders.Temp, environment["TEMP"]);
        Assert.Equal(folders.Temp, environment["TMP"]);
        Assert.Equal("on", environment["APP_SETTING"]);
        Assert.DoesNotContain(environment.Keys, name => name.StartsWith("WEBVIEW2_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("SPAR_", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(environment.Keys, name => string.Equals(name, "PATH", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(new string[0], "\"C:\\a b\\app.exe\"")]
    [InlineData(new[] { "plain", "with space", "" }, "\"C:\\a b\\app.exe\" plain \"with space\" \"\"")]
    [InlineData(new[] { "say \"hi\"", "end\\", "dir\\ x\\" }, "\"C:\\a b\\app.exe\" \"say \\\"hi\\\"\" end\\ \"dir\\ x\\\\\"")]
    public void Arguments_reach_the_app_as_given(string[] arguments, string expected) =>
        Assert.Equal(expected, CommandLine.Build(@"C:\a b\app.exe", arguments));

    private static bool Exited(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }
}
