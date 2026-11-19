using System.Runtime.Versioning;

namespace Spar.Testing;

/// <summary>
/// The evidence of a Sandbox check: a UI Automation dump of the app's windows and a screenshot of the guest's screen,
/// under the run's results folder (<c>&lt;results&gt;/&lt;project&gt;/sandbox/</c>), which the runner brings back to the
/// host's <c>artifacts/test-results/</c>.
/// </summary>
internal static class SandboxEvidence
{
    private static readonly Lock NoteGate = new();

    /// <summary>Writes <c>&lt;name&gt;.uia.txt</c> and <c>&lt;name&gt;.png</c>; returns the dump for the test's own checks.</summary>
    [SupportedOSPlatform("windows10.0.17763")]
    public static async Task<string> SaveAsync(SparBlackBoxApp app, string project, string name)
    {
        var folder = Path.Combine(TestArtifacts.Results(project), "sandbox");
        Directory.CreateDirectory(folder);
        var dump = await app.Window.DumpAsync();
        await File.WriteAllTextAsync(Path.Combine(folder, name + ".uia.txt"), dump);
        ScreenCapture.Save(Path.Combine(folder, name + ".png"));
        return dump;
    }

    /// <summary>Appends a line to <c>&lt;results&gt;/&lt;project&gt;/sandbox/checks.txt</c>, with the time.</summary>
    public static void Note(string project, string line)
    {
        var folder = Path.Combine(TestArtifacts.Results(project), "sandbox");
        Directory.CreateDirectory(folder);
        lock (NoteGate)
        {
            File.AppendAllLines(Path.Combine(folder, "checks.txt"), [$"{DateTime.UtcNow:O} {line}"]);
        }
    }
}
