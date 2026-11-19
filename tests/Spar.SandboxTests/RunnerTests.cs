using System.Globalization;
using System.Text.RegularExpressions;
using Spar.Testing;

namespace Spar.SandboxTests;

/// <summary>
/// The runner's own behaviour, read on the host from the suite's run: results through the mapped folder, the deadline
/// ending a run in the guest while the finished tests' results stay, no sandbox and no client window left, the client
/// window never touched without the user-session lane, and the real sign-out's session end read from the app's log.
/// </summary>
public sealed partial class RunnerTests
{
    [Fact]
    public async Task The_system_lane_writes_its_results_through_the_mapped_folder()
    {
        var run = await RanAsync();

        var job = run.Job(SandboxSuite.WindowSystem);
        var checks = Path.Combine(job.TestResultsFolder, SandboxSuite.WindowTests, "sandbox", "checks.txt");
        Assert.True(File.Exists(checks), $"No {checks}.");
        Assert.Contains("system lane as", await File.ReadAllTextAsync(checks, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(job.OutputFolder, "results.trx")), "The TRX report came back.");
        Assert.All(job.Tests, test => Assert.Equal(GuestOutcome.Passed, test.Outcome));
    }

    [Fact]
    public async Task A_run_ended_at_its_deadline_keeps_the_results_of_the_tests_that_finished()
    {
        var run = await RanAsync();

        var job = run.Job(SandboxSuite.Deadline);
        Assert.True(job.TimedOut);
        Assert.Contains(job.Tests, test => test.Outcome == GuestOutcome.Unfinished && test.DisplayName.EndsWith("Runs_until_the_runner_ends_it", StringComparison.Ordinal));
        Assert.Contains(job.Tests, test => test.Outcome == GuestOutcome.Passed);
        Assert.False(File.Exists(Path.Combine(job.OutputFolder, "results.trx")), "xUnit writes its TRX only at the end, which a killed run never reaches.");
        Assert.InRange(job.Elapsed, SandboxSuite.DeadlineLimit, SandboxSuite.DeadlineLimit + TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task No_sandbox_is_left_and_the_client_window_is_touched_only_by_the_user_session_lane()
    {
        var run = await RanAsync();

        var running = await Wsb.ListAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(running);
        Assert.Empty(running);
        foreach (var session in run.Sessions)
        {
            var connected = session.Steps.Any(step => step.Name == "connect");
            Assert.Equal(connected, session.Minimizes > 0 || session.Steps.Any(step => step.Name == "client-window"));
            Assert.Contains(session.Steps, step => step.Name == "stop");
        }

        if (!run.UserSessionAllowed)
        {
            Assert.DoesNotContain(run.Sessions.SelectMany(session => session.Steps), step => step.Name == "connect");
        }
    }

    [Fact]
    public async Task Signing_the_guest_out_ends_the_app_within_the_session_end_budget()
    {
        var run = await RanAsync();
        Assert.SkipUnless(run.UserSessionAllowed, SandboxDesktopGate.SkipReason);

        var job = run.Job(SandboxSuite.SignOut);
        var checks = await File.ReadAllLinesAsync(Path.Combine(job.TestResultsFolder, SandboxSuite.WindowTests, "sandbox", "checks.txt"), TestContext.Current.CancellationToken);
        var root = checks.Select(line => line[(line.IndexOf(' ', StringComparison.Ordinal) + 1)..])
            .Single(line => line.StartsWith("sign-out data root: ", StringComparison.Ordinal))["sign-out data root: ".Length..];
        Assert.DoesNotContain(checks, line => line.Contains("still running", StringComparison.Ordinal));

        // The runner copied the logs of the job's data roots from the guest's disk: the app's log, under the same path.
        var relative = Path.GetRelativePath(Path.Combine(@"C:\spar\work", job.Stage, "artifacts"), root);
        var log = Path.Combine(job.OutputFolder, "artifacts", relative, "LocalAppData", "com.example.tests.WindowHost", "logs", "app.log");
        Assert.True(File.Exists(log), $"The app's log did not come back: {log}");
        var text = await File.ReadAllTextAsync(log, TestContext.Current.CancellationToken);
        Assert.Contains("session ending", text, StringComparison.Ordinal);
        var held = HeldPattern().Match(text);
        Assert.True(held.Success, "The app's log has no 'WM_ENDSESSION held' line.\n" + text);
        Assert.InRange(int.Parse(held.Groups[1].Value, CultureInfo.InvariantCulture), 0, 4_100);
    }

    private static async Task<SuiteRun> RanAsync()
    {
        var run = await SandboxSuite.GetAsync();
        Assert.SkipUnless(run.Ran, run.SkipReason ?? "Windows Sandbox did not run.");
        return run;
    }

    [GeneratedRegex(@"WM_ENDSESSION held (\d+) ms")]
    private static partial Regex HeldPattern();
}
