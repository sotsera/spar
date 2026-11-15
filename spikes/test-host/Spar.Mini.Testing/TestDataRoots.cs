using System.Globalization;

namespace Spar.Testing;

/// <summary>
/// One data root per app instance, under the repository's disposable <c>artifacts/</c>: never a per-user folder.
/// Web engine data goes where this repository keeps it: WebView2 profiles under <c>artifacts/webview2-profiles/</c>,
/// WebKitGTK data under <c>artifacts/linux-homes/</c>.
/// </summary>
internal static class TestDataRoots
{
    private static readonly string Run = $"{DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{Environment.ProcessId}";
    private static readonly Lazy<string> Artifacts = new(FindArtifacts);
    private static int s_next;

    public static SparFolders Next()
    {
        var name = $"app-{Interlocked.Increment(ref s_next):D3}";
        var root = Path.Combine(Artifacts.Value, "test-host", Run, name);
        var webView = OperatingSystem.IsWindows()
            ? Path.Combine(Artifacts.Value, "webview2-profiles", "test-host", Run, name)
            : Path.Combine(Artifacts.Value, "linux-homes", $"test-host-{Run}", name, "webkit");
        Directory.CreateDirectory(root);
        return SparFolders.ForTest(root, webView);
    }

    public static void Delete(SparFolders folders)
    {
        foreach (var folder in new[] { folders.DataRoot, folders.WebView })
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (Directory.Exists(folder))
                    {
                        Directory.Delete(folder, recursive: true);
                    }

                    break;
                }
                catch (IOException) when (attempt < 4)
                {
                    Thread.Sleep(100);
                }
                catch (UnauthorizedAccessException) when (attempt < 4)
                {
                    Thread.Sleep(100);
                }
            }
        }
    }

    /// <summary>The repository root holds global.json and artifacts/; tests run from somewhere below it.</summary>
    private static string FindArtifacts()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")) && Directory.Exists(Path.Combine(directory.FullName, "artifacts")))
            {
                return Path.Combine(directory.FullName, "artifacts");
            }
        }

        throw new InvalidOperationException("No repository root (global.json beside artifacts/) above " + AppContext.BaseDirectory);
    }
}
