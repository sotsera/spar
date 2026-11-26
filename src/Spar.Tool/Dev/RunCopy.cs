namespace Spar.Tool.Dev;

/// <summary>
/// The copy of the build's output the app runs from (decision "M2b's decide-first questions", item 2), so the build in
/// <c>bin/</c> never meets a file the running app holds (Windows locks a running executable and its loaded DLLs) and the
/// IDE can build meanwhile. Two folders under <c>obj/spar-dev/</c> take turns: the next run gets the one the running app
/// is not using, brought up to date file by file (a file whose size or time differs is copied; one the output no longer
/// has is deleted).
/// </summary>
internal static class RunCopy
{
    private static readonly string[] Slots = ["run-a", "run-b"];

    /// <summary>The folder for the next run: the slot <paramref name="running"/> is not in.</summary>
    public static string NextSlot(string projectDirectory, string? running)
    {
        var folder = Path.Combine(projectDirectory, "obj", "spar-dev");
        var first = Path.Combine(folder, Slots[0]);
        var second = Path.Combine(folder, Slots[1]);
        return running is not null && IsInside(running, first) ? second : first;
    }

    /// <summary>Brings <paramref name="target"/> up to date with <paramref name="source"/>; returns how many files it copied.</summary>
    public static int Sync(string source, string target)
    {
        Directory.CreateDirectory(target);
        var copied = 0;
        var wanted = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            wanted.Add(relative);
            var destination = Path.Combine(target, relative);
            if (Same(file, destination))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
            File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(file));
            copied++;
        }

        foreach (var file in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories))
        {
            if (!wanted.Contains(Path.GetRelativePath(target, file)))
            {
                File.Delete(file);
            }
        }

        return copied;
    }

    private static bool Same(string source, string destination)
    {
        var from = new FileInfo(source);
        var to = new FileInfo(destination);
        return to.Exists && to.Length == from.Length && to.LastWriteTimeUtc == from.LastWriteTimeUtc;
    }

    private static bool IsInside(string path, string folder)
    {
        var relative = Path.GetRelativePath(folder, path);
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }
}
