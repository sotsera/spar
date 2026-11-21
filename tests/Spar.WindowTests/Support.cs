using Spar.Hosting;
using Spar.Testing;
using WindowHost;

namespace Spar.WindowTests;

/// <summary>
/// Where window tests can run (decision "M1c step 7's shape: Linux window and black-box tests"): Windows and Linux. The
/// first window test of the process runs the host's start-up check, by opening WindowHost's window once; when it finds
/// something missing (no WebView2 Runtime; no Xvfb, no WebKitGTK at the supported minimum, or user namespaces blocked
/// for the web process's sandbox), every window test skips with the check's own message, and fails when <c>CI=true</c>,
/// so CI cannot pass by skipping.
/// </summary>
internal static class WindowSupport
{
    private static readonly Lazy<Task<string?>> Problem = new(FindProblemAsync);

    public static async Task SkipUnlessSupportedAsync()
    {
        if (await Problem.Value is not { } problem)
        {
            return;
        }

        if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Fail(problem + " (CI=true: a window test that cannot run fails.)");
        }

        Assert.Skip(problem);
    }

    /// <summary>A test of a Windows mechanism with no Linux counterpart yet: an explicit skip elsewhere, with why.</summary>
    public static void SkipUnlessWindows(string why) => Assert.SkipUnless(OperatingSystem.IsWindows(), why);

    /// <summary>
    /// The black-box fixture's tests, on Windows and Linux: where the fixture lacks something (on Linux Xvfb, D-Bus or
    /// at-spi2-core) they skip with its message, and fail when <c>CI=true</c>.
    /// </summary>
    public static void SkipUnlessBlackBox()
    {
        if (BlackBoxPlatform.Problem is not { } problem)
        {
            return;
        }

        if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Fail(problem + " (CI=true: a black-box test that cannot run fails.)");
        }

        Assert.Skip(problem);
    }

    private static async Task<string?> FindProblemAsync()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            return "The Window surface runs on Windows and Linux.";
        }

        try
        {
            // The process's first window: the host's check, its session (Xvfb, D-Bus) or shared browser, and the window.
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            await using (var app = await SparTestApp.StartAsync<Program>(options => options.Surface = SparTestSurface.Window))
            {
                Results.Append("first-window.txt", $"first window of the process open in {System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:0} ms");
            }

            return null;
        }
        catch (SparTestWindowUnavailableException unavailable)
        {
            return unavailable.Message;
        }
    }
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

    /// <summary>
    /// Appends a line of evidence to this project's results folder, with the time. The file is shared for writing:
    /// another test process may append to it at the same time (the install-path test runs a copy of these tests).
    /// </summary>
    public static void Append(string file, string line)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes($"{DateTime.UtcNow:O} {line}\n");
        lock (AppendGate)
        {
            using var stream = new FileStream(Path.Combine(Folder("."), file), FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            stream.Write(bytes);
        }
    }
}
