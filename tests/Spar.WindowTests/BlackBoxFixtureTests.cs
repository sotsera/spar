using System.Diagnostics;
using Spar.Testing;

namespace Spar.WindowTests;

/// <summary>
/// The black-box fixture's own rules, on WindowHost's Production executable (<c>Projects.WindowHost</c>), on Windows
/// and Linux: the job object that ends the app with its owner (Windows), the deadline that ends a hung app, a cleanup
/// that cannot finish reported as a failure, the ready timeout, the dialog API on a real failed relaunch (a suspended
/// first instance), the window read and driven through the accessibility API (UI Automation, AT-SPI), an invoke that
/// lands as the first action after the launch and one whose press removes its element, and the options refused before
/// anything starts.
/// The two tests of Windows-only mechanisms carry <c>OS=Windows</c>, which the Linux CI run leaves out (it takes every
/// other black-box test with no skip).
/// </summary>
[Trait("Surface", "BlackBox")]
public sealed class BlackBoxFixtureTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("OS", "Windows")]
    public async Task Closing_the_job_ends_the_app_and_everything_it_started()
    {
        // Linux ends the app with the test process through KillOnParentExit, which only the test process's own death
        // shows (docs/evidence/linux-blackbox-atspi: 0 processes left in 25 of 25 runs); a test of it needs a holder process.
        WindowSupport.SkipUnlessWindows("Windows' job object; on Linux the kernel ends the app with the test process (KillOnParentExit), measured by the AT-SPI spike.");
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            Assert.Skip("The black-box fixture runs on Windows 10 1809 and later.");
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
            AutomationHost.For(SparBlackBox.Placement).Desktop,
            SparBlackBox.Placement == SparBlackBoxPlacement.ShownWithoutActivation ? (ushort)4 : null);
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
        WindowSupport.SkipUnlessBlackBox();
        var app = await SparBlackBox.LaunchAsync(Projects.WindowHost, o => o.ExitTimeout = TimeSpan.FromSeconds(1), Token);
        await Waits.UntilAsync(() => app.ProcessIds.Count >= 3, TimeSpan.FromSeconds(20), "the browser's processes");
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
        WindowSupport.SkipUnlessBlackBox();
        var app = await SparBlackBox.LaunchAsync(Projects.WindowHost, cancellationToken: Token);
        Assert.Equal(0, await app.CloseAsync(Token));

        // What keeps a folder from deletion: a file held open without sharing on Windows, a folder without write access on Linux.
        var blocker = Path.Combine(app.DataRoot, "held");
        if (OperatingSystem.IsWindows())
        {
            using (new FileStream(blocker, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await app.DisposeAsync();
            }
        }
        else
        {
            Directory.CreateDirectory(blocker);
            File.WriteAllText(Path.Combine(blocker, "kept.txt"), "kept");
            File.SetUnixFileMode(blocker, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            await app.DisposeAsync();
            File.SetUnixFileMode(blocker, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var kept = Assert.Single(SparTestCleanup.End(TestContext.Current.Test!.UniqueID));
        Assert.Contains("could not be deleted", kept.Message, StringComparison.Ordinal);
        TestDataRoots.Delete(app.DataRoot);
    }

    [Fact]
    public async Task An_app_not_ready_in_time_is_ended_and_its_root_deleted()
    {
        WindowSupport.SkipUnlessBlackBox();

        // The roots this test's own launches create: other black-box tests create theirs beside them meanwhile.
        var created = new List<string>();
        TestDataRoots.Created.Value = created;

        var failure = await Assert.ThrowsAsync<TimeoutException>(() =>
            SparBlackBox.LaunchAsync(Projects.WindowHost, o => o.ReadyTimeout = TimeSpan.FromMilliseconds(50), Token));

        Assert.Contains("was not ready within", failure.Message, StringComparison.Ordinal);
        var root = Assert.Single(created);
        Assert.False(Directory.Exists(root), $"{root} is still there.");
    }

    [Fact]
    [Trait("OS", "Windows")]
    public async Task A_network_LOCALAPPDATA_is_refused_with_exit_70_and_nothing_written()
    {
        // Linux mounts a network file system at a local path, which the host cannot tell apart: no counterpart.
        WindowSupport.SkipUnlessWindows("Windows' refusal of a data root on a network drive (a UNC path or a mapped drive); Linux has no such path.");
        WindowSupport.SkipUnlessBlackBox();

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
        WindowSupport.SkipUnlessBlackBox();
        await using var first = await SparBlackBox.LaunchAsync(Projects.WindowHost, cancellationToken: Token);
        first.Process.SuspendProcess();

        await using var second = await SparBlackBox.LaunchAsync(Projects.WindowHost, o =>
        {
            o.DataRoot = first.DataRoot;
            o.WaitForReady = false;
        }, Token);
        var clock = Stopwatch.StartNew();
        var dialog = await second.WaitForDialogAsync(TimeSpan.FromSeconds(15), Token);
        var found = clock.Elapsed;

        // The host's own words for where to end a stuck app: Task Manager on Windows, the system monitor on Linux.
        Assert.Equal("WindowHost", dialog.Title);
        Assert.True(dialog.Texts.Count == 2, $"{dialog}{Environment.NewLine}{await second.Window.DumpAsync()}");
        Assert.Equal("WindowHost can't start", dialog.Texts[0]);
        Assert.Matches(
            @"^The app is already running but did not answer within 2 seconds\. Wait a moment, then start it again, or end it in (Task Manager|the system monitor)\.",
            dialog.Texts[1]);
        Assert.Contains("Close", dialog.Buttons);
        Assert.DoesNotContain("Minimize", dialog.Buttons);
        await Assert.ThrowsAsync<ArgumentException>(() => dialog.PressAsync("OK"));

        clock.Restart();
        await dialog.PressAsync("Close");
        Assert.Equal(8, await second.WaitForExitAsync(Token));
        Results.Append("black-box.txt", $"relaunch dialog found {found.TotalMilliseconds:0} ms after the launch; Close to exit 8 in {clock.Elapsed.TotalMilliseconds:0} ms");
    }

    [Fact]
    public async Task The_window_is_read_and_driven_through_the_accessibility_API()
    {
        WindowSupport.SkipUnlessBlackBox();
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

        var clock = Stopwatch.StartNew();
        await window.GetByRole(SparRole.Button, new() { Name = "Show the late button" }).InvokeAsync();
        var invoked = clock.Elapsed;
        await window.GetByRole(SparRole.Button, new() { Name = "Late button", Exact = true }).InvokeAsync();
        await window.GetByText("Late button clicked").WaitForAsync();
        Assert.Equal("", await window.GetByRole(SparRole.Textbox, new() { Name = "Notes" }).InputValueAsync());

        // The host's first line names the build: its identity block, written by the generator (decision "The spar tool
        // (implementation record; choices for review; 2026-11-22)"). The revision is there only in a git work tree.
        using (var log = new StreamReader(new FileStream(app.LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)))
        {
            Assert.Matches(
                @"Host started [0-9.]+ ms after the process; build Production, (revision [0-9a-f]{40} \([a-z ]+\)|no revision \(not built in a git work tree\)), contract fingerprint [0-9a-f]{16}\.",
                await log.ReadToEndAsync(Token));
        }

        clock.Restart();
        Assert.Equal(0, await app.CloseAsync(Token));
        var closed = clock.Elapsed;
        Assert.True(await app.WaitForProcessTreeExitAsync());
        Results.Append("black-box.txt", $"launch to ready {app.StartedAfter.TotalMilliseconds:0} ms; a button found and invoked in {invoked.TotalMilliseconds:0} ms; close to exit {closed.TotalMilliseconds:0} ms");
    }

    [Fact]
    public async Task An_invoke_as_the_first_action_after_the_launch_lands()
    {
        // Before the fix, a page button invoked as soon as the launch returned did nothing in 4 of this test's 24 launches
        // on Windows while the invoke reported success (pitfalls, "A black-box Invoke right after a page button appears
        // can do nothing"). Now a press the browser does not confirm fails the invoke itself.
        WindowSupport.SkipUnlessBlackBox();
        const int Launches = 8;
        var landed = 0;
        for (var launch = 0; launch < Launches; launch++)
        {
            await using var app = await SparBlackBox.LaunchAsync(Projects.WindowHost, cancellationToken: Token);
            var clock = Stopwatch.StartNew();
            await app.Window.GetByRole(SparRole.Button, new() { Name = "Covered button" }).InvokeAsync();
            var invoked = clock.Elapsed;
            if (await IsShownAsync(app.Window.GetByText("Covered button clicked")))
            {
                landed++;
            }

            Results.Append("black-box.txt", $"first invoke after the launch: {invoked.TotalMilliseconds:0} ms");
            Assert.Equal(0, await app.CloseAsync(Token));
        }

        Assert.Equal(Launches, landed);
    }

    [Fact]
    public async Task A_press_that_removes_its_element_or_leaves_the_page_is_taken()
    {
        // The browser drops the Invoked event with its node: the element gone from the tree confirms these presses.
        WindowSupport.SkipUnlessBlackBox();
        await using var app = await SparBlackBox.LaunchAsync(Projects.WindowHost, cancellationToken: Token);
        var window = app.Window;

        await window.GetByRole(SparRole.Button, new() { Name = "Open the dialog" }).InvokeAsync();
        await window.GetByRole(SparRole.Button, new() { Name = "Confirm", Exact = true }).InvokeAsync();
        await window.GetByText("Confirmed").WaitForAsync();

        await window.GetByRole(SparRole.Link, new() { Name = "Second page" }).InvokeAsync();
        await window.GetByRole(SparRole.Heading, new() { Name = "Second page" }).WaitForAsync();
        await window.GetByRole(SparRole.Button, new() { Name = "Echo" }).InvokeAsync();
        await window.GetByText("Echo: again").WaitForAsync();

        Assert.Equal(0, await app.CloseAsync(Token));
    }

    [Fact]
    public async Task Options_the_fixture_cannot_honour_are_refused_before_anything_starts()
    {
        WindowSupport.SkipUnlessBlackBox();
        string[] browser = OperatingSystem.IsWindows() ? ["WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS"] : [];
        string[] refused = [.. SparBlackBoxOptions.FixtureVariables, .. browser, "SPAR_AUTOMATED", "A=B"];
        foreach (var name in refused)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => SparBlackBox.LaunchAsync(Projects.WindowHost, o => o.Environment[name] = "x", Token));
        }

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => SparBlackBox.LaunchAsync(Projects.WindowHost, o => o.ReadyTimeout = TimeSpan.Zero, Token));
        await Assert.ThrowsAsync<ArgumentException>(() => SparBlackBox.LaunchAsync(Projects.WindowHost, o => o.DataRoot = Path.GetTempPath(), Token));
        var outlives = await Assert.ThrowsAsync<ArgumentException>(() => SparBlackBox.LaunchAsync(Projects.WindowHost, o => o.OutlivesTest = true, Token));
        Assert.Contains("Sandbox checks only", outlives.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            SparBlackBox.LaunchAsync(new SparProject("Missing", "com.example.Missing", "Missing.exe", ""), cancellationToken: Token));
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("true", null, true)]
    [InlineData("TRUE", null, true)]
    [InlineData("1", null, true)]
    [InlineData("false", null, false)]
    [InlineData("", null, false)]
    [InlineData(null, "True", true)]
    [InlineData("0", "false", false)]
    public void A_CI_run_is_recognised_by_its_standard_variables(string? ci, string? azure, bool expected)
    {
        var variables = new Dictionary<string, string?> { ["CI"] = ci, ["TF_BUILD"] = azure };

        Assert.Equal(expected, SparBlackBox.IsContinuousIntegration(name => variables.GetValueOrDefault(name)));
    }

    [Fact]
    public void The_environment_points_into_the_root_and_leaves_out_the_browser_and_Spar_variables()
    {
        var folders = BlackBoxFolders.For(Path.Combine(Path.GetTempPath(), "root"));
        var options = new SparBlackBoxOptions();
        options.Environment["APP_SETTING"] = "on";
        if (OperatingSystem.IsLinux())
        {
            // Not started for this: the private display is the fixture's choice, so the check reads it through the option.
            options.Display = ":0-test";
        }

        var environment = SparBlackBox.Environment(options, folders);

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(folders.LocalAppData, environment["LOCALAPPDATA"]);
            Assert.Equal(folders.RoamingAppData, environment["APPDATA"]);
            Assert.Equal(folders.Temp, environment["TEMP"]);
            Assert.Equal(folders.Temp, environment["TMP"]);
        }
        else
        {
            Assert.Equal(folders.Home, environment["HOME"]);
            Assert.Equal(folders.LocalAppData, environment["XDG_DATA_HOME"]);
            Assert.Equal(folders.RoamingAppData, environment["XDG_CONFIG_HOME"]);
            Assert.Equal(folders.Cache, environment["XDG_CACHE_HOME"]);
            Assert.Equal(folders.State, environment["XDG_STATE_HOME"]);
            Assert.Equal(folders.Temp, environment["TMPDIR"]);
            Assert.StartsWith("/dev/shm/spar-bb-", environment["XDG_RUNTIME_DIR"], StringComparison.Ordinal);
            Assert.Equal(":0-test", environment["DISPLAY"]);
            Assert.DoesNotContain(environment.Keys, name => SparBlackBoxOptions.LinuxRemoved.Contains(name) && name != "DISPLAY");
        }

        Assert.Equal("on", environment["APP_SETTING"]);
        Assert.DoesNotContain(environment.Keys, name => SparBlackBoxOptions.RemovedPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(environment.Keys, name => string.Equals(name, "PATH", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(new string[0], "\"C:\\a b\\app.exe\"")]
    [InlineData(new[] { "plain", "with space", "" }, "\"C:\\a b\\app.exe\" plain \"with space\" \"\"")]
    [InlineData(new[] { "say \"hi\"", "end\\", "dir\\ x\\" }, "\"C:\\a b\\app.exe\" \"say \\\"hi\\\"\" end\\ \"dir\\ x\\\\\"")]
    public void Arguments_reach_the_app_as_given(string[] arguments, string expected) =>
        Assert.Equal(expected, CommandLine.Build(@"C:\a b\app.exe", arguments));

    /// <summary>Whether the locator's element is shown within 3 s.</summary>
    private static async Task<bool> IsShownAsync(SparBlackBoxLocator locator)
    {
        try
        {
            await locator.WaitForAsync(new() { Timeout = 3000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    internal static bool Exited(int processId)
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
