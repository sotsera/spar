using System.Globalization;

namespace Spar.WebViewProfiles;

/// <summary>/proc helpers. Only this spike's own descendants are ever tracked; nothing here sends a signal.</summary>
internal static class Proc
{
    public readonly record struct Child(int Pid, string Name, long Start);

    /// <summary>Live (non-zombie) descendants of <paramref name="root"/>, with their start times (clock ticks).</summary>
    public static List<Child> Descendants(int root)
    {
        var table = new Dictionary<int, (int Parent, string Name, long Start)>();
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (int.TryParse(Path.GetFileName(directory), out var pid) && Stat(pid) is { } stat && stat.State != "Z")
            {
                table[pid] = (stat.Parent, stat.Name, stat.Start);
            }
        }

        var found = new List<Child>();
        var frontier = new Queue<int>([root]);
        while (frontier.TryDequeue(out var parent))
        {
            foreach (var (pid, entry) in table)
            {
                if (entry.Parent == parent)
                {
                    found.Add(new Child(pid, entry.Name, entry.Start));
                    frontier.Enqueue(pid);
                }
            }
        }

        return found;
    }

    /// <summary>True while the same process (pid and start time) exists and is not a zombie.</summary>
    public static bool Alive(Child child) => Stat(child.Pid) is { } stat && stat.Start == child.Start && stat.State != "Z";

    public static long PssKb(int pid)
    {
        try
        {
            foreach (var line in File.ReadLines($"/proc/{pid}/smaps_rollup"))
            {
                if (line.StartsWith("Pss:", StringComparison.Ordinal))
                {
                    return long.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1], CultureInfo.InvariantCulture);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return 0;
    }

    /// <summary>Processes of this user that have a file open under <paramref name="root"/>.</summary>
    public static List<int> FileUsers(string root)
    {
        var users = new List<int>();
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory), out var pid))
            {
                continue;
            }

            try
            {
                foreach (var fd in Directory.EnumerateFileSystemEntries(Path.Combine(directory, "fd")))
                {
                    var target = new FileInfo(fd).LinkTarget;
                    if (target is not null && target.StartsWith(root, StringComparison.Ordinal))
                    {
                        users.Add(pid);
                        break;
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        return users;
    }

    private static (string Name, string State, int Parent, long Start)? Stat(int pid)
    {
        try
        {
            var stat = File.ReadAllText($"/proc/{pid}/stat");
            var open = stat.IndexOf('(', StringComparison.Ordinal);
            var close = stat.LastIndexOf(')');
            var rest = stat[(close + 2)..].Split(' ');
            return (stat[(open + 1)..close], rest[0], int.Parse(rest[1], CultureInfo.InvariantCulture), long.Parse(rest[19], CultureInfo.InvariantCulture));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or IndexOutOfRangeException)
        {
            return null;
        }
    }
}
