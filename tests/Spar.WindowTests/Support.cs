using Spar.Testing;

namespace Spar.WindowTests;

/// <summary>Where window tests can run: Windows until the Linux host arrives (M1c). Elsewhere each is an explicit skip.</summary>
internal static class WindowSupport
{
    public static void SkipUnlessSupported() =>
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The Window surface runs on Windows; the Linux host arrives in M1c.");
}

/// <summary>Bounded waits for state the page API does not see, such as the backend's or the log's.</summary>
internal static class Waits
{
    public static async Task UntilAsync(Func<bool> condition, TimeSpan limit, string? what = null)
    {
        var deadline = DateTime.UtcNow + limit;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                Assert.Fail($"Timed out after {limit.TotalSeconds:0.#} s waiting for {what ?? "the condition"}.");
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}

/// <summary>
/// The folders the tests write to: this project's results (<c>artifacts/test-results/Spar.WindowTests/</c> in the
/// repository, or the folder the runner names, as Windows Sandbox's does in its guest) and the run's artifacts folder.
/// </summary>
internal static class Results
{
    private static readonly Lock AppendGate = new();

    /// <summary>The run's artifacts folder (<c>artifacts/</c> in the repository), where data roots go.</summary>
    public static string Artifacts =>
        TestArtifacts.Root ?? throw new InvalidOperationException("These tests run from a build inside the Spar repository, or with SPAR_TEST_ARTIFACTS set.");

    public static string Folder(string name)
    {
        var folder = Path.Combine(TestArtifacts.Results("Spar.WindowTests"), name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>Appends a line of evidence to this project's results folder, with the time.</summary>
    public static void Append(string file, string line)
    {
        lock (AppendGate)
        {
            File.AppendAllLines(Path.Combine(Folder("."), file), [$"{DateTime.UtcNow:O} {line}"]);
        }
    }
}
