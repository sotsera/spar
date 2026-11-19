using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;

namespace Spar.Testing;

/// <summary>
/// Spar's Windows Sandbox runner (plan section 5, issue J; decisions "Window-test profiles, trusted input, the page's
/// pickers and the Sandbox runner" and "Step 8c's shape: the Windows Sandbox runner"), internal to Spar.Testing until
/// M3's app-facing layer. A run uses one sandbox for all its jobs, in sequence, each staged under a new folder; a job
/// that needs a clean machine gets a sandbox of its own, started after the shared one stopped. One sandbox runs at a
/// time per user, so a run refuses while any other sandbox runs (<see cref="SandboxBusyException"/>), and it stops only
/// the sandboxes it started. Jobs in <see cref="SandboxLane.UserSession"/> run only when the caller passed the allow
/// gate (<see cref="SandboxDesktopGate"/>); the runner never checks it itself, so a caller cannot forget to skip.
/// </summary>
[SupportedOSPlatform("windows10.0.17763")]
internal sealed class SandboxRunner
{
    private readonly string _folder;
    private readonly SandboxFeatures _features;
    private readonly TimeSpan _readyLimit;

    /// <param name="folder">The run's folder on the host: each sandbox gets <c>sNN/in</c> (staging) and <c>sNN/out</c> (results).</param>
    /// <param name="features">What the guest's configuration turns on (nothing by default).</param>
    /// <param name="readyLimit">How long a started sandbox may take until it runs a command.</param>
    public SandboxRunner(string folder, SandboxFeatures? features = null, TimeSpan? readyLimit = null)
    {
        _folder = Path.GetFullPath(folder);
        _features = features ?? new SandboxFeatures();
        _readyLimit = readyLimit ?? TimeSpan.FromMinutes(3);
    }

    /// <summary>The run's sessions, each with its timeline, in the order they ran.</summary>
    public List<SandboxSessionSummary> Sessions { get; } = [];

    /// <summary>
    /// Whether Windows Sandbox's command line answers on this machine (Windows 11 24H2 or later with the feature on); the
    /// reason when it does not. Never starts a sandbox.
    /// </summary>
    public static async Task<(bool Available, string Reason)> AvailableAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return (false, "Windows Sandbox's command line (wsb) needs Windows 11.");
        }

        var version = await Wsb.RunAsync(TimeSpan.FromSeconds(30), cancellationToken, "--version");
        return version.ExitCode == 0
            ? (true, version.Output.Trim())
            : (false, $"Windows Sandbox is not available here: 'wsb --version' failed ({version.OneLine}). It needs Windows 11 " +
                "Pro, Enterprise or Education with the Windows Sandbox feature turned on, and is not on hosted CI runners.");
    }

    /// <summary>
    /// Runs <paramref name="jobs"/>: the shared ones in one sandbox, in order, then each fresh one in a sandbox of its own.
    /// Returns a result per job in the order given; a job that could not run has its problem and no tests.
    /// </summary>
    public async Task<IReadOnlyList<SandboxJobResult>> RunAsync(IReadOnlyList<SandboxJob> jobs, CancellationToken cancellationToken)
    {
        var running = await Wsb.ListAsync(cancellationToken)
            ?? throw new InvalidOperationException("Windows Sandbox's command line did not list the running sandboxes ('wsb list').");
        if (running.Count > 0)
        {
            throw new SandboxBusyException(running.Count);
        }

        var results = new Dictionary<SandboxJob, SandboxJobResult>();
        var shared = jobs.Where(job => !job.FreshSandbox).ToList();
        if (shared.Count > 0)
        {
            await RunInOneAsync(shared, results, cancellationToken);
        }

        foreach (var fresh in jobs.Where(job => job.FreshSandbox))
        {
            await RunInOneAsync([fresh], results, cancellationToken);
        }

        return [.. jobs.Select(job => results[job])];
    }

    private async Task RunInOneAsync(List<SandboxJob> jobs, Dictionary<SandboxJob, SandboxJobResult> results, CancellationToken cancellationToken)
    {
        var name = string.Create(CultureInfo.InvariantCulture, $"s{Sessions.Count + 1:D2}");
        var folder = Path.Combine(_folder, name);
        var clock = Stopwatch.StartNew();
        SandboxSession session;
        try
        {
            session = await SandboxSession.StartAsync(folder, _features, _readyLimit, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or TimeoutException)
        {
            foreach (var job in jobs)
            {
                results[job] = NotRun(job, folder, $"The sandbox did not start: {exception.Message}");
            }

            Sessions.Add(new SandboxSessionSummary(name, folder, clock.Elapsed, [], 0));
            return;
        }

        await using (session)
        {
            foreach (var job in jobs)
            {
                try
                {
                    results[job] = await session.RunAsync(job, cancellationToken);
                }
                catch (Exception exception) when (exception is InvalidOperationException or TimeoutException or IOException)
                {
                    results[job] = NotRun(job, folder, $"The job did not run: {exception.Message}");
                }
            }
        }

        Sessions.Add(new SandboxSessionSummary(name, folder, clock.Elapsed, session.Timeline.Steps, session.Minimizes));
        TryDelete(Path.Combine(folder, "in"));
    }

    private static SandboxJobResult NotRun(SandboxJob job, string folder, string problem) =>
        new(job, "", folder, null, false, TimeSpan.Zero, [], problem);

    /// <summary>The staging copies are disposable (under artifacts/); a copy the guest still holds stays until the next run.</summary>
    private static void TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Left for the next run's cleanup of artifacts/; nothing depends on it.
        }
    }
}

/// <summary>One sandbox of a run: its folder, how long it lived, its steps, and how many times its client window was minimized.</summary>
internal sealed record SandboxSessionSummary(string Name, string Folder, TimeSpan Elapsed, IReadOnlyList<SandboxStep> Steps, int Minimizes);

/// <summary>Another sandbox runs: Windows runs one per user at a time, and the runner never stops one it did not start.</summary>
internal sealed class SandboxBusyException(int running) : InvalidOperationException(
    $"{running} Windows Sandbox {(running == 1 ? "is" : "are")} already running. Windows runs one sandbox at a time, and the " +
    "runner stops only sandboxes it started: close it (or wait for it) and run again.");
