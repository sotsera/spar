using System.Globalization;

namespace Theme.Linux;

/// <summary>
/// Command line (paths relative to the current directory, which must be inside the repository copy):
///   --label NAME          output artifacts/theme-startup/NAME (timeline.json; xcap writes frames.jsonl and PNGs there)
///   --web DIR             built frontend (default spikes/theme-startup/frontend/dist)
///   --page FILE --query Q page to open
///   --profile NAME        WebKit data artifacts/webview2-profiles/theme-startup-linux-NAME; --fresh deletes it first
///   --appdata NAME        the app's data folder artifacts/theme-startup/appdata/NAME (saved colour scheme)
///   --theme light|dark|system, --system light|dark (simulated system theme; default: portal, then GTK fallback)
///   --layers a,b          css, prefer-dark, gtk-theme, wv-bg, transparent
///   --show now|ready, --show-timeout MS, --headerbar, --bundle-delay/--html-delay/--css-delay MS,
///   --after-ready MS, --live, --reduced-motion (gtk-enable-animations=false), --no-hash, --no-csp, --csp-meta (policy as a meta element instead of the header)
/// </summary>
internal sealed class Options
{
    public required string[] Raw { get; init; }
    public required string RepoRoot { get; init; }
    public required string Label { get; init; }
    public required string WebDirectory { get; init; }
    public required string Page { get; init; }
    public string? Query { get; init; }
    public required string DataDirectory { get; init; }
    public required string AppDataDirectory { get; init; }
    public string? Theme { get; init; }
    public string? SystemTheme { get; init; }
    public required HashSet<string> Layers { get; init; }
    public required string Show { get; init; }
    public int ShowTimeoutMs { get; init; }
    public bool HeaderBar { get; init; }
    public int BundleDelayMs { get; init; }
    public int HtmlDelayMs { get; init; }
    public int CssDelayMs { get; init; }
    public int AfterReadyMs { get; init; }
    public bool Live { get; init; }
    public bool ReducedMotion { get; init; }
    public bool NoHash { get; init; }
    public bool NoCsp { get; init; }
    public bool CspMeta { get; init; }
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

        int Number(string name, int fallback) => Option(name) is { } text ? int.Parse(text, CultureInfo.InvariantCulture) : fallback;

        var root = FindRepoRoot();
        var label = Option("--label") ?? "linux-run";
        var data = Path.Combine(root, "artifacts", "webview2-profiles", "theme-startup-linux-" + (Option("--profile") ?? label));
        if (args.Contains("--fresh") && Directory.Exists(data))
        {
            Directory.Delete(data, recursive: true);
        }

        var options = new Options
        {
            Raw = args,
            RepoRoot = root,
            Label = label,
            WebDirectory = Path.GetFullPath(Option("--web") ?? Path.Combine(root, "spikes", "theme-startup", "frontend", "dist")) + "/",
            Page = Option("--page") ?? "baseline.html",
            Query = Option("--query"),
            DataDirectory = data,
            AppDataDirectory = Path.Combine(root, "artifacts", "theme-startup", "appdata", Option("--appdata") ?? label),
            Theme = Option("--theme"),
            SystemTheme = Option("--system"),
            Layers = [.. (Option("--layers") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)],
            Show = Option("--show") ?? "now",
            ShowTimeoutMs = Number("--show-timeout", 3000),
            HeaderBar = args.Contains("--headerbar"),
            BundleDelayMs = Number("--bundle-delay", 0),
            HtmlDelayMs = Number("--html-delay", 0),
            CssDelayMs = Number("--css-delay", 0),
            AfterReadyMs = Number("--after-ready", 800),
            Live = args.Contains("--live"),
            ReducedMotion = args.Contains("--reduced-motion"),
            NoHash = args.Contains("--no-hash"),
            NoCsp = args.Contains("--no-csp"),
            CspMeta = args.Contains("--csp-meta"),
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
