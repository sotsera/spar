using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace Spar.WindowTests;

/// <summary>
/// <c>SIGTERM</c> and <c>SIGINT</c> as the end of the session on Linux (decision "The Linux black box: AT-SPI over GDBus;
/// the dialog's title; SIGTERM as a session end"), on the bridge probe's Production executable: both guards asked with
/// the session end's reason and neither able to keep the window, the services disposed, the final line written within
/// Windows' 4 s budget (a slower disposal is cut there, as Windows ends the process after <c>WM_ENDSESSION</c>), exit 0;
/// a second signal ends the app at once with 128 + its number. The shared rules are in <see cref="SessionEndHostTests"/>.
/// </summary>
[SupportedOSPlatform("linux")]
[Collection(nameof(ProbeCollection))]
[Trait("Surface", "BlackBox")]
[Trait("App", "BridgeProbe")]
public sealed class LinuxSessionEndTests
{
    private const string Held = @"session end: (?<outcome>[^;]+); (?<signal>SIGTERM|SIGINT) held (?<ms>\d+) ms";

    [Theory]
    [InlineData("TERM")]
    [InlineData("INT")]
    public async Task A_signal_closes_the_window_as_the_session_end_and_exits_0(string signal)
    {
        await using var app = await ProbeApp.StartAsync("");
        await app.WaitForAsync(@"probe note: plan done");

        var clock = Stopwatch.StartNew();
        Signal(signal, app.Id);
        var exit = await app.WaitForExitAsync(TimeSpan.FromSeconds(10));
        var exited = clock.Elapsed;

        Assert.Equal(0, exit);
        var log = app.Log();
        Assert.Contains($"SIG{signal}: the session ends", log, StringComparison.Ordinal);
        Assert.Contains("session ending", log, StringComparison.Ordinal);
        Assert.Contains("session end: the page's guard answered", log, StringComparison.Ordinal);
        Assert.Contains("services disposed", log, StringComparison.Ordinal);
        var held = Regex.Match(log, Held);
        Assert.True(held.Success, app.Describe());
        Assert.Equal("window closed, last work done", held.Groups["outcome"].Value);
        Assert.InRange(int.Parse(held.Groups["ms"].Value, CultureInfo.InvariantCulture), 0, 2_000);
        Results.Append("session-end.txt", $"linux SIG{signal}: {held.Value}; exit 0 {exited.TotalMilliseconds:0} ms after the signal");
    }

    [Fact]
    public async Task A_page_guard_that_keeps_the_window_keeps_nothing_at_session_end()
    {
        await using var app = await ProbeApp.StartAsync("guard-keep");
        await app.WaitForAsync(@"probe note: guard keep");

        Signal("TERM", app.Id);

        Assert.Equal(0, await app.WaitForExitAsync(TimeSpan.FromSeconds(10)));
        var log = app.Log();
        Assert.Contains("session end: the page's guard answered Keep", log, StringComparison.Ordinal);
        Assert.DoesNotContain("close kept by the page's guard", log, StringComparison.Ordinal);
        Assert.Matches(Held, log);
    }

    [Fact]
    public async Task A_disposal_slower_than_the_budget_is_cut_at_four_seconds_after_the_final_line()
    {
        await using var app = await ProbeApp.StartAsync("", ("BRIDGE_PROBE_SLOW_DISPOSE_MS", "15000"));
        await app.WaitForAsync(@"probe note: plan done");
        var webProcesses = app.WebKitProcesses();

        var clock = Stopwatch.StartNew();
        Signal("TERM", app.Id);
        var exit = await app.WaitForExitAsync(TimeSpan.FromSeconds(10));
        var exited = clock.Elapsed;

        Assert.Equal(0, exit);
        var held = Regex.Match(app.Log(), Held);
        Assert.True(held.Success, app.Describe());
        Assert.Equal("window closed, last work still running", held.Groups["outcome"].Value);
        Assert.InRange(int.Parse(held.Groups["ms"].Value, CultureInfo.InvariantCulture), 3_800, 4_300);
        Assert.InRange(exited.TotalMilliseconds, 3_800, 5_500);
        Assert.DoesNotContain("services disposed", app.Log(), StringComparison.Ordinal);
        Assert.All(webProcesses, process => Assert.False(ProbeApp.IsRunning(process.Id), $"{process.Name} {process.Id} outlived the app."));
        Results.Append("session-end.txt", $"linux SIGTERM, 15 s disposal: {held.Value}; exit 0 {exited.TotalMilliseconds:0} ms after the signal");
    }

    [Fact]
    public async Task A_second_signal_ends_the_app_at_once_with_128_and_its_number()
    {
        await using var app = await ProbeApp.StartAsync("", ("BRIDGE_PROBE_SLOW_DISPOSE_MS", "15000"));
        await app.WaitForAsync(@"probe note: plan done");

        Signal("INT", app.Id);
        await app.WaitForAsync("services|window destroyed");
        var clock = Stopwatch.StartNew();
        Signal("TERM", app.Id);
        var exit = await app.WaitForExitAsync(TimeSpan.FromSeconds(10));
        var exited = clock.Elapsed;

        Assert.Equal(143, exit);
        Assert.Contains("SIGINT: the session ends", app.Log(), StringComparison.Ordinal);
        Assert.Contains("SIGTERM again: exit 143 at once", app.Log(), StringComparison.Ordinal);
        Assert.InRange(exited.TotalMilliseconds, 0, 1_000);
        Results.Append("session-end.txt", $"linux SIGINT then SIGTERM: exit 143 {exited.TotalMilliseconds:0} ms after the second signal");
    }

    private static void Signal(string signal, int process)
    {
        using var kill = Process.Start("kill", ["-" + signal, process.ToString(CultureInfo.InvariantCulture)]);
        kill.WaitForExit();
    }
}
