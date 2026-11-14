using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("linux")]

namespace Mem.Linux;

/// <summary>
/// Command line (the current directory must be inside the repository copy):
///   --mode gtk|webkit             gtk: a window with a label and no WebKit (the floor); webkit (default): a web view
///   --page empty|small|heavy      the first page (webkit mode)
///   --hw default|always|never     WebKit's hardware-acceleration policy (default: leave WebKit's own)
///   --sandbox                     turn the web-process sandbox on (4.1; 6.0 is always sandboxed)
///   --drag off|move|resize        host-started window-manager move or bottom-right resize after a 4 px drag in the view
///   --reset-after-begin           GTK4: reset the view's event controllers right after starting the move or resize
///   --fix none|unparent|dispose|terminate|insensitive|reset|hide-first|try-close   the shutdown order (see Host.Shutdown)
///   --control FIFO                read commands from a named pipe: load PAGE, close, quit, gc, facts
///   --label NAME --size WxH --wait-children MS
/// </summary>
internal sealed class Options
{
    public required string Mode { get; init; }
    public required string Page { get; init; }
    public required string Hw { get; init; }
    public bool Sandbox { get; init; }
    public bool ResetAfterBegin { get; init; }
    public required string Drag { get; init; }
    public required string Fix { get; init; }
    public string? Control { get; init; }
    public required string Label { get; init; }
    public required string Title { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int WaitChildren { get; init; }
    public required string WebDirectory { get; init; }
    public required string DataDirectory { get; init; }
}

internal static class Program
{
    private static int Main(string[] args)
    {
        string? Option(string name)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        var root = FindRepoRoot();
        var label = Option("--label") ?? "run";
        var size = (Option("--size") ?? "480x360").Split('x');
        var options = new Options
        {
            Mode = Option("--mode") ?? "webkit",
            Page = Option("--page") ?? "small",
            Hw = Option("--hw") ?? "default",
            Sandbox = args.Contains("--sandbox"),
            ResetAfterBegin = args.Contains("--reset-after-begin"),
            Drag = Option("--drag") ?? "off",
            Fix = Option("--fix") ?? "none",
            Control = Option("--control"),
            Label = label,
            Title = "Spar mem " + label,
            Width = int.Parse(size[0], CultureInfo.InvariantCulture),
            Height = int.Parse(size[1], CultureInfo.InvariantCulture),
            WaitChildren = int.Parse(Option("--wait-children") ?? "10000", CultureInfo.InvariantCulture),
            WebDirectory = Path.Combine(root, "spikes", "linux-memory", "web") + "/",
            DataDirectory = Path.Combine(root, "artifacts", "webview2-profiles", "linux-memory-" + label),
        };

        Log.Mark($"process-start pid={Environment.ProcessId} api={Api.Name} args={string.Join(' ', args)}");
        return new Host(options).Run();
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Run from inside the repository copy (global.json not found).");
    }
}

internal static class Api
{
#if SPAR_GTK4
    public const string Name = "gtk4/webkitgtk-6.0";
#else
    public const string Name = "gtk3/webkit2gtk-4.1";
#endif
}

/// <summary>Marks on standard output: "M &lt;CLOCK_MONOTONIC ms&gt; &lt;ms since start&gt; text".</summary>
internal static class Log
{
    private static readonly long s_start = Stopwatch.GetTimestamp();
    private static readonly Lock s_gate = new();

    public static double Monotonic => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;

    public static void Mark(string text)
    {
        var line = string.Create(CultureInfo.InvariantCulture, $"M {Monotonic:F3} {Stopwatch.GetElapsedTime(s_start).TotalMilliseconds,9:F1} {text}");
        lock (s_gate)
        {
            Console.Out.WriteLine(line);
            Console.Out.Flush();
        }
    }
}

/// <summary>The host's live descendants, from /proc (zombies excluded).</summary>
internal static class Descendants
{
    public static List<string> List(int root)
    {
        var parents = new Dictionary<int, (int Parent, string Name)>();
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory), out var pid))
            {
                continue;
            }

            try
            {
                var stat = File.ReadAllText(Path.Combine(directory, "stat"));
                var open = stat.IndexOf('(', StringComparison.Ordinal);
                var close = stat.LastIndexOf(')');
                var rest = stat[(close + 2)..].Split(' ');
                if (rest[0] != "Z")
                {
                    parents[pid] = (int.Parse(rest[1], CultureInfo.InvariantCulture), stat[(open + 1)..close]);
                }
            }
            catch (IOException)
            {
                // The process ended while it was read.
            }
        }

        var found = new List<string>();
        var frontier = new Queue<int>([root]);
        while (frontier.TryDequeue(out var parent))
        {
            foreach (var (pid, entry) in parents)
            {
                if (entry.Parent == parent)
                {
                    found.Add(string.Create(CultureInfo.InvariantCulture, $"{entry.Name}:{pid}"));
                    frontier.Enqueue(pid);
                }
            }
        }

        return found;
    }
}
