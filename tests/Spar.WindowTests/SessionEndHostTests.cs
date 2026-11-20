using System.Collections.Concurrent;
using System.Diagnostics;
using Spar.Testing;
using static Spar.WindowTests.HostPage;

namespace Spar.WindowTests;

/// <summary>
/// The end of the OS session (plan section 5's required host tests), on the test's window and on WindowHost's
/// Production executable: the window closes with its reason inside the held <c>WM_ENDSESSION</c>, within 4 s however
/// slow the guards are and at once when they are quick; the last lines reach the log file even when Windows ends the
/// process right after (found by Windows Sandbox's real sign-out); and a renderer lost during the session end is
/// neither reloaded nor waited for.
/// <para>
/// They are one class so that they never run at the same time as each other: every test window of a test process
/// shares one UI thread, and a held <c>WM_ENDSESSION</c> pumps that thread's messages inside its handler, so a second
/// window's session end started meanwhile runs inside the first one's, and the first cannot return before it
/// (<c>spikes/renderer-sharing</c>: a quick session end held 3.0–3.1 s under another's). The rest of the suite runs
/// beside them: nothing else nests a message loop on that thread.
/// </para>
/// </summary>
public sealed class SessionEndHostTests
{
    /// <summary><c>WM_QUERYENDSESSION</c>, <c>WM_ENDSESSION</c> and <c>ENDSESSION_LOGOFF</c>.</summary>
    private const uint QueryEndSession = 0x0011;
    private const uint EndSession = 0x0016;
    private static readonly nint Logoff = unchecked((nint)0x80000000);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// Windows ends a session with <c>WM_QUERYENDSESSION</c> and <c>WM_ENDSESSION</c>; Linux with logind's
    /// <c>PrepareForShutdown</c> and the session's end, which the Linux host does not follow yet. The session end's close
    /// rules run on both hosts through <c>app.Window.CloseAsync(new() { Reason = CloseReason.SessionEnd })</c> (CloseTests).
    /// </summary>
    private static void SkipUnlessWindowsSession() =>
        WindowSupport.SkipUnlessWindows("The OS session's end is Windows' WM_ENDSESSION here; the Linux host does not follow logind's session end yet (M1c leaves it).");

    [Fact]
    [Trait("Surface", "Window")]
    public async Task The_end_of_the_session_closes_the_window_with_its_reason_and_settles_its_calls()
    {
        SkipUnlessWindowsSession();
        var phases = new ConcurrentQueue<string>();
        await using var host = await HostPage.OpenAsync(options => options.WindowPhase = phases.Enqueue);
        var activity = host.Activity;
        await host.GuardAsync("keep");
        await host.ClickAsync("Start a long call");
        await host.WaitForRunningAsync(1);

        var allowed = await host.App.Window.SendMessageAsync(QueryEndSession, 0, Logoff);

        // WM_ENDSESSION returns only once the window has closed and the services are disposed: Windows may end the
        // process when it does.
        await host.App.Window.SendMessageAsync(EndSession, 1, Logoff);

        Assert.Equal(1, allowed);
        Assert.True(phases.Contains("closing"), "WM_ENDSESSION returned before the window's controller closed.");
        Assert.NotNull(activity.DisposedAt);
        Assert.Equal(0, await host.App.Window.Closed.WaitAsync(TimeSpan.FromSeconds(15), Token));

        // The page's guard ran with its reason, and its keep kept nothing; then the backend's.
        Assert.Equal(["close sessionEnd automated keep"], NotesOf(activity));
        Assert.Equal(CloseReason.SessionEnd, Assert.Single(activity.CloseRequests).Reason);
        Assert.Contains("cancelled hold", activity.Events);
        Assert.Equal(0, activity.Running);
    }

    [Fact]
    [Trait("Surface", "Window")]
    public async Task The_end_of_the_session_holds_the_message_under_four_seconds_however_slow_the_guards_and_the_cleanup()
    {
        SkipUnlessWindowsSession();
        var phases = new ConcurrentQueue<string>();
        await using var host = await HostPage.OpenAsync(options => options.WindowPhase = phases.Enqueue);
        var activity = host.Activity;
        await host.GuardAsync("slow");
        activity.BackendGuardDelay = TimeSpan.FromSeconds(5);
        activity.DisposeDelay = TimeSpan.FromSeconds(5);
        await host.ClickAsync("Start a slow call");
        await host.WaitForRunningAsync(1);

        var held = Stopwatch.StartNew();
        await host.App.Window.SendMessageAsync(EndSession, 1, Logoff);
        held.Stop();

        // Page guard 2 s, backend guard the rest of 3 s, then the teardown and the disposal until 4 s: the message
        // returns with the disposal still running, as Windows may end the process from then on.
        var hold = Assert.Single(phases, phase => phase.StartsWith("session end held ", StringComparison.Ordinal));
        Results.Append("session-end.txt", $"slow guards and disposal: WM_ENDSESSION held {held.ElapsedMilliseconds} ms ({hold})");
        Assert.InRange(held.ElapsedMilliseconds, 3_500, 4_250);
        Assert.Null(activity.DisposedAt);
        Assert.Equal(["close sessionEnd automated slow"], NotesOf(activity));
        Assert.Equal(CloseReason.SessionEnd, Assert.Single(activity.CloseRequests).Reason);
        Assert.Equal(0, await host.App.Window.Closed.WaitAsync(TimeSpan.FromSeconds(15), Token));
        await Waits.UntilAsync(() => activity.DisposedAt is not null, TimeSpan.FromSeconds(10), "the slow disposal to finish");
    }

    [Fact]
    [Trait("Surface", "Window")]
    public async Task The_end_of_the_session_returns_as_soon_as_the_window_and_the_services_are_done()
    {
        SkipUnlessWindowsSession();
        await using var host = await HostPage.OpenAsync();
        var activity = host.Activity;

        var held = Stopwatch.StartNew();
        await host.App.Window.SendMessageAsync(EndSession, 1, Logoff);
        held.Stop();

        Results.Append("session-end.txt", $"fast guards and disposal: WM_ENDSESSION held {held.ElapsedMilliseconds} ms");
        Assert.InRange(held.ElapsedMilliseconds, 0, 2_000);
        Assert.NotNull(activity.DisposedAt);
        Assert.Equal(0, await host.App.Window.Closed.WaitAsync(TimeSpan.FromSeconds(15), Token));
    }

    [Fact]
    [Trait("Surface", "BlackBox")]
    public async Task The_session_ends_last_lines_are_in_the_log_when_Windows_ends_the_process_right_after()
    {
        SkipUnlessWindowsSession();
        WindowSupport.SkipUnlessBlackBox();
        await using var app = await SparBlackBox.LaunchAsync(Projects.WindowHost, cancellationToken: Token);

        Assert.True(await app.Automation.EndSessionAsync(app.ProcessId, Token));
        app.Kill();
        Assert.True(await app.WaitForProcessTreeExitAsync());

        var log = Read(app.LogFile);
        Assert.Contains("session ending", log, StringComparison.Ordinal);
        Assert.Matches(@"session end: window closed[^\n]*; WM_ENDSESSION held \d+ ms", log);
        Results.Append("session-end.txt", "black-box session end then kill: the last line was in the log");
    }

    /// <summary>
    /// The page's guard would hold the session end for 2 s; its renderer is ended 300 ms in. The page is gone, so once
    /// WebView2 reports it the close goes on without its answer and without waiting for its close work, and nothing
    /// is reloaded. How soon WebView2 reports it depends on the machine's load (11–872 ms after the kill in a
    /// solution-wide run), so the test checks what the host did after the report, by its log's times, not the
    /// message's whole hold. Before, the hold waited out the page's 2 s and the close work's 1 s: about 3.0 s.
    /// </summary>
    [Fact]
    [Trait("Surface", "Window")]
    public async Task A_renderer_lost_during_the_session_end_is_neither_reloaded_nor_waited_for()
    {
        SkipUnlessWindowsSession();
        var phases = new ConcurrentQueue<string>();
        await using var host = await HostPage.OpenAsync(options => options.WindowPhase = phases.Enqueue);
        await host.GuardAsync("slow");
        var renderers = await host.App.Window.RendererProcessesAsync();
        Assert.NotEmpty(renderers);

        var held = Stopwatch.StartNew();
        var ending = host.App.Window.SendMessageAsync(EndSession, 1, Logoff);
        await Task.Delay(300, Token);
        foreach (var id in renderers)
        {
            using var renderer = Process.GetProcessById(id);
            renderer.Kill();
        }

        var killed = held.ElapsedMilliseconds;
        await ending;
        held.Stop();
        Assert.Equal(0, await host.App.Window.Closed.WaitAsync(TimeSpan.FromSeconds(15), Token));
        var file = Path.Combine(host.App.Folders.Logs, "app.log");
        await Waits.UntilAsync(() => File.Exists(file) && Read(file).Contains("during shutdown", StringComparison.Ordinal), TimeSpan.FromSeconds(5), "the lost renderer in the log");
        var log = Read(file);
        var lost = Since(log, "during shutdown");
        // At session end the window stays until the message returns, so the page's part ends when the controller closes.
        var closed = Since(log, "controller closed");
        Results.Append("session-end.txt", $"renderer test: renderer ended at {killed} ms, seen lost at {lost:0} ms, controller closed at {closed:0} ms (host's log, from 'session ending'); WM_ENDSESSION held {held.ElapsedMilliseconds} ms");

        // From the report of the lost renderer to the controller's close, nothing waits for the page.
        var context = $"WM_ENDSESSION held {held.ElapsedMilliseconds} ms. The host's log:{Environment.NewLine}{SessionLines(log)}";
        Assert.True(lost >= 0 && closed >= 0, context);
        Assert.True(closed - lost < 500, context);
        Assert.Contains("closed (renderer exited)", log, StringComparison.Ordinal);
        Assert.DoesNotContain("did not answer within", log, StringComparison.Ordinal);
        Assert.DoesNotContain("page close work timed out", log, StringComparison.Ordinal);
        Assert.DoesNotContain("renderer reloaded", log, StringComparison.Ordinal);
        Assert.DoesNotContain("browser recreated", log, StringComparison.Ordinal);
        Assert.DoesNotContain(phases, phase => phase.Contains("renderer reloaded", StringComparison.Ordinal));
    }

    /// <summary>
    /// Milliseconds from the host's "session ending" line to the first line containing <paramref name="marker"/>, by
    /// the lines' own times (<c>2026-11-19T13:18:16.645Z INFO …</c>), or -1 when either is missing.
    /// </summary>
    private static double Since(string log, string marker)
    {
        var lines = log.Split('\n');
        var start = lines.FirstOrDefault(line => line.Contains("session ending", StringComparison.Ordinal));
        var end = lines.FirstOrDefault(line => line.Contains(marker, StringComparison.Ordinal));
        if (start is null || end is null || !TryTime(start, out var from) || !TryTime(end, out var to))
        {
            return -1;
        }

        return (to - from).TotalMilliseconds;
    }

    private static bool TryTime(string line, out DateTimeOffset time)
    {
        var space = line.IndexOf(' ', StringComparison.Ordinal);
        return DateTimeOffset.TryParse(space > 0 ? line[..space] : line, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out time);
    }

    /// <summary>The host's lines from the session end's start on, the part a slow session end is explained by.</summary>
    private static string SessionLines(string log)
    {
        var start = log.IndexOf("session ending", StringComparison.Ordinal);
        if (start < 0)
        {
            return log;
        }

        var lineStart = log.LastIndexOf('\n', start) + 1;
        return log[lineStart..];
    }

    private static string Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
