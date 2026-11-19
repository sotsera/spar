namespace Spar.Testing;

/// <summary>Who runs a job in the guest.</summary>
internal enum SandboxLane
{
    /// <summary>
    /// As SYSTEM, with no user session: no <c>wsb connect</c>, so no client window on the host. For tests without
    /// windows (a WebView2 controller cannot be created as SYSTEM).
    /// </summary>
    System,

    /// <summary>
    /// In the guest's signed-in user session (<c>wsb connect</c> first): windows, real input, focus. The Sandbox client
    /// window takes the host's foreground a few times per connect, so this lane runs only when the run allows it
    /// (<see cref="SandboxDesktopGate"/>).
    /// </summary>
    UserSession,
}

/// <summary>
/// One payload to run in the guest: a published executable with its folder (staged without PDBs under a new folder name),
/// its arguments, its lane and its deadline. <see cref="FreshSandbox"/> runs it in a sandbox of its own, started for it
/// and stopped after it: for checks that need a clean machine or that end the guest's session.
/// </summary>
internal sealed record SandboxJob(string Name, string PayloadFolder, string Executable, SandboxLane Lane, TimeSpan Deadline)
{
    /// <summary>The executable's arguments, each passed as one argument.</summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>
    /// The payload is an xUnit v3 test executable: the runner adds its JSON reporter, written to the output folder as each
    /// test finishes, and the TRX and xUnit XML reports written at its end.
    /// </summary>
    public bool IsTestRun { get; init; } = true;

    /// <summary>A sandbox of its own, started for this job only, never shared with another job.</summary>
    public bool FreshSandbox { get; init; }

    /// <summary>
    /// How long the runner waits after the payload's end before it collects the logs from the guest's disk: for a job
    /// that ends the guest's session, whose apps go on to the end of it after the test process is gone.
    /// </summary>
    public TimeSpan CollectAfter { get; init; }

    /// <summary>Variables added to the payload's environment in the guest.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
}

/// <summary>What the guest's configuration turns on; everything is off unless a run asks for it.</summary>
internal sealed record SandboxFeatures
{
    public bool Networking { get; init; }

    public bool Clipboard { get; init; }

    public bool Printers { get; init; }

    public bool Camera { get; init; }

    public bool Microphone { get; init; }
}

/// <summary>How one job ended: the guest's exit code (null when none came back), the deadline, the files and the tests.</summary>
internal sealed record SandboxJobResult(
    SandboxJob Job,
    string Stage,
    string OutputFolder,
    int? ExitCode,
    bool TimedOut,
    TimeSpan Elapsed,
    IReadOnlyList<GuestTestResult> Tests,
    string? Problem)
{
    /// <summary>The payload's console output (its JSON reporter's lines for a test run), as it came back.</summary>
    public string ConsolePath => Path.Combine(OutputFolder, "console.log");

    /// <summary>The results the payload's tests wrote (<see cref="TestArtifacts.ResultsVariable"/> in the guest).</summary>
    public string TestResultsFolder => Path.Combine(OutputFolder, "test-results");
}

/// <summary>
/// The allow switch of the user-session lane (decision "Step 8c's shape: the Windows Sandbox runner"): it runs only when
/// <see cref="Variable"/> is <c>allow</c>, because the Sandbox client window takes the host's foreground. Agents ask the
/// user before setting it.
/// </summary>
internal static class SandboxDesktopGate
{
    public const string Variable = "SPAR_SANDBOX_DESKTOP";

    public static bool IsAllowed => IsAllowedValue(System.Environment.GetEnvironmentVariable(Variable));

    /// <summary>Why the lane does not run, for its explicit skips.</summary>
    public static string SkipReason =>
        $"The Windows Sandbox user-session lane opens the Sandbox client window, which takes this desktop's foreground a few " +
        $"times per run: it runs only with {Variable}=allow, set by the person at this computer.";

    internal static bool IsAllowedValue(string? value) => string.Equals(value?.Trim(), "allow", StringComparison.OrdinalIgnoreCase);
}
