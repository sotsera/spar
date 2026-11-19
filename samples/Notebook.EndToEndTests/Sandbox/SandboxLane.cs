using System.Runtime.Versioning;
using Spar.Testing;

namespace Notebook.EndToEndTests;

/// <summary>
/// The Notebook's checks that run inside Windows Sandbox only, started by Spar's runner (<c>dotnet test --project
/// tests/Spar.SandboxTests</c>), on its Production executable: by their <c>Sandbox</c> trait, <c>System</c> (as SYSTEM,
/// no window), <c>UserSession</c> (real input in the guest's user session) and <c>CleanMachine</c> (a fresh, offline
/// sandbox of its own). Real input is Spar's internal API until M3, so this project sees Spar.Testing's internals. They
/// are explicit, so an ordinary run leaves them out, and each one skips anywhere but the guest it belongs to.
/// </summary>
internal static class SandboxLane
{
    public const string Trait = "Sandbox";

    private const string Where = "It runs only inside Windows Sandbox, started by Spar's runner: dotnet test --project tests/Spar.SandboxTests.";

    public static void SkipUnlessGuest() => Assert.SkipUnless(SandboxGuest.IsRunnerGuest, Where);

    public static void SkipUnlessUserSession() => Assert.SkipUnless(SandboxGuest.IsUserSession, Where + " Its user-session lane needs the run's allow.");

    /// <summary>Writes the evidence of a check (UI Automation dump, screenshot) under this project's results; returns the dump.</summary>
    [SupportedOSPlatform("windows10.0.17763")]
    public static Task<string> EvidenceAsync(SparBlackBoxApp app, string name) => SandboxEvidence.SaveAsync(app, "Notebook.EndToEndTests", name);

    public static void Note(string line) => SandboxEvidence.Note("Notebook.EndToEndTests", line);
}
