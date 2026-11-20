using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Spar.Testing;

namespace Spar.Tests;

/// <summary>
/// Hangs on the Linux host (plan M1c, build step 3), driven from outside like <see cref="LinuxHostTests"/>: the bridge
/// probe's page hangs, works or idles as the test's plan says, and the test reads the host's log and counts the
/// process's windows on the runner's X server (a dialog is a second one). The prompts are answered by the Development
/// build's scripted answers (<c>SPAR_DEV_PROMPT_ANSWERS</c>), never by input; against a Production probe, which has
/// none, the tests check what shows and leave the rest to the Development run. Times are recorded in
/// <c>artifacts/test-results/Spar.Tests/linux-hangs.txt</c>, not asserted beyond the design's bounds.
/// </summary>
[SupportedOSPlatform("linux")]
[Collection(nameof(LinuxWindowsCollection))]
public sealed class LinuxHangTests
{
    private const string Alarm = @"^\S+ WARN  Spar\.Host  \[[0-9a-f]{8}\] The page is not responding: it (?<why>[^(]+) \(";

    /// <summary>The alarm comes 15–20 s after a hang at the 5 s heartbeat; more for a slow start of the window.</summary>
    private static readonly TimeSpan AlarmPatience = TimeSpan.FromSeconds(40);

    [Fact]
    public async Task A_hung_page_raises_the_alarm_and_Reload_page_ends_its_web_process_and_reloads_it_once()
    {
        var development = LinuxAppRun.IsDevelopmentProbe();
        await using var app = LinuxAppRun.Start("hang", ("SPAR_DEV_PROMPT_ANSWERS", "second@1000"));
        await app.WaitForAsync(@"probe note: hang starts$");
        var hung = HostTime(app, "ui-ready at ", 0);

        var alarm = await app.WaitForAsync(Alarm + ".*$", AlarmPatience);
        Assert.Equal("did not answer three heartbeats", alarm.Groups["why"].Value);
        await app.WaitForAsync(@"hung-page dialog shown \(Wait / Reload page\)");
        await app.WaitForAsync(@"probe hang: Unresponsive after 15\.0 s");
        var alarmAfter = (HostTime(app, "The page is not responding", 0) - hung).TotalSeconds;
        Assert.InRange(alarmAfter, 10, 25);
        var windows = await WindowsAsync(app, 2);
        if (!development)
        {
            Record($"production: alarm {alarmAfter:0.0} s after the hang, the dialog shown ({windows} windows); no answer without the Development build");
            return;
        }

        var choice = await app.WaitForAsync(@"^\S+ .*hung-page dialog: Reload page, after [0-9.]+ s.*$");
        await app.WaitForAsync("web process ended by the host");
        await app.WaitForAsync(@"probe note: first document [0-9a-f]{16} \(reloaded\)");
        var log = app.Log();
        var readyAgain = Regex.Matches(log, @"^\S+ INFO  Spar\.Host  \[[0-9a-f]{8}\] Bridge: document [0-9a-f]{16} reported ui-ready\.$", RegexOptions.Multiline)[^1];
        var ended = At(Regex.Match(log, @"^\S+ .*web process ended by the host.*$", RegexOptions.Multiline)) - At(choice);
        Assert.Contains("hung page reloaded", log, StringComparison.Ordinal);
        Assert.Matches(@"probe hang: Reloaded after [0-9.]+ s", log);
        Assert.DoesNotContain("page reloaded after the web process ended", log, StringComparison.Ordinal);
        Assert.Equal(1, app.WindowCount());

        Assert.Equal(0, await app.CloseAsync());
        Record(string.Create(
            CultureInfo.InvariantCulture,
            $"development: alarm {alarmAfter:0.0} s after the hang; Reload page: web process ended {ended.TotalMilliseconds:0.0} ms and ui-ready {(LinuxAppRun.TimeOf(readyAgain) - LinuxAppRun.TimeOf(choice)).TotalMilliseconds:0} ms after the choice"));
    }

    [Fact]
    public async Task Wait_brings_the_dialog_back_after_4_8_16_then_32_heartbeats()
    {
        Assert.SkipUnless(LinuxAppRun.IsDevelopmentProbe(), "Needs the Development build's heartbeat interval and scripted answers.");
        await using var app = LinuxAppRun.Start(
            "hang",
            ("SPAR_DEV_HEARTBEAT_MS", "500"),
            ("SPAR_DEV_PROMPT_ANSWERS", "first@100,first@100,first@100,first@100,first@100,second@100"));

        await app.WaitForAsync(@"probe hang: Reloaded after", TimeSpan.FromSeconds(60));
        var shown = Regex.Matches(app.Log(), @"^\S+ .*hung-page dialog shown \(Wait / Reload page\).*$", RegexOptions.Multiline)
            .Select(At)
            .ToList();
        Assert.Equal(6, shown.Count);
        var returns = shown.Zip(shown.Skip(1), (first, next) => (next - first).TotalSeconds - 0.1).ToList();

        // 4, 8, 16, 32 and 32 ticks of 0.5 s, each up to one tick early (the schedule counts ticks).
        double[] expected = [2, 4, 8, 16, 16];
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.InRange(returns[i], expected[i] - 0.7, expected[i] + 0.3);
        }

        await app.WaitForAsync(@"probe note: first document [0-9a-f]{16} \(reloaded\)");
        Assert.Equal(0, await app.CloseAsync());
        Record("development, 500 ms heartbeat: after each Wait the dialog came back after "
            + string.Join(", ", returns.Select(value => value.ToString("0.0", CultureInfo.InvariantCulture))) + " s (2, 4, 8, 16, 16 s decided)");
    }

    [Fact]
    public async Task An_idle_page_and_work_of_3_and_10_seconds_raise_no_alarm()
    {
        await using var app = LinuxAppRun.Start("wait:20000,busy:3000,busy:10000,wait:6000");

        await app.WaitForAsync(@"probe note: plan done", TimeSpan.FromSeconds(60));
        var log = app.Log();
        Assert.DoesNotContain("The page is not responding", log, StringComparison.Ordinal);
        Assert.DoesNotContain("hung-page dialog", log, StringComparison.Ordinal);
        Assert.Equal(1, app.WindowCount());

        Assert.Equal(0, await app.CloseAsync());
        var heartbeat = Regex.Match(app.Log(), @"Heartbeat: (?<ticks>[0-9]+) ticks, (?<pings>[0-9]+) pings, longest tick on the UI thread (?<ms>[0-9.]+) ms");
        Assert.True(heartbeat.Success, app.Describe());
        Record($"{Build()}: idle 20 s, then work of 3 s and 10 s, then idle 6 s: no alarm; {heartbeat.Groups["ticks"].Value} ticks, "
            + $"{heartbeat.Groups["pings"].Value} pings, longest tick on the loop {heartbeat.Groups["ms"].Value} ms");
    }

    [Fact]
    public async Task A_page_that_hangs_before_it_connects_raises_the_alarm_and_closes_anyway()
    {
        var development = LinuxAppRun.IsDevelopmentProbe();
        await using var app = LinuxAppRun.Start("hang-before-connect", ("SPAR_DEV_PROMPT_ANSWERS", "first@500,second@500"));
        var alarm = await app.WaitForAsync(Alarm + ".*$", AlarmPatience);

        // The second document is the one that hangs: the first navigated to it once it was ready.
        var documents = Regex.Matches(app.Log(), @"^\S+ .*Bridge: document [0-9a-f]{16} opened\.", RegexOptions.Multiline);
        Assert.Equal(2, documents.Count);
        Assert.Contains("Heartbeat armed for the new document.", app.Log(), StringComparison.Ordinal);
        var alarmAfter = (HostTime(app, "The page is not responding", 0) - HostTime(app, "Bridge: document [0-9a-f]{16} opened", 1)).TotalSeconds;
        Assert.InRange(alarmAfter, 10, 25);
        await WindowsAsync(app, 2);
        if (!development)
        {
            Record($"production: a page hung before it connected, alarm {alarmAfter:0.0} s after its document opened");
            return;
        }

        // The dialog is modal: GTK does not pass the window manager's close on while it is open (pitfalls), so Wait
        // first, then the close, which finds the page hung and asks at once.
        var webProcesses = app.WebKitProcesses();
        await app.WaitForAsync(@"hung-page dialog: Wait");
        await WindowsAsync(app, 1);
        app.RequestClose();
        await app.WaitForAsync(@"hung-page dialog shown \(Wait / Close anyway\)");
        Assert.Equal(0, await app.WaitForExitAsync());
        var log = app.Log();
        Assert.Contains("hung-page dialog: Close anyway", log, StringComparison.Ordinal);
        Assert.Contains("web process of the hung page ended before closing", log, StringComparison.Ordinal);
        Assert.Matches(@"probe hang: ClosedAnyway after [0-9.]+ s", log);
        Assert.All(webProcesses, process => Assert.False(LinuxAppRun.IsRunning(process.Id), $"{process.Name} {process.Id} outlived the app."));
        Record($"development: a page hung before it connected, alarm {alarmAfter:0.0} s after its document opened; closed anyway, exit 0");
    }

    [Fact]
    public async Task A_close_that_meets_a_hung_page_asks_Wait_or_Close_anyway_and_ends_its_web_process()
    {
        Assert.SkipUnless(LinuxAppRun.IsDevelopmentProbe(), "Needs the Development build's scripted answers.");
        await using var app = LinuxAppRun.Start("hang", ("SPAR_DEV_PROMPT_ANSWERS", "first@500,second@500"));
        await app.WaitForAsync(@"probe note: hang starts");
        await Task.Delay(1_000, TestContext.Current.CancellationToken);
        var webProcesses = app.WebKitProcesses();

        app.RequestClose();
        var requested = At(await app.WaitForAsync(@"^\S+ .*close requested \(User\).*$"));
        var asked = At(await app.WaitForAsync(@"^\S+ .*hung-page dialog shown \(Wait / Close anyway\).*$"));
        await WindowsAsync(app, 2);
        var exit = await app.WaitForExitAsync(TimeSpan.FromSeconds(40));

        Assert.Equal(0, exit);
        var log = app.Log();
        Assert.Contains("did not answer the close request within 5000 ms", log, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(log, @"hung-page dialog shown \(Wait / Close anyway\)").Count);
        var closedAnyway = At(Regex.Match(log, @"^\S+ .*hung-page dialog: Close anyway.*$", RegexOptions.Multiline));
        var exited = At(Regex.Match(log, @"^\S+ .*\] exit 0 at .*$", RegexOptions.Multiline));
        Assert.Contains("web process of the hung page ended before closing", log, StringComparison.Ordinal);
        Assert.Matches(@"probe hang: Unresponsive after", log);
        Assert.Matches(@"probe hang: ClosedAnyway after [0-9.]+ s", log);
        Assert.Matches(@"web processes exited after [0-9]+ ms", log);
        Assert.All(webProcesses, process => Assert.False(LinuxAppRun.IsRunning(process.Id), $"{process.Name} {process.Id} outlived the app."));
        var askedAgain = Regex.Matches(log, @"^\S+ .*hung-page dialog shown \(Wait / Close anyway\).*$", RegexOptions.Multiline).Select(At).ToList();
        Record(string.Create(
            CultureInfo.InvariantCulture,
            $"development: a close 1 s into a hang: Wait / Close anyway {(asked - requested).TotalMilliseconds:0} ms after the request, again {(askedAgain[1] - askedAgain[0]).TotalSeconds:0.0} s after Wait; Close anyway to exit 0 in {(exited - closedAnyway).TotalMilliseconds:0} ms"));
    }

    [Fact]
    public async Task The_pages_close_guard_keeps_the_window_or_is_asked_about_when_it_stays_silent()
    {
        Assert.SkipUnless(LinuxAppRun.IsDevelopmentProbe(), "Needs the Development build's scripted answers.");
        await using (var kept = LinuxAppRun.Start("guard-keep"))
        {
            await kept.WaitForAsync(@"probe note: guard keep");
            kept.RequestClose();
            await kept.WaitForAsync(@"close kept by the page's guard");
            Assert.Contains("the page answered Keep", kept.Log(), StringComparison.Ordinal);
            Assert.False(kept.HasExited);
        }

        await using var silent = LinuxAppRun.Start("guard-silent", ("SPAR_DEV_PROMPT_ANSWERS", "second@500"));
        await silent.WaitForAsync(@"probe note: guard silent");
        silent.RequestClose();
        var requested = At(await silent.WaitForAsync(@"^\S+ .*close request [0-9]+ sent to the page \(user\).*$"));
        var asked = At(await silent.WaitForAsync(@"^\S+ .*hung-page dialog shown \(Wait / Close anyway\).*$"));
        Assert.Equal(0, await silent.WaitForExitAsync());
        var log = silent.Log();
        Assert.Contains("its close guard has not answered", log, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"probe hang:", log);
        Assert.DoesNotContain("web process of the hung page ended", log, StringComparison.Ordinal);
        Record(string.Create(CultureInfo.InvariantCulture, $"development: the page's guard kept the window; a silent guard was asked about {(asked - requested).TotalMilliseconds:0} ms after the request, Close anyway, exit 0"));
    }

    [Fact]
    public async Task A_long_shutdown_asks_Keep_waiting_or_Exit_now_and_Exit_now_exits_with_code_10()
    {
        var development = LinuxAppRun.IsDevelopmentProbe();
        var dispose = development ? "120000" : "15000";
        await using var app = LinuxAppRun.Start("", ("BRIDGE_PROBE_SLOW_DISPOSE_MS", dispose), ("SPAR_DEV_PROMPT_ANSWERS", "first@500,second@500"));
        await app.WaitForAsync(@"probe note: plan done");
        var webProcesses = app.WebKitProcesses();

        app.RequestClose();
        var closing = At(await app.WaitForAsync(@"^\S+ .*\] closing at .*$"));
        var prompt = At(await app.WaitForAsync(@"^\S+ .*shutdown prompt shown \(Keep waiting / Exit now\).*$", TimeSpan.FromSeconds(30)));
        await WindowsAsync(app, 1);
        var firstAfter = (prompt - closing).TotalSeconds;
        Assert.InRange(firstAfter, 9.5, 12);
        if (!development)
        {
            // The disposal ends at 15 s: the prompt is taken down and the app exits by itself.
            Assert.Equal(0, await app.WaitForExitAsync(TimeSpan.FromSeconds(30)));
            Assert.Contains("services disposed", app.Log(), StringComparison.Ordinal);
            Record($"production: a 15 s disposal: the shutdown prompt {firstAfter:0.0} s after the teardown began, gone when the shutdown finished, exit 0");
            return;
        }

        var exit = await app.WaitForExitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(10, exit);
        var log = app.Log();
        var prompts = Regex.Matches(log, @"^\S+ .*shutdown prompt shown \(Keep waiting / Exit now\).*$", RegexOptions.Multiline).Select(At).ToList();
        Assert.Equal(2, prompts.Count);
        Assert.Contains("shutdown prompt: Keep waiting", log, StringComparison.Ordinal);
        Assert.Matches(@"shutdown prompt: Exit now after [0-9.]+ s; exit 10", log);
        Assert.DoesNotContain("services disposed", log, StringComparison.Ordinal);
        Assert.All(webProcesses, process => Assert.False(LinuxAppRun.IsRunning(process.Id), $"{process.Name} {process.Id} outlived the app."));
        Record($"development: a 120 s disposal: the shutdown prompt {firstAfter:0.0} s and again {(prompts[1] - closing).TotalSeconds:0.0} s after the teardown began; Exit now, exit 10");
    }

    [Fact]
    public async Task The_stall_probe_names_a_stalled_main_loop_and_a_Production_build_keeps_it_only_when_asked()
    {
        if (!LinuxAppRun.IsDevelopmentProbe())
        {
            await using (var plain = LinuxAppRun.Start(""))
            {
                await plain.WaitForAsync(@"probe note: plan done");
                Assert.DoesNotContain("UI-thread stall probe on.", plain.Log(), StringComparison.Ordinal);
                Assert.Equal(0, await plain.CloseAsync());
            }

            await using var kept = LinuxAppRun.Start("", ("BRIDGE_PROBE_KEEP_STALL_PROBE", "1"));
            await kept.WaitForAsync(@"probe note: plan done");
            Assert.Contains("UI-thread stall probe on.", kept.Log(), StringComparison.Ordinal);
            Assert.Equal(0, await kept.CloseAsync());
            Record("production: no stall probe without KeepStallProbe(), the probe on with it");
            return;
        }

        await using var app = LinuxAppRun.Start("", ("SPAR_DEV_UI_STALL_MS", "300"));
        var stall = await app.WaitForAsync(@"The UI thread stalled (?<ms>[0-9.]+) ms.*$");
        Assert.Contains("development stall trigger", stall.Value, StringComparison.Ordinal);
        Assert.Equal(0, await app.CloseAsync());
        var others = Regex.Matches(app.Log(), @"The UI thread stalled [0-9.]+ ms.*$", RegexOptions.Multiline).Count - 1;
        Record($"development: a 300 ms stall of the main loop reported as {stall.Groups["ms"].Value} ms in the development stall trigger; {others} other stalls over 50 ms");
    }

    [Fact]
    public async Task The_automated_mode_shows_no_dialog_and_closes_a_hung_page_without_asking()
    {
        Assert.SkipUnless(LinuxAppRun.IsDevelopmentProbe(), "Needs the Development build's automated mode.");
        await using var app = LinuxAppRun.Start("hang", ("SPAR_AUTOMATED", "1"), ("SPAR_DEV_HEARTBEAT_MS", "500"));
        await app.WaitForAsync(Alarm, AlarmPatience);
        await app.WaitForAsync("The automated mode shows no hung-page dialog.");
        Assert.Equal(1, app.WindowCount());

        app.RequestClose();
        Assert.Equal(0, await app.WaitForExitAsync());
        var log = app.Log();
        Assert.Contains("The automated mode closes a page that does not answer without asking.", log, StringComparison.Ordinal);
        Assert.Contains("web process of the hung page ended before closing", log, StringComparison.Ordinal);
        Assert.DoesNotContain("dialog shown", log, StringComparison.Ordinal);
    }

    /// <summary>Waits until the process shows <paramref name="count"/> windows (its window and its dialogs), for at most 5 s.</summary>
    private static async Task<int> WindowsAsync(LinuxAppRun app, int count)
    {
        var limit = Stopwatch.StartNew();
        int shown;
        while ((shown = app.WindowCount()) != count)
        {
            Assert.True(limit.Elapsed < TimeSpan.FromSeconds(5), $"{shown} windows instead of {count}.\n{app.Describe()}");
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        return shown;
    }

    /// <summary>
    /// The host's own time of a step's log line (its "at N ms", from the host's start): the log's wall-clock times
    /// jumped by about a second in WSL during a run (pitfalls), the host's clock is monotonic.
    /// </summary>
    private static TimeSpan At(Match line)
    {
        var at = Regex.Match(line.Value, @" at (?<ms>[0-9.]+) ms$");
        Assert.True(at.Success, $"No host time in '{line.Value}'.");
        return TimeSpan.FromMilliseconds(double.Parse(at.Groups["ms"].Value, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// The host's own time of the <paramref name="index"/>th host line matching <paramref name="pattern"/>, from its
    /// standard error, where every host line carries its time since the host started (<c>spar info +N ms: …</c>).
    /// </summary>
    private static TimeSpan HostTime(LinuxAppRun app, string pattern, int index)
    {
        var lines = Regex.Matches(app.Output, @"^spar \w+ \+(?<ms>[0-9.]+) ms: .*" + pattern, RegexOptions.Multiline);
        Assert.True(lines.Count > index, $"No host line {index} for '{pattern}'.\n{app.Describe()}");
        return TimeSpan.FromMilliseconds(double.Parse(lines[index].Groups["ms"].Value, CultureInfo.InvariantCulture));
    }

    private static string Build() => LinuxAppRun.IsDevelopmentProbe() ? "development" : "production";

    private static void Record(string line)
    {
        var results = Path.Combine(TestFolders.Artifacts(), "test-results", "Spar.Tests");
        Directory.CreateDirectory(results);
        File.AppendAllLines(Path.Combine(results, "linux-hangs.txt"), [string.Create(CultureInfo.InvariantCulture, $"{DateTimeOffset.Now:HH:mm:ss} {line}")]);
    }
}
