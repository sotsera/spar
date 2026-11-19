using System.Diagnostics;
using System.Runtime.Versioning;
using Spar.Testing;

namespace Spar.WindowTests;

/// <summary>
/// A real sign-out of the guest with WindowHost's Production executable open and its page's close guard keeping the
/// window for 5 s, ignoring its signal (step 8c; decision "Window tests: closing, session-end budget, test theme and
/// any-character keys": the session end within 4 s). The sign-out ends this test process too, so the test only sets
/// the scene, writes where the app's log is, and signs out; the runner collects the log from the guest afterwards and
/// <c>Spar.SandboxTests</c> checks it on the host. It runs in a sandbox of its own, as the last thing that sandbox does.
/// </summary>
[Trait(SandboxLane.Trait, "SignOut")]
[SupportedOSPlatform("windows10.0.17763")]
public sealed class SessionEndTests
{
    /// <summary>The note the host reads: the line starts with this, then the app's data root.</summary>
    public const string RootNote = "sign-out data root: ";

    [Fact(Explicit = true)]
    public async Task Signing_out_ends_the_app_within_the_session_end_budget()
    {
        SandboxLane.SkipUnlessUserSession();
        var token = TestContext.Current.CancellationToken;

        // Not disposed, and outside a kill-on-close job: the sign-out ends this process about 130 ms after it is asked,
        // which with the fixture's usual job ended the app before Windows asked it to end its session (run of
        // 2026-11-19). The sandbox goes with the run.
        var app = await SparBlackBox.LaunchAsync(Projects.WindowHost, o =>
        {
            o.KeepData = true;
            o.OutlivesTest = true;
        }, token);
        await FocusAndDialogTests.PickGuardAsync(app, "Keep after 5 seconds, ignoring the signal");
        await SandboxLane.EvidenceAsync(app, "sign-out-before");
        SandboxLane.Note(RootNote + app.DataRoot);

        using var signOut = Process.Start(new ProcessStartInfo("shutdown.exe", "/l") { UseShellExecute = false, CreateNoWindow = true })!;
        SandboxLane.Note($"sign-out asked at {DateTime.UtcNow:O}");

        // The session's end takes this process with it; still running a minute later means the sign-out did nothing.
        await Task.Delay(TimeSpan.FromMinutes(1), token);
        SandboxLane.Note("still running a minute after the sign-out");
        Assert.Fail("The sign-out did not end the guest's session within a minute.");
    }
}
