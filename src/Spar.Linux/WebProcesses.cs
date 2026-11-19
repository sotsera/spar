using System.Globalization;

namespace Spar.Linux;

/// <summary>
/// The app's WebKit processes, which WebKit gives no ids for (pitfall "Reading WebKitGTK process memory"): the
/// descendants of this process, found through <c>/proc/&lt;pid&gt;/task/&lt;tid&gt;/children</c>, whose name starts with
/// <c>WebKit</c> (<c>WebKitWebProces</c>, <c>WebKitNetworkPr</c>, <c>WebKitGPUProces</c>; the kernel keeps 15
/// characters). Their sandbox's <c>bwrap</c> and <c>xdg-dbus-proxy</c> parents end with them, and the app's own child
/// processes are not WebKit's to wait for. Reads only <c>/proc</c>, so it is called off the main loop.
/// </summary>
internal static class WebProcesses
{
    /// <summary>The WebKit processes below <paramref name="root"/>, with their names.</summary>
    public static List<(int Id, string Name)> Below(int root)
    {
        var found = new List<(int, string)>();
        var pending = new Stack<int>();
        pending.Push(root);
        while (pending.TryPop(out var parent))
        {
            foreach (var child in Children(parent))
            {
                pending.Push(child);
                if (Name(child) is { } name && name.StartsWith("WebKit", StringComparison.Ordinal))
                {
                    found.Add((child, name));
                }
            }
        }

        return found;
    }

    /// <summary>The children of every thread of <paramref name="process"/>; none when it has gone meanwhile.</summary>
    private static List<int> Children(int process)
    {
        var children = new List<int>();
        try
        {
            foreach (var task in Directory.EnumerateDirectories($"/proc/{process}/task"))
            {
                foreach (var id in ReadOrEmpty(Path.Combine(task, "children")).Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var child))
                    {
                        children.Add(child);
                    }
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The process ended while it was read.
        }

        return children;
    }

    private static string? Name(int process) => ReadOrEmpty($"/proc/{process}/comm").Trim() is { Length: > 0 } name ? name : null;

    private static string ReadOrEmpty(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }
}
