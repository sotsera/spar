namespace Spar.Testing;

/// <summary>
/// Whether this process runs inside a Windows Sandbox guest that Spar's runner started (<see cref="SandboxRunner"/>), and
/// in which lane. Three things must agree: the run's id in <see cref="RunVariable"/>, which the runner's guest script sets;
/// the same id in <see cref="MarkerPath"/>, a file on the guest's own disk the runner writes as SYSTEM once the sandbox
/// is ready; and, for the user-session lane, the guest's one user, <see cref="GuestUser"/>. A developer's machine has
/// none of the three, so whatever sends real input or captures the screen (<see cref="EnsureUserSession"/>) refuses there.
/// </summary>
internal static class SandboxGuest
{
    /// <summary>The run's id, set by the runner's guest script for the payload it starts.</summary>
    public const string RunVariable = "SPAR_SANDBOX_RUN";

    /// <summary>The account Windows Sandbox signs its user session in as.</summary>
    public const string GuestUser = "WDAGUtilityAccount";

    /// <summary>The runner's marker in the guest: the run's id, written as SYSTEM when the sandbox is ready.</summary>
    public const string MarkerPath = @"C:\spar\runner\run-id.txt";

    /// <summary>Whether this process was started by the runner in a guest it started, in either lane.</summary>
    public static bool IsRunnerGuest => Refusal(Environment.GetEnvironmentVariable(RunVariable), null, ReadMarker) is null;

    /// <summary>Whether this process runs in the user session of a guest the runner started (window and input tests).</summary>
    public static bool IsUserSession => Refusal(Environment.GetEnvironmentVariable(RunVariable), Environment.UserName, ReadMarker) is null;

    /// <summary>
    /// Throws unless this process runs in the user session of a guest the runner started: real input, the screen and
    /// windows placed on the input desktop belong to that guest only, never to the developer's desktop.
    /// </summary>
    public static void EnsureUserSession(string action)
    {
        if (Refusal(Environment.GetEnvironmentVariable(RunVariable), Environment.UserName, ReadMarker) is { } reason)
        {
            throw new InvalidOperationException(
                $"Spar's tests {action} only inside a Windows Sandbox guest that Spar's runner started, in its user session " +
                $"(tests/Spar.SandboxTests): {reason}. Nothing was sent.");
        }
    }

    /// <summary>
    /// Why this process is not in a runner's guest, or null when it is: <paramref name="user"/> null checks either
    /// lane, a name checks the user-session lane.
    /// </summary>
    internal static string? Refusal(string? runId, string? user, Func<string, string?> readMarker)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return $"{RunVariable} is not set";
        }

        var marker = readMarker(MarkerPath);
        if (marker is null)
        {
            return $"the runner's marker {MarkerPath} is missing";
        }

        if (!string.Equals(marker.Trim(), runId.Trim(), StringComparison.Ordinal))
        {
            return $"the runner's marker names another run";
        }

        if (user is not null && !string.Equals(user, GuestUser, StringComparison.OrdinalIgnoreCase))
        {
            return $"the process runs as {user}, not as the guest's user {GuestUser}";
        }

        return null;
    }

    private static string? ReadMarker(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
