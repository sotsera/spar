using System.Globalization;

namespace Spar.Testing;

/// <summary>
/// One data root per app instance (plan section 5, "Safe by default"): under <c>test-host/</c> of the run's artifacts
/// folder (<see cref="TestArtifacts.Root"/>: the folder the runner names, or the repository's disposable <c>artifacts/</c>),
/// otherwise under the system's temporary folder. Never a per-user app folder.
/// </summary>
internal static class TestDataRoots
{
    private static readonly string Run =
        $"{DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{Environment.ProcessId}";

    private static readonly Lazy<string> Base = new(FindBase);
    private static readonly Lazy<string> ProfilesBase = new(FindProfiles);
    private static readonly Lazy<string> HomesBase = new(FindHomes);
    private static readonly Lazy<string> LinuxBlackBoxBase = new(() => TestArtifacts.Root is { } artifacts
        ? Path.Combine(artifacts, "linux-homes", "black-box")
        : Path.Combine(Base.Value, "linux-homes-black-box"));
    private static int s_next;

    /// <summary>
    /// The black-box roots the current flow creates, when a test asks (Spar's own fixture tests): a list it set before a
    /// launch receives each root <see cref="NextBlackBox"/> makes for it, unmixed with those of tests running beside it.
    /// </summary>
    internal static readonly AsyncLocal<List<string>?> Created = new();

    /// <summary>
    /// Where window tests keep their browser profiles (<c>AGENTS.md</c>): <c>artifacts/webview2-profiles/test-host/</c> in a
    /// repository, beside the data roots otherwise. Each test process takes a root of its own under it.
    /// </summary>
    public static string Profiles => ProfilesBase.Value;

    /// <summary>
    /// Where Linux window tests keep the test process's private home and XDG folders (<c>AGENTS.md</c>; plan section 5,
    /// "Linux isolation"): <c>artifacts/linux-homes/test-host/</c> in a repository, beside the data roots otherwise. Each
    /// test process takes a root of its own under it.
    /// </summary>
    public static string LinuxHomes => HomesBase.Value;

    public static string Next() => Next("app");

    /// <summary>
    /// A new data root for a black-box launch: under this run's folder on Windows; on Linux, where the root holds the
    /// app's <c>HOME</c> and <c>XDG_*</c> folders, under <c>artifacts/linux-homes/black-box/</c> (<c>AGENTS.md</c>).
    /// </summary>
    public static string NextBlackBox()
    {
        string root;
        if (OperatingSystem.IsLinux())
        {
            root = Path.Combine(BlackBoxRun, $"black-box-{Interlocked.Increment(ref s_next):D3}");
            Directory.CreateDirectory(root);
        }
        else
        {
            root = Next("black-box");
        }

        if (Created.Value is { } created)
        {
            lock (created)
            {
                created.Add(root);
            }
        }

        return root;
    }

    /// <summary>The folder this run's black-box roots go in.</summary>
    public static string BlackBoxRun => OperatingSystem.IsLinux() ? Path.Combine(LinuxBlackBoxBase.Value, Run) : Path.Combine(Base.Value, Run);

    /// <summary>A new data root named <paramref name="kind"/>-NNN under this run's folder.</summary>
    public static string Next(string kind)
    {
        var name = $"{kind}-{Interlocked.Increment(ref s_next):D3}";
        var root = Path.Combine(Base.Value, Run, name);
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>Whether <paramref name="path"/> is a data root of this run (a folder directly under its run folder).</summary>
    public static bool IsDataRoot(string path)
    {
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)));
        return string.Equals(parent, Path.Combine(Base.Value, Run), StringComparison.OrdinalIgnoreCase)
            || string.Equals(parent, BlackBoxRun, StringComparison.OrdinalIgnoreCase);
    }

    public static void Delete(string root)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }

                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException && attempt < 5)
            {
                // A file the app just closed can stay locked for a moment on Windows.
                Thread.Sleep(100);
            }
        }
    }

    /// <summary>
    /// Deletes a root a launched app used, trying again for up to <paramref name="limit"/>: the files of a process that was
    /// just ended (a browser's memory-mapped <c>CrashpadMetrics-active.pma</c>) can stay open for a moment after its job
    /// reports no process left (seen once in 2 × 12 black-box suite runs, under load).
    /// </summary>
    public static async Task DeleteAsync(string root, TimeSpan limit)
    {
        var deadline = DateTime.UtcNow + limit;
        while (true)
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }

                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException && DateTime.UtcNow < deadline)
            {
                await Task.Delay(100, CancellationToken.None);
            }
        }
    }

    private static string FindProfiles()
    {
        var testHost = Base.Value;
        return TestArtifacts.Root is { } artifacts
            ? Path.Combine(artifacts, "webview2-profiles", "test-host")
            : Path.Combine(testHost, "webview2-profiles");
    }

    private static string FindHomes() =>
        TestArtifacts.Root is { } artifacts
            ? Path.Combine(artifacts, "linux-homes", "test-host")
            : Path.Combine(Base.Value, "linux-homes");

    private static string FindBase() =>
        TestArtifacts.Root is { } artifacts
            ? Path.Combine(artifacts, "test-host")
            : Path.Combine(Path.GetTempPath(), "spar-test-host");
}
