using System.Globalization;
using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("linux")]

namespace Integ.Linux;

/// <summary>
/// Command line (paths relative to the current directory, which must be inside the repository copy):
///   --mode host|single|presence     host (default): window + web view; single: single-instance probe; presence: D-Bus presence checks
///   --label NAME --title TEXT       run name; window title (default "Spar integ NAME", which the X11 tool finds)
///   --frame native|none|titlebar    native title bar, gtk_window_set_decorated(FALSE), or an empty titlebar widget (CSD kept)
///   --drag press|threshold|off      when the injected script asks the host to move the window
///   --region attr|css               drag regions marked with data-spar-drag or with --spar-region: drag
///   --edges                         page-side resize edges (for --frame none)
///   --threshold PX                  drag threshold (default 4)
///   --tray --tray-id ID             export a StatusNotifierItem; --close-to-tray hides on close; --activate-toggles
///   --start-hidden                  do not show the window at start
///   --control FIFO                  read commands from a named pipe
///   --save-placement F --restore-placement F
///   --clipboard-access --allow-clipboard-permission
///   --size WxH --profile NAME
/// </summary>
internal sealed class Options
{
    public required string RepoRoot { get; init; }
    public required string Label { get; init; }
    public required string Title { get; init; }
    public required string WebDirectory { get; init; }
    public required string DataDirectory { get; init; }
    public required string Frame { get; init; }
    public required string DragMode { get; init; }
    public required string RegionMode { get; init; }
    public bool Edges { get; init; }
    public int Threshold { get; init; }
    public bool Tray { get; init; }
    public required string TrayId { get; init; }
    public bool CloseToTray { get; init; }
    public bool ActivateToggles { get; init; }
    public bool StartHidden { get; init; }
    public string? Control { get; init; }
    public string? SavePlacement { get; init; }
    public string? RestorePlacement { get; init; }
    public bool ClipboardAccess { get; init; }
    public bool AllowClipboardPermission { get; init; }
    public bool WindowCss { get; init; }
    public bool MaximizeAfterMap { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
#if SPAR_GTK4
    public bool Gtk4 => true;
#else
    public bool Gtk4 => false;
#endif
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

        bool Flag(string name) => args.Contains(name);

        Log.Mark(string.Create(CultureInfo.InvariantCulture, $"process-start pid={Environment.ProcessId} api={Api.Name} args={string.Join(' ', args)}"));
        var mode = Option("--mode") ?? "host";
        if (mode == "single")
        {
            return SingleInstance.Run(args, Option);
        }

        if (mode == "presence")
        {
            return Presence.Run();
        }

        var root = FindRepoRoot();
        var label = Option("--label") ?? "run";
        var size = (Option("--size") ?? "720x480").Split('x');
        var options = new Options
        {
            RepoRoot = root,
            Label = label,
            Title = Option("--title") ?? "Spar integ " + label,
            WebDirectory = Path.Combine(root, "spikes", "linux-integration", "web") + "/",
            DataDirectory = Path.Combine(root, "artifacts", "webview2-profiles", "linux-integration-" + (Option("--profile") ?? label)),
            Frame = Option("--frame") ?? "titlebar",
            DragMode = Option("--drag") ?? "threshold",
            RegionMode = Option("--region") ?? "attr",
            Edges = Flag("--edges"),
            Threshold = int.Parse(Option("--threshold") ?? "4", CultureInfo.InvariantCulture),
            Tray = Flag("--tray"),
            TrayId = Option("--tray-id") ?? "spar-integration",
            CloseToTray = Flag("--close-to-tray"),
            ActivateToggles = Flag("--activate-toggles"),
            StartHidden = Flag("--start-hidden"),
            Control = Option("--control"),
            SavePlacement = Option("--save-placement"),
            RestorePlacement = Option("--restore-placement"),
            ClipboardAccess = Flag("--clipboard-access"),
            AllowClipboardPermission = Flag("--allow-clipboard-permission"),
            WindowCss = !Flag("--no-window-css"),
            MaximizeAfterMap = Flag("--maximize-after-map"),
            Width = int.Parse(size[0], CultureInfo.InvariantCulture),
            Height = int.Parse(size[1], CultureInfo.InvariantCulture),
        };

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

/// <summary>The compiled API variant.</summary>
internal static class Api
{
#if SPAR_GTK4
    public const string Name = "gtk4/webkitgtk-6.0";
#else
    public const string Name = "gtk3/webkit2gtk-4.1";
#endif
}

/// <summary>Process-tree helpers over /proc.</summary>
internal static class Processes
{
    /// <summary>Counts live (non-zombie) descendants of a process.</summary>
    public static int Descendants(int root)
    {
        var parents = new Dictionary<int, int>();
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory), out var pid))
            {
                continue;
            }

            try
            {
                var stat = File.ReadAllText(Path.Combine(directory, "stat"));
                var rest = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
                if (rest[0] != "Z")
                {
                    parents[pid] = int.Parse(rest[1], CultureInfo.InvariantCulture);
                }
            }
            catch (IOException)
            {
                // The process ended while we looked.
            }
        }

        var count = 0;
        var frontier = new Queue<int>([root]);
        while (frontier.TryDequeue(out var parent))
        {
            foreach (var (pid, ppid) in parents)
            {
                if (ppid == parent)
                {
                    count++;
                    frontier.Enqueue(pid);
                }
            }
        }

        return count;
    }
}
