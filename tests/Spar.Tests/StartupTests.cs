using System.Runtime.Versioning;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Spar.Hosting;
using Spar.Windows;

namespace Spar.Tests;

/// <summary>
/// The start-up decisions every path of plan section 3, "Missing prerequisites", takes, as plain functions, so each
/// path is covered without uninstalling anything: the Windows and Runtime verdicts, the failures after the checks,
/// the fixed exit codes, the line for standard error and the log, and the app's hook (<c>spar.OnStartupProblem</c>).
/// </summary>
[SupportedOSPlatform("windows10.0.17763")]
public sealed class StartupTests
{
    private const int NotFound = unchecked((int)0x80070002);

    [Theory]
    [InlineData(10, 0, 19045, true)]
    [InlineData(10, 0, 22631, true)]
    [InlineData(10, 0, 26320, true)]
    [InlineData(10, 0, 19044, false)]
    [InlineData(10, 0, 17763, false)]
    [InlineData(6, 3, 9600, false)]
    [InlineData(11, 0, 0, true)]
    public void Windows_10_22H2_is_the_oldest_supported(int major, int minor, int build, bool supported)
    {
        var problem = Prerequisites.DecideWindows(new Version(major, minor, build), Prerequisites.MinimumWindowsBuild);

        Assert.Equal(supported, problem is null);
        if (problem is not null)
        {
            Assert.Equal((StartupProblemKind.OsTooOld, 7, "Windows", $"{major}.{minor}.{build}", "10.0.19045"), (problem.Kind, problem.ExitCode, problem.Component, problem.Found, problem.Required));
            Assert.Contains("Windows 10 version 22H2", problem.Details, StringComparison.Ordinal);
            Assert.Null(problem.HelpUrl);
        }
    }

    [Fact]
    public void No_Runtime_is_missing_with_the_download_page()
    {
        var problem = Prerequisites.DecideRuntime(NotFound, null, Prerequisites.MinimumRuntime, comparisonFailed: false, comparison: 0);

        Assert.NotNull(problem);
        Assert.Equal((StartupProblemKind.Missing, 3, "WebView2 Runtime", null, "139.0.3405.78"), (problem.Kind, problem.ExitCode, problem.Component, problem.Found, problem.Required));
        Assert.Equal("https://developer.microsoft.com/microsoft-edge/webview2/consumer/", problem.HelpUrl);
    }

    [Fact]
    public void A_Runtime_older_than_the_minimum_is_too_old()
    {
        var problem = Prerequisites.DecideRuntime(0, "138.0.3351.83", Prerequisites.MinimumRuntime, comparisonFailed: false, comparison: -1);

        Assert.NotNull(problem);
        Assert.Equal((StartupProblemKind.TooOld, 4, "138.0.3351.83"), (problem.Kind, problem.ExitCode, problem.Found));
        Assert.Contains("139.0.3405.78 or later; this computer has 138.0.3351.83", problem.Details, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void A_Runtime_at_or_above_the_minimum_starts(int comparison)
    {
        Assert.Null(Prerequisites.DecideRuntime(0, "157.0.4341.46", Prerequisites.MinimumRuntime, comparisonFailed: false, comparison));
    }

    [Fact]
    public void A_Runtime_that_cannot_be_read_is_broken()
    {
        var unreadable = Prerequisites.DecideRuntime(unchecked((int)0x80004005), null, Prerequisites.MinimumRuntime, comparisonFailed: false, comparison: 0);
        var badVersion = Prerequisites.DecideRuntime(0, "garbage", Prerequisites.MinimumRuntime, comparisonFailed: true, comparison: 0);
        var noVersion = Prerequisites.DecideRuntime(0, null, Prerequisites.MinimumRuntime, comparisonFailed: false, comparison: 0);

        Assert.Equal((StartupProblemKind.Broken, 5), (unreadable!.Kind, unreadable.ExitCode));
        Assert.Contains("0x80004005", unreadable.Details, StringComparison.Ordinal);
        Assert.Equal(StartupProblemKind.Broken, badVersion!.Kind);
        Assert.Equal(StartupProblemKind.Missing, noVersion!.Kind);
    }

    [Fact]
    public void Failures_after_the_checks_map_to_the_fixed_codes()
    {
        var controller = BrowserView.StartupProblemOf(new Spar.Windows.Interop.WebView2Exception("CreateCoreWebView2Controller", unchecked((int)0x800700AA)), "157.0.4341.46");
        var otherSettings = BrowserView.StartupProblemOf(new Spar.Windows.Interop.WebView2Exception("CreateCoreWebView2Controller", unchecked((int)0x8007139F)), "157.0.4341.46");
        var vanished = BrowserView.StartupProblemOf(new Spar.Windows.Interop.WebView2Exception("CreateCoreWebView2EnvironmentWithOptions", NotFound), null);
        var noLoader = BrowserView.StartupProblemOf(new DllNotFoundException("WebView2Loader"), null);
        var noInterface = BrowserView.StartupProblemOf(new InvalidCastException(), "157.0.4341.46");

        Assert.Equal((StartupProblemKind.Broken, 5, "157.0.4341.46"), (controller.Kind, controller.ExitCode, controller.Found));
        Assert.Contains("0x800700AA", controller.Details, StringComparison.Ordinal);
        Assert.Equal(StartupProblemKind.Broken, otherSettings.Kind);
        Assert.Contains("different settings", otherSettings.Details, StringComparison.Ordinal);
        Assert.Equal(StartupProblemKind.Missing, vanished.Kind);
        Assert.Equal((StartupProblemKind.Missing, "WebView2 loader"), (noLoader.Kind, noLoader.Component));
        Assert.Equal(StartupProblemKind.TooOld, noInterface.Kind);
    }

    [Theory]
    [InlineData(StartupProblemKind.Missing, 3)]
    [InlineData(StartupProblemKind.TooOld, 4)]
    [InlineData(StartupProblemKind.Broken, 5)]
    [InlineData(StartupProblemKind.OsTooOld, 7)]
    [InlineData(StartupProblemKind.NotResponding, 8)]
    [InlineData(StartupProblemKind.OtherUser, 9)]
    public void Every_kind_has_its_fixed_exit_code(StartupProblemKind kind, int exitCode)
    {
        Assert.Equal(exitCode, new StartupProblem(kind, "x", null, null, "d", null).ExitCode);
    }

    [Fact]
    public void The_line_for_standard_error_and_the_log_names_everything()
    {
        var line = StartupReport.Describe(new StartupProblem(StartupProblemKind.TooOld, "WebView2 Runtime", "138.0.1.2", "139.0.3405.78", "Update it.", "https://example.test/get"));

        Assert.Equal("Start-up problem TooOld: WebView2 Runtime (found 138.0.1.2, required 139.0.3405.78). Update it. Help: https://example.test/get Exit code 4.", line);
        Assert.Equal(
            "Start-up problem NotResponding: The running app. Wait. Exit code 8.",
            StartupReport.Describe(new StartupProblem(StartupProblemKind.NotResponding, "The running app", null, null, "Wait.", null)));
    }

    [Fact]
    public void The_hook_decides_only_whether_Spar_shows_its_message()
    {
        var problem = new StartupProblem(StartupProblemKind.Missing, "WebView2 Runtime", null, "139.0.3405.78", "Install it.", null);
        var errors = new List<string>();
        StartupProblem? seen = null;

        Assert.False(StartupReport.AppShowedItsOwnMessage(null, problem, (message, _) => errors.Add(message)));
        Assert.True(StartupReport.AppShowedItsOwnMessage(p => (seen = p) is not null, problem, (message, _) => errors.Add(message)));
        Assert.Same(problem, seen);
        Assert.False(StartupReport.AppShowedItsOwnMessage(_ => false, problem, (message, _) => errors.Add(message)));
        Assert.Empty(errors);
    }

    [Fact]
    public void A_hook_that_throws_lets_Spar_show_its_message()
    {
        var problem = new StartupProblem(StartupProblemKind.Broken, "WebView2 Runtime", null, null, "Reinstall it.", null);
        var errors = new List<(string, Exception)>();

        var own = StartupReport.AppShowedItsOwnMessage(_ => throw new InvalidOperationException("boom"), problem, (message, exception) => errors.Add((message, exception)));

        Assert.False(own);
        var (text, error) = Assert.Single(errors);
        Assert.Contains("start-up hook failed", text, StringComparison.Ordinal);
        Assert.IsType<InvalidOperationException>(error);
    }

    [Fact]
    public void The_builder_keeps_the_hook_and_the_relaunch_handler()
    {
        var builder = new SparBuilder([]);
        Func<StartupProblem, bool> hook = _ => true;

        builder.OnStartupProblem(hook).OnRelaunch((_, _) => { });

        Assert.Same(hook, builder.StartupProblemHandler);
        Assert.NotNull(builder.RelaunchHandler);
        Assert.Throws<ArgumentNullException>(() => builder.OnStartupProblem(null!));
    }

    [Theory]
    [InlineData("#ffffff", 0xFF, 0xFF, 0xFF)]
    [InlineData("#1D1F24", 0x1D, 0x1F, 0x24)]
    [InlineData("#abc", 0xAA, 0xBB, 0xCC)]
    public void Window_colours_are_hex(string text, int red, int green, int blue)
    {
        Assert.True(SparColor.TryParse(text, out var color));
        Assert.Equal(new SparColor((byte)red, (byte)green, (byte)blue), color);
        Assert.Equal((uint)(red | (green << 8) | (blue << 16)), color.ColorRef);
    }

    [Theory]
    [InlineData("ffffff")]
    [InlineData("#ffff")]
    [InlineData("#gggggg")]
    [InlineData("#ffffff00")]
    [InlineData("white")]
    public void Other_colour_forms_are_refused(string text)
    {
        Assert.False(SparColor.TryParse(text, out _));
        Assert.Throws<ArgumentException>(() => new SparBuilder([]).Window(w => w.Background(text, "#000000")));
    }

    [Fact]
    public void The_window_options_keep_colours_and_sizes()
    {
        var builder = new SparBuilder([]);

        builder.Window(w => w.Background(light: "#fafafa", dark: "#101010").Size(1200, 800).MinSize(800, 600));

        var window = builder.MainWindow;
        Assert.Equal(("#fafafa", "#101010"), (window.LightBackground.ToString(), window.DarkBackground.ToString()));
        Assert.Equal(((1200, 800), (800, 600)), (window.InitialSize, window.Minimum));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Window(w => w.Size(0, 600)));
    }

    [Fact]
    public void Without_options_the_window_has_the_defaults()
    {
        var window = new SparBuilder([]).MainWindow;

        Assert.Equal(("#ffffff", "#1f1f1f", (1024, 768), (0, 0)), (window.LightBackground.ToString(), window.DarkBackground.ToString(), window.InitialSize, window.Minimum));
    }

    [Fact]
    public void The_native_layouts_have_their_header_sizes()
    {
        Assert.Equal(276, System.Runtime.CompilerServices.Unsafe.SizeOf<Spar.Windows.Interop.OsVersionInfo>());
        Assert.Equal(52, System.Runtime.CompilerServices.Unsafe.SizeOf<Spar.Windows.Interop.FileIdentity>());
        Assert.Equal(24, System.Runtime.CompilerServices.Unsafe.SizeOf<Spar.Windows.Interop.SecurityAttributes>());
        Assert.Equal(4, System.Runtime.CompilerServices.Unsafe.SizeOf<Spar.Windows.Interop.WebView2Color>());
    }

    [Fact]
    public void A_relaunch_survives_the_wire_unchanged()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Work Folder");
        var relaunch = new Relaunch(["--recipe", "nightly copy.sqlrecipe", "ünïcode ✓ 😀", "", "a\"b"], folder);

        Assert.True(RelaunchMessage.TryDecode(RelaunchMessage.Encode(relaunch), out var decoded));

        Assert.Equal(relaunch.WorkingDirectory, decoded!.WorkingDirectory);
        Assert.Equal(relaunch.Arguments, decoded.Arguments);
        Assert.Equal(Path.Combine(folder, "nightly copy.sqlrecipe"), decoded.ResolvePath("nightly copy.sqlrecipe"));
    }

    [Fact]
    public void A_relaunch_without_arguments_survives_the_wire()
    {
        Assert.True(RelaunchMessage.TryDecode(RelaunchMessage.Encode(new Relaunch([], Path.GetTempPath())), out var decoded));
        Assert.Empty(decoded!.Arguments);
    }

    [Fact]
    public void A_malformed_relaunch_is_refused_without_an_exception()
    {
        var folder = Path.GetTempPath();
        var good = RelaunchMessage.Encode(new Relaunch(["a", "b"], folder));

        Assert.False(RelaunchMessage.TryDecode(good.AsSpan(0, good.Length - 1), out _));
        Assert.False(RelaunchMessage.TryDecode([.. good, 0], out _));
        Assert.False(RelaunchMessage.TryDecode([2, .. good[1..]], out _));
        Assert.False(RelaunchMessage.TryDecode([], out _));

        var relative = RelaunchMessage.Encode(new Relaunch(["a"], "relative"));
        Assert.False(RelaunchMessage.TryDecode(relative, out _));

        var hugeCount = (byte[])good.Clone();
        BitConverter.TryWriteBytes(hugeCount.AsSpan(1 + 4 + Encoding.UTF8.GetByteCount(folder)), int.MaxValue);
        Assert.False(RelaunchMessage.TryDecode(hugeCount, out _));

        var invalidUtf8 = (byte[])good.Clone();
        invalidUtf8[^1] = 0xFF;
        Assert.False(RelaunchMessage.TryDecode(invalidUtf8, out _));
    }

    [Fact]
    public async Task Relaunches_reach_the_handler_one_at_a_time_in_order()
    {
        var seen = new List<string>();
        var running = 0;
        var overlapped = false;
        var queue = new RelaunchQueue(
            async (relaunch, _, cancellationToken) =>
            {
                overlapped |= Interlocked.Increment(ref running) > 1;
                await Task.Delay(5, cancellationToken);
                seen.Add(relaunch.Arguments[0]);
                Interlocked.Decrement(ref running);
            },
            NullLogger.Instance);

        // Before the services exist, relaunches wait in the queue.
        Assert.True(queue.TryAdd(new Relaunch(["1"], @"C:\")));
        Assert.True(queue.TryAdd(new Relaunch(["2"], @"C:\")));
        queue.Start(new ServiceCollection().BuildServiceProvider());
        Assert.True(queue.TryAdd(new Relaunch(["3"], @"C:\")));
        await WaitUntilAsync(() => Volatile.Read(ref seen).Count == 3);
        await queue.StopAsync();

        Assert.Equal(["1", "2", "3"], seen);
        Assert.False(overlapped);
        Assert.False(queue.TryAdd(new Relaunch(["4"], @"C:\")));
    }

    [Fact]
    public async Task A_failing_handler_never_stops_the_next_relaunch()
    {
        var seen = new List<string>();
        var queue = new RelaunchQueue(
            (relaunch, _, _) => relaunch.Arguments[0] == "bad" ? throw new InvalidOperationException("bad") : Add(seen, relaunch),
            NullLogger.Instance);
        queue.Start(new ServiceCollection().BuildServiceProvider());

        queue.TryAdd(new Relaunch(["bad"], @"C:\"));
        queue.TryAdd(new Relaunch(["good"], @"C:\"));
        await WaitUntilAsync(() => seen.Count == 1);
        await queue.StopAsync();

        Assert.Equal(["good"], seen);
    }

    [Fact]
    public async Task Stopping_cancels_the_running_handlers_token()
    {
        var started = new TaskCompletionSource();
        var cancelled = false;
        var queue = new RelaunchQueue(
            async (_, _, cancellationToken) =>
            {
                started.SetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                    throw;
                }
            },
            NullLogger.Instance);
        queue.Start(new ServiceCollection().BuildServiceProvider());
        queue.TryAdd(new Relaunch([], @"C:\"));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        await queue.StopAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.True(cancelled);
    }

    private static ValueTask Add(List<string> seen, Relaunch relaunch)
    {
        seen.Add(relaunch.Arguments[0]);
        return ValueTask.CompletedTask;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 500 && !condition(); attempt++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }
}
