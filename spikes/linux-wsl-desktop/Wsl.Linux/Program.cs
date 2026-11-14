using System.Globalization;
using System.Runtime.Versioning;

// The host runs only on Linux (GTK and WebKitGTK).
[assembly: SupportedOSPlatform("linux")]

namespace Wsl.Linux;

/// <summary>
/// Command line (paths relative to the current directory, which must be inside the repository copy):
///   --label NAME          output artifacts/linux-wsl-desktop/runs/NAME/timeline.json
///   --title TEXT          window title (default "Spar WSL NAME"); the Windows-side capture finds the window by it
///   --web DIR --page FILE --query Q   page to open (default spikes/linux-wsl-desktop/web, probe.html)
///   --profile NAME --fresh            WebKit data artifacts/webview2-profiles/linux-wsl-NAME; --fresh deletes it first
///   --sandbox             WebKitGTK 4.1 only: webkit_web_context_set_sandbox_enabled(TRUE) (6.0 is always sandboxed)
///   --hw POLICY           WebKitSettings hardware-acceleration-policy (4.1: ondemand|always|never; 6.0: always|never)
///   --gpu-page            after ui-ready, open webkit://gpu and save its text as gpu.txt
///   --order ORDER         show-first (default: show, create the view, load), load-first (create, load, show) or
///                         show-on-commit (create, load, show at load-committed)
///   --grab-focus          gtk_widget_grab_focus on the web view after attaching it
///   --resize-test         after ui-ready: maximize, wait, unmaximize, wait (the page reports resizes and frames)
///   --after-ready MS      time to stay open after ui-ready (default 800); the page's own tests run meanwhile
///   --theme light|dark|system, --system light|dark, --layers css,prefer-dark,gtk-theme,wv-bg,transparent,
///   --headerbar, --size WxH, --no-lib-check
/// </summary>
internal sealed class Options
{
    public required string[] Raw { get; init; }
    public required string RepoRoot { get; init; }
    public required string Label { get; init; }
    public required string Title { get; init; }
    public required string WebDirectory { get; init; }
    public required string Page { get; init; }
    public string? Query { get; init; }
    public required string DataDirectory { get; init; }
    public string? Theme { get; init; }
    public string? SystemTheme { get; init; }
    public required HashSet<string> Layers { get; init; }
    public bool HeaderBar { get; init; }
    public bool Sandbox { get; init; }
    public string? AccelerationPolicy { get; init; }
    public bool GpuPage { get; init; }
    public bool ResizeTest { get; init; }
    public bool GrabFocus { get; init; }
    public required string Order { get; init; }
    public int AfterReadyMs { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
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

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"SPAR_PROCESS_START {Environment.TickCount64}"));
        if (!args.Contains("--no-lib-check") && LibraryCheck.Run() is { } problem)
        {
            Console.Error.WriteLine(problem);
            return 3;
        }

        var root = FindRepoRoot();
        var label = Option("--label") ?? "wsl-run";
        var data = Path.Combine(root, "artifacts", "webview2-profiles", "linux-wsl-" + (Option("--profile") ?? label));
        if (args.Contains("--fresh") && Directory.Exists(data))
        {
            Directory.Delete(data, recursive: true);
        }

        var size = (Option("--size") ?? "960x720").Split('x');
        var options = new Options
        {
            Raw = args,
            RepoRoot = root,
            Label = label,
            Title = Option("--title") ?? "Spar WSL " + label,
            WebDirectory = Path.GetFullPath(Option("--web") ?? Path.Combine(root, "spikes", "linux-wsl-desktop", "web")) + "/",
            Page = Option("--page") ?? "probe.html",
            Query = Option("--query"),
            DataDirectory = data,
            Theme = Option("--theme"),
            SystemTheme = Option("--system"),
            Layers = [.. (Option("--layers") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)],
            HeaderBar = args.Contains("--headerbar"),
            Sandbox = args.Contains("--sandbox"),
            AccelerationPolicy = Option("--hw"),
            GpuPage = args.Contains("--gpu-page"),
            ResizeTest = args.Contains("--resize-test"),
            GrabFocus = args.Contains("--grab-focus"),
            Order = Option("--order") ?? "show-first",
            AfterReadyMs = Number("--after-ready", 800),
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
