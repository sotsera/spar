using Spar.Testing;

namespace Spar.WindowTests;

/// <summary>
/// Checks that run inside Windows Sandbox only, started by Spar's runner (<c>dotnet test --project tests/Spar.SandboxTests</c>),
/// which publishes this project with NativeAOT and runs its tests by their <c>Sandbox</c> trait: <c>System</c> (as SYSTEM,
/// no window), <c>UserSession</c> (windows and real input in the guest's user session), <c>SignOut</c> (a job of its own
/// that signs the guest out) and <c>Deadline</c> (the runner's own deadline check). They are explicit, so an ordinary run
/// leaves them out, and each one skips anywhere but the guest it belongs to.
/// </summary>
internal static class SandboxLane
{
    public const string Trait = "Sandbox";

    private const string Where = "It runs only inside Windows Sandbox, started by Spar's runner: dotnet test --project tests/Spar.SandboxTests.";

    public static void SkipUnlessGuest() => Assert.SkipUnless(SandboxGuest.IsRunnerGuest, Where);

    public static void SkipUnlessUserSession() => Assert.SkipUnless(SandboxGuest.IsUserSession, Where + " Its user-session lane needs the run's allow.");

    /// <summary>Writes the evidence of a check (UI Automation dump, screenshot) under this project's results.</summary>
    public static Task<string> EvidenceAsync(SparBlackBoxApp app, string name)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            throw new PlatformNotSupportedException("Windows Sandbox checks run on Windows.");
        }

        return SandboxEvidence.SaveAsync(app, "Spar.WindowTests", name);
    }

    public static void Note(string line) => SandboxEvidence.Note("Spar.WindowTests", line);
}
