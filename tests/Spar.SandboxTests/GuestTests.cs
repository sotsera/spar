using Spar.Testing;

namespace Spar.SandboxTests;

/// <summary>
/// Every guest test, reported here as a test of its own (decision record of step 8c, choice "one host result per guest
/// test"): its name is the job and the guest test's name, its outcome the guest's (a failure carries the guest's
/// message and stack trace, a skip its reason), so a run reads like any other test run. Each job also has a row of its
/// own that fails when the job did not come back (no exit code, a deadline it was not meant to reach). The rows exist
/// only once the sandbox has run, so the theory is not enumerated at discovery; a row's argument is its number.
/// </summary>
public sealed class GuestTests
{
    public static async Task<IEnumerable<TheoryDataRow<int>>> Cases()
    {
        var cases = await GuestCase.AllAsync();
        return cases.Select((guestCase, index) => new TheoryDataRow<int>(index)
        {
            TestDisplayName = guestCase.Name,
            Skip = guestCase.Skip,
        });
    }

    [Theory(DisableDiscoveryEnumeration = true)]
    [MemberData(nameof(Cases))]
    public async Task Guest(int row)
    {
        var guestCase = (await GuestCase.AllAsync())[row];
        if (guestCase.Skip is { } reason)
        {
            Assert.Skip(reason);
        }

        if (guestCase.Test is not { } test)
        {
            JobCameBack(guestCase.Result!);
            return;
        }

        var result = guestCase.Result!;
        switch (test.Outcome)
        {
            case GuestOutcome.Passed:
                break;
            case GuestOutcome.Failed:
                Assert.Fail($"Failed in the guest ({result.Stage}, {result.OutputFolder}):\n{test.Message}\n{test.Output}");
                break;
            case GuestOutcome.Unfinished:
                // The deadline job's probe is ended by the runner, the sign-out's test by the session's end: the host's own checks read them.
                Assert.True(result.Job.Name is SandboxSuite.Deadline or SandboxSuite.SignOut, $"{test.Message} ({result.Problem})");
                break;
            default:
                Assert.Skip(test.Message ?? test.Outcome.ToString());
                break;
        }
    }

    private static void JobCameBack(SandboxJobResult result)
    {
        if (result.Job.Name == SandboxSuite.Deadline)
        {
            // Its probe outlives the deadline on purpose: the run must have been ended, and still come back.
            Assert.True(result.TimedOut, "The deadline job was meant to reach its deadline.");
            return;
        }

        if (result.Job.Name == SandboxSuite.SignOut)
        {
            // The sign-out ends the test process and its script with the session: no exit code comes back, but no deadline either.
            Assert.False(result.TimedOut, "The sign-out job reached its deadline: the session did not end.");
            Assert.NotEmpty(result.Tests);
            return;
        }

        // A guest test that failed fails its own row; here only that the executable finished and reported.
        Assert.True(result.Problem is null, $"{result.Problem} ({result.OutputFolder})");
        Assert.NotEmpty(result.Tests);
    }
}

/// <summary>One row: a job's own check (no test), a guest test, or a test the run left out (with the skip's reason).</summary>
internal sealed record GuestCase(string Name, SandboxJobResult? Result, GuestTestResult? Test, string? Skip)
{
    private static readonly Lazy<Task<IReadOnlyList<GuestCase>>> Cases = new(BuildAsync);

    public static Task<IReadOnlyList<GuestCase>> AllAsync() => Cases.Value;

    private static async Task<IReadOnlyList<GuestCase>> BuildAsync()
    {
        var run = await SandboxSuite.GetAsync();
        if (!run.Ran)
        {
            return [new GuestCase("Windows Sandbox", null, null, run.SkipReason)];
        }

        var cases = new List<GuestCase>();
        foreach (var result in run.Results)
        {
            cases.Add(new GuestCase($"[{result.Job.Name}] the job ran and came back", result, null, null));
            foreach (var test in result.Tests)
            {
                var skip = test.Outcome is GuestOutcome.Skipped or GuestOutcome.NotRun
                    ? $"Skipped in the guest: {test.Message ?? test.Outcome.ToString()}"
                    : null;
                cases.Add(new GuestCase($"[{result.Job.Name}] {test.DisplayName}", result, test, skip));
            }
        }

        foreach (var (job, test) in run.Skipped)
        {
            cases.Add(new GuestCase($"[{job}] {test}", null, null, SandboxDesktopGate.SkipReason));
        }

        return cases;
    }
}
