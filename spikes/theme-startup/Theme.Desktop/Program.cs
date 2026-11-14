using System.Globalization;

namespace Theme.Desktop;

/// <summary>
/// Command line (all paths relative to the current directory):
///   --label NAME           output folder artifacts/theme-startup/NAME (timeline.json, PNG frames)
///   --web DIR              built frontend (default spikes/theme-startup/frontend/dist)
///   --page FILE            page to open (default baseline.html); --query "a=1&amp;b=2" is appended
///   --profile NAME         WebView2 profile artifacts/webview2-profiles/theme-startup-NAME; --fresh deletes it first
///   --appdata NAME         the app's data folder artifacts/theme-startup/appdata/NAME (saved colour scheme)
///   --theme light|dark|system   the app's choice for this run (default: saved choice, else system)
///   --system light|dark    simulated system theme (default: read AppsUseLightTheme)
///   --layers a,b           brush, erase, titlebar, wv-put, wv-env, wv-options, scheme, transparent
///   --show now|ready|cloak  show at once, or at ui-ready (hidden / cloaked until then); --show-timeout MS
///   --bundle-delay / --html-delay / --css-delay MS   delay those responses; --navigate-delay MS after the controller; --capture MS; --after-ready MS; --live; --reduced-motion
///   --no-hash / --no-csp   CSP without the inline-style hashes (negative control) / no CSP header
/// </summary>
internal sealed class Options
{
    public required string[] Raw { get; init; }
    public required string RepoRoot { get; init; }
    public required string Label { get; init; }
    public required string WebDirectory { get; init; }
    public required string Page { get; init; }
    public string? Query { get; init; }
    public required string Profile { get; init; }
    public required string AppDataDirectory { get; init; }
    public string? Theme { get; init; }
    public string? SystemTheme { get; init; }
    public required HashSet<string> Layers { get; init; }
    public required string Show { get; init; }
    public int ShowTimeoutMs { get; init; }
    public int BundleDelayMs { get; init; }
    public int HtmlDelayMs { get; init; }
    public int CssDelayMs { get; init; }
    public int NavigateDelayMs { get; init; }
    public int CaptureMs { get; init; }
    public required string CaptureWith { get; init; }
    public int AfterReadyMs { get; init; }
    public bool Live { get; init; }
    public bool ReducedMotion { get; init; }
    public bool NoHash { get; init; }
    public bool NoCsp { get; init; }
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
        var label = Option("--label") ?? "run";
        var profile = Option("--profile") ?? label;
        var profileDirectory = Path.Combine(root, "artifacts", "webview2-profiles", "theme-startup-" + profile);
        if (args.Contains("--fresh") && Directory.Exists(profileDirectory))
        {
            Directory.Delete(profileDirectory, recursive: true);
        }

        var options = new Options
        {
            Raw = args,
            RepoRoot = root,
            Label = label,
            WebDirectory = Path.GetFullPath(Option("--web") ?? Path.Combine(root, "spikes", "theme-startup", "frontend", "dist")) + Path.DirectorySeparatorChar,
            Page = Option("--page") ?? "baseline.html",
            Query = Option("--query"),
            Profile = profile,
            AppDataDirectory = Path.Combine(root, "artifacts", "theme-startup", "appdata", Option("--appdata") ?? label),
            Theme = Option("--theme"),
            SystemTheme = Option("--system"),
            Layers = [.. (Option("--layers") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)],
            Show = Option("--show") ?? "now",
            ShowTimeoutMs = Number("--show-timeout", 3000),
            BundleDelayMs = Number("--bundle-delay", 0),
            HtmlDelayMs = Number("--html-delay", 0),
            CssDelayMs = Number("--css-delay", 0),
            NavigateDelayMs = Number("--navigate-delay", 0),
            CaptureMs = Number("--capture", 6000),
            CaptureWith = Option("--capture-with") ?? "print",
            AfterReadyMs = Number("--after-ready", 800),
            Live = args.Contains("--live"),
            ReducedMotion = args.Contains("--reduced-motion"),
            NoHash = args.Contains("--no-hash"),
            NoCsp = args.Contains("--no-csp"),
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

        throw new InvalidOperationException("Run from inside the repository (global.json not found).");
    }
}
