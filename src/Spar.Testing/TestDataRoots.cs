using System.Globalization;

namespace Spar.Testing;

/// <summary>
/// One data root per app instance (plan section 5, "Safe by default"): under the repository's disposable
/// <c>artifacts/test-host/</c> when the test runs inside a repository, otherwise under the system's temporary folder.
/// Never a per-user app folder.
/// </summary>
internal static class TestDataRoots
{
    private static readonly string Run =
        $"{DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{Environment.ProcessId}";

    private static readonly Lazy<string> Base = new(FindBase);
    private static readonly Lazy<string> ProfilesBase = new(FindProfiles);
    private static int s_next;

    /// <summary>
    /// Where window tests keep their browser profiles (<c>AGENTS.md</c>): <c>artifacts/webview2-profiles/test-host/</c> in a
    /// repository, beside the data roots otherwise. Each test process takes a root of its own under it.
    /// </summary>
    public static string Profiles => ProfilesBase.Value;

    public static string Next() => Next("app");

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
        var run = Path.Combine(Base.Value, Run);
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)));
        return string.Equals(parent, run, StringComparison.OrdinalIgnoreCase);
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
        var artifacts = Path.GetDirectoryName(testHost)!;
        return Path.GetFileName(testHost) == "test-host" && Path.GetFileName(artifacts) == "artifacts"
            ? Path.Combine(artifacts, "webview2-profiles", "test-host")
            : Path.Combine(testHost, "webview2-profiles");
    }

    private static string FindBase()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var artifacts = Path.Combine(directory.FullName, "artifacts");
            if (File.Exists(Path.Combine(directory.FullName, "global.json")) && Directory.Exists(artifacts))
            {
                return Path.Combine(artifacts, "test-host");
            }
        }

        return Path.Combine(Path.GetTempPath(), "spar-test-host");
    }
}
