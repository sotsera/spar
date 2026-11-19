using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Spar.Testing;

namespace Spar.SandboxTests;

/// <summary>
/// The suite's one Windows Sandbox run, made the first time a test asks for it and shared by every test of the process:
/// the payloads this project's build published, the jobs of each lane, the runner, and the results. Without Windows
/// Sandbox the run is a skip reason; without the allow, the user-session lane's tests are listed from the payloads
/// (no sandbox) and become skips that name the variable.
/// </summary>
internal static class SandboxSuite
{
    public const string WindowTests = "Spar.WindowTests";
    public const string Notebook = "Notebook.EndToEndTests";

    /// <summary>The jobs by name, as the rows and the host's own checks name them.</summary>
    public const string WindowSystem = "window-tests/System";
    public const string NotebookSystem = "notebook/System";
    public const string Deadline = "window-tests/Deadline";
    public const string WindowUser = "window-tests/UserSession";
    public const string NotebookUser = "notebook/UserSession";
    public const string CleanMachine = "notebook/CleanMachine";
    public const string SignOut = "window-tests/SignOut";

    /// <summary>The deadline of the deadline check's job: its probe test outlives it, the lane's other tests do not.</summary>
    public static readonly TimeSpan DeadlineLimit = TimeSpan.FromSeconds(45);

    private static readonly Lazy<Task<SuiteRun>> Run = new(() => RunAsync(CancellationToken.None));

    public static Task<SuiteRun> GetAsync() => Run.Value;

    private static async Task<SuiteRun> RunAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return SuiteRun.Unavailable("Windows Sandbox's runner needs Windows 11 (wsb).");
        }

        var (available, reason) = await SandboxRunner.AvailableAsync(cancellationToken);
        if (!available)
        {
            return SuiteRun.Unavailable(reason);
        }

        var allowed = SandboxDesktopGate.IsAllowed;
        var jobs = new List<SandboxJob>
        {
            Job(WindowSystem, WindowTests, SandboxLane.System, TimeSpan.FromMinutes(5), "System"),
            Job(NotebookSystem, Notebook, SandboxLane.System, TimeSpan.FromMinutes(5), "System"),
            Job(Deadline, WindowTests, SandboxLane.System, DeadlineLimit, "Deadline", "System"),
        };

        var skipped = new List<(string Job, string Test)>();
        var userJobs = new[]
        {
            Job(WindowUser, WindowTests, SandboxLane.UserSession, TimeSpan.FromMinutes(10), "UserSession"),
            Job(NotebookUser, Notebook, SandboxLane.UserSession, TimeSpan.FromMinutes(10), "UserSession"),
            Job(CleanMachine, Notebook, SandboxLane.UserSession, TimeSpan.FromMinutes(5), "CleanMachine") with { FreshSandbox = true },
            Job(SignOut, WindowTests, SandboxLane.UserSession, TimeSpan.FromMinutes(3), "SignOut") with
            {
                FreshSandbox = true,
                CollectAfter = TimeSpan.FromSeconds(15),
            },
        };

        if (allowed)
        {
            jobs.AddRange(userJobs);
        }
        else
        {
            foreach (var job in userJobs)
            {
                foreach (var test in await ListAsync(job, cancellationToken))
                {
                    skipped.Add((job.Name, test));
                }
            }
        }

        var folder = Path.Combine(TestArtifacts.Results("Spar.SandboxTests"), DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        var runner = new SandboxRunner(folder);
        var clock = Stopwatch.StartNew();
        var results = await runner.RunAsync(jobs, cancellationToken);
        var run = new SuiteRun(true, null, allowed, folder, clock.Elapsed, results, skipped, runner.Sessions);
        await File.WriteAllTextAsync(Path.Combine(folder, "summary.txt"), run.Summary(), cancellationToken);
        return run;
    }

    /// <summary>A job running a payload's tests of the given <c>Sandbox</c> trait values (any of them).</summary>
    private static SandboxJob Job(string name, string payload, SandboxLane lane, TimeSpan deadline, params string[] traits)
    {
        var arguments = new List<string>();
        foreach (var trait in traits)
        {
            arguments.Add("-trait");
            arguments.Add($"Sandbox={trait}");
        }

        arguments.Add("-explicit");
        arguments.Add("on");
        return new SandboxJob(name, Payload(payload), payload + ".exe", lane, deadline) { Arguments = arguments };
    }

    /// <summary>The folder this project's build published <paramref name="name"/> into (assembly metadata).</summary>
    public static string Payload(string name)
    {
        var folder = typeof(SandboxSuite).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "SandboxPayload:" + name)?.Value;
        var executable = folder is null ? null : Path.Combine(folder, name + ".exe");
        if (executable is null || !File.Exists(executable))
        {
            throw new InvalidOperationException(
                $"{name} is not published for the Sandbox ({executable ?? "no folder"}): building tests/Spar.SandboxTests on Windows publishes it.");
        }

        return folder!;
    }

    /// <summary>The display names of a job's tests, listed by its executable on the host (it lists them, runs nothing).</summary>
    private static async Task<IReadOnlyList<string>> ListAsync(SandboxJob job, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(Path.Combine(job.PayloadFolder, job.Executable))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "-list", "tests/json" }.Concat(job.Arguments))
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var json = output[output.IndexOf('[', StringComparison.Ordinal)..];
        using var document = JsonDocument.Parse(json);
        return [.. document.RootElement.EnumerateArray().Select(item => item.GetString()!)];
    }
}

/// <summary>The suite's run: whether it ran, the jobs' results, the user-session tests it left out, and the sessions' timings.</summary>
internal sealed record SuiteRun(
    bool Ran,
    string? SkipReason,
    bool UserSessionAllowed,
    string Folder,
    TimeSpan Elapsed,
    IReadOnlyList<SandboxJobResult> Results,
    IReadOnlyList<(string Job, string Test)> Skipped,
    IReadOnlyList<SandboxSessionSummary> Sessions)
{
    public static SuiteRun Unavailable(string reason) => new(false, reason, false, "", TimeSpan.Zero, [], [], []);

    public SandboxJobResult Job(string name) => Results.Single(result => result.Job.Name == name);

    /// <summary>The run's timings per session and step, and each job's outcome, for the evidence.</summary>
    public string Summary()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"Run {Path.GetFileName(Folder)}: {Elapsed.TotalSeconds:0.0} s, user session {(UserSessionAllowed ? "allowed" : "not allowed")}\n");
        foreach (var session in Sessions)
        {
            text.Append(CultureInfo.InvariantCulture, $"\nsession {session.Name}: {session.Elapsed.TotalMilliseconds:0} ms, client minimized {session.Minimizes} times\n");
            foreach (var step in session.Steps.Where(step => step.Took is not null))
            {
                text.Append(CultureInfo.InvariantCulture, $"  {step.Name,-22} {step.Took!.Value.TotalMilliseconds,9:0} ms  (at {step.At.TotalMilliseconds:0})\n");
            }
        }

        text.Append('\n');
        foreach (var result in Results)
        {
            var counts = string.Join(", ", result.Tests.GroupBy(test => test.Outcome).Select(group => $"{group.Count()} {group.Key}"));
            text.Append(CultureInfo.InvariantCulture, $"job {result.Job.Name}: exit {result.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "none"}, {result.Elapsed.TotalMilliseconds:0} ms, {counts}{(result.Problem is null ? "" : "; " + result.Problem)}\n");
        }

        return text.ToString();
    }
}
