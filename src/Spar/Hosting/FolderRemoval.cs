namespace Spar.Hosting;

/// <summary>
/// Removes the folders an app chose with <see cref="FolderOptions.RemoveOnExit"/> (decision "M3b's decide-first
/// questions", item 6): at exit, once the browser's processes have exited, and at start, what a crash or a killed session
/// left. The host calls it only while it holds the app's single-instance lock, so another instance's folders are never
/// touched. A file still held briefly is retried; what cannot be removed is reported and left for the next start.
/// </summary>
internal static class FolderRemoval
{
    /// <summary>
    /// Removes the chosen folders, the logs only when <paramref name="includeLogs"/> (at start, this run's log is already
    /// open there); <paramref name="warn"/> hears each one that stays.
    /// </summary>
    public static void Remove(SparFolders folders, bool includeLogs, Action<string> warn)
    {
        foreach (var folder in folders.RemovedOnExit)
        {
            if (folder == SparFolder.Logs && !includeLogs)
            {
                continue;
            }

            var path = folder switch
            {
                SparFolder.Profile => folders.Profile,
                SparFolder.Cache => folders.Cache,
                SparFolder.Data => folders.Data,
                _ => folders.Logs,
            };
            if (RemoveOne(path) is { } problem)
            {
                warn($"Folder {path} could not be removed ({problem}); the next start tries again.");
            }
        }
    }

    /// <summary>Whether the app wants its logs removed at exit, which the host does after closing its log file.</summary>
    public static bool RemovesLogs(SparFolders folders) => folders.RemovedOnExit.Contains(SparFolder.Logs);

    private static string? RemoveOne(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }

                return null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt == 5)
                {
                    return exception.Message;
                }

                // A browser process can hold a file for a moment after it ended.
                Thread.Sleep(100 * attempt);
            }
        }
    }
}
