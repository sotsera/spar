using System.Globalization;
using System.Runtime.InteropServices;
using Spar.Hosting;
using Spar.Linux.Interop;

namespace Spar.Linux;

/// <summary>
/// The start-up checks of plan section 3, "Missing prerequisites" (Linux order), each a fixed exit code: a display named
/// in the environment (6), then, after single instance, GLib and GTK 4 loadable (3) and new enough for the host's own
/// dialogs (4), WebKitGTK 6.0 loadable (3) and at least <see cref="MinimumWebKit"/> (4), its helper programs (5),
/// <c>bwrap</c> and <c>xdg-dbus-proxy</c> (3), and a trial <c>bwrap</c> run (5), which catches a denied sandbox in
/// milliseconds instead of WebKit's abort seconds later. None of them starts GTK: the libraries are loaded, never
/// initialised, so the environment can still be set before GTK starts. The decisions are plain functions, so tests
/// cover every path; a Development or Test build can raise the WebKitGTK minimum through the environment to force
/// "too old" on a healthy machine, and a Production build has no such override.
/// </summary>
internal static class Prerequisites
{
    /// <summary>
    /// The provisional minimum WebKitGTK (plan section 3, "Runtime packages"): the APIs used need 2.40, and the
    /// <c>JSC_useWasm</c> option name 2.48; M4 sets the supported one.
    /// </summary>
    public const string MinimumWebKit = "2.48.0";

    /// <summary>The oldest GTK 4 with <c>GtkAlertDialog</c>, which every message of the host uses.</summary>
    public const string MinimumGtk = "4.10.0";

    /// <summary>Raises the minimum WebKitGTK in Development and Test builds only, to force "too old".</summary>
    public const string RequireWebKitVariable = "SPAR_DEV_REQUIRE_WEBKITGTK";

    public const string WebKitComponent = "WebKitGTK 6.0";

    private const string GtkComponent = "GTK 4";

    /// <summary>How long the trial run may take; it took 3–19 ms where allowed and 1.5 ms where denied.</summary>
    private const int TrialMilliseconds = 5_000;

    /// <summary>The bwrap arguments of the trial: every namespace WebKit's sandbox unshares, around <c>true</c>.</summary>
    private static readonly string[] TrialArguments =
        ["--ro-bind", "/", "/", "--dev", "/dev", "--proc", "/proc", "--unshare-all", "--die-with-parent", "true"];

    /// <summary>
    /// The display check, before anything else (plan section 3, Linux order, step 1): a session names its display in
    /// <c>WAYLAND_DISPLAY</c> or <c>DISPLAY</c>. Without either, nothing graphical can be shown.
    /// </summary>
    public static StartupProblem? CheckDisplay(Func<string, string?> variable)
    {
        if (!string.IsNullOrEmpty(variable("WAYLAND_DISPLAY")) || !string.IsNullOrEmpty(variable("DISPLAY")))
        {
            return null;
        }

        return NoDisplay("No graphical session: neither DISPLAY nor WAYLAND_DISPLAY is set. Start the app from the desktop.");
    }

    /// <summary>GTK found no display it could open (it was named, but no server answered).</summary>
    public static StartupProblem DisplayNotOpened(string? display) =>
        NoDisplay($"No graphical session: GTK could not open the display {display}. Start the app from the desktop.");

    /// <summary>
    /// The library and program checks, in plan section 3's order, then the trial sandbox run. Returns the first problem,
    /// and whether GTK 4 can show the message (loaded and new enough).
    /// </summary>
    public static StartupProblem? CheckLibraries(Distribution distribution, HostLog log, out bool gtkUsable)
    {
        gtkUsable = false;
        foreach (var library in new[] { "libglib-2.0.so.0", "libgobject-2.0.so.0", "libgio-2.0.so.0" })
        {
            if (Load(library) is { } failure)
            {
                return Missing("GLib", failure, distribution);
            }
        }

        if (Load("libgtk-4.so.1") is { } noGtk)
        {
            return Missing(GtkComponent, noGtk, distribution);
        }

        var gtk = new Version((int)Gtk.gtk_get_major_version(), (int)Gtk.gtk_get_minor_version(), (int)Gtk.gtk_get_micro_version());
        if (DecideGtk(gtk, distribution) is { } oldGtk)
        {
            return oldGtk;
        }

        gtkUsable = true;
        foreach (var library in new[] { "libwebkitgtk-6.0.so.4", "libjavascriptcoregtk-6.0.so.1", "libsoup-3.0.so.0" })
        {
            if (Load(library) is { } failure)
            {
                return Missing(WebKitComponent, failure, distribution);
            }
        }

        var webKit = new Version(
            (int)WebKit.webkit_get_major_version(), (int)WebKit.webkit_get_minor_version(), (int)WebKit.webkit_get_micro_version());
        log.Info($"GTK {gtk}, WebKitGTK {webKit}.");
        if (DecideWebKit(webKit, RequiredWebKit(), distribution) is { } oldWebKit)
        {
            return oldWebKit;
        }

        if (CheckHelpers(File.ReadAllText("/proc/self/maps"), distribution) is { } helpers)
        {
            return helpers;
        }

        return CheckSandbox(distribution, log);
    }

    /// <summary>
    /// Whether a message can be shown with GTK 4, for a problem found before the library checks (a relaunch that
    /// cannot reach the running app): GLib and GTK load, and GTK is new enough.
    /// </summary>
    public static bool CanShowGtkMessage() =>
        Load("libglib-2.0.so.0") is null
        && Load("libgobject-2.0.so.0") is null
        && Load("libgtk-4.so.1") is null
        && DecideGtk(new Version((int)Gtk.gtk_get_major_version(), (int)Gtk.gtk_get_minor_version(), (int)Gtk.gtk_get_micro_version()), Distribution.FromOsRelease("")) is null;

    /// <summary>Whether GTK 4 is new enough for the host's own dialogs.</summary>
    internal static StartupProblem? DecideGtk(Version found, Distribution distribution)
    {
        var required = Version.Parse(MinimumGtk);
        if (found >= required)
        {
            return null;
        }

        return new StartupProblem(
            StartupProblemKind.TooOld,
            GtkComponent,
            found.ToString(3),
            required.ToString(2),
            $"This app needs GTK {required.ToString(2)} or later; this computer has {found.ToString(3)}. {distribution.UpgradeHint}",
            null);
    }

    /// <summary>Whether WebKitGTK is at least <paramref name="required"/>.</summary>
    internal static StartupProblem? DecideWebKit(Version found, Version required, Distribution distribution)
    {
        if (found >= required)
        {
            return null;
        }

        return new StartupProblem(
            StartupProblemKind.TooOld,
            WebKitComponent,
            found.ToString(3),
            required.ToString(3),
            $"This app needs WebKitGTK {required.ToString(3)} or later; this computer has {found.ToString(3)}. {distribution.UpgradeHint}",
            null);
    }

    /// <summary>
    /// WebKit's helper programs, beside the library actually loaded (<paramref name="maps"/>, the text of
    /// <c>/proc/self/maps</c>): Debian and Ubuntu keep them in <c>&lt;libdir&gt;/webkitgtk-6.0</c>, Fedora in
    /// <c>/usr/libexec/webkitgtk-6.0</c>. Without them WebKit aborts seconds after the window shows.
    /// </summary>
    internal static StartupProblem? CheckHelpers(string maps, Distribution distribution)
    {
        if (HelperFolder(maps, HasHelpers) is not null)
        {
            return null;
        }

        return new StartupProblem(
            StartupProblemKind.Broken,
            WebKitComponent,
            null,
            null,
            $"WebKitGTK's helper programs (WebKitWebProcess, WebKitNetworkProcess) are missing. {distribution.ReinstallHint}",
            null);
    }

    /// <summary>The folder that holds both helpers, or null; <paramref name="hasHelpers"/> checks a candidate folder.</summary>
    internal static string? HelperFolder(string maps, Func<string, bool> hasHelpers)
    {
        string? library = null;
        foreach (var line in maps.Split('\n'))
        {
            var start = line.IndexOf('/');
            if (start >= 0 && line.AsSpan(start).Contains("/libwebkitgtk-6.0.so", StringComparison.Ordinal))
            {
                library = line[start..].Trim();
                break;
            }
        }

        // Linux paths, built with '/' so the decision reads the same in the tests on every OS.
        var candidates = new List<string>();
        if (library is not null && library.LastIndexOf('/') is > 0 and var end)
        {
            var libraryFolder = library[..end];
            candidates.Add(libraryFolder + "/webkitgtk-6.0");
            if (libraryFolder.LastIndexOf('/') is > 0 and var parent)
            {
                candidates.Add(libraryFolder[..parent] + "/libexec/webkitgtk-6.0");
            }
        }

        candidates.Add("/usr/libexec/webkitgtk-6.0");
        foreach (var folder in candidates)
        {
            if (hasHelpers(folder))
            {
                return folder;
            }
        }

        return null;
    }

    /// <summary>
    /// <c>bwrap</c> and <c>xdg-dbus-proxy</c>, which WebKit's sandbox runs, then a trial <c>bwrap</c> run with the
    /// namespaces it needs: a sandbox the system denies (no user namespaces, a container's seccomp profile, AppArmor's
    /// restriction) fails it at once.
    /// </summary>
    private static StartupProblem? CheckSandbox(Distribution distribution, HostLog log)
    {
        var bubblewrap = FindProgram("bwrap");
        var proxy = FindProgram("xdg-dbus-proxy");
        var missing = (bubblewrap, proxy) switch
        {
            (null, null) => "bubblewrap and xdg-dbus-proxy",
            (null, _) => "bubblewrap",
            (_, null) => "xdg-dbus-proxy",
            _ => null,
        };
        if (missing is not null)
        {
            return new StartupProblem(
                StartupProblemKind.Missing,
                missing,
                null,
                null,
                $"This app's web content runs in a sandbox, which needs {missing}. {distribution.SandboxHint}",
                null);
        }

        var trial = ChildProcess.Run(bubblewrap!, TrialArguments, captureOutput: true, TrialMilliseconds);
        log.Info(string.Create(CultureInfo.InvariantCulture, $"Sandbox trial: bwrap exit {trial.ExitCode} in {trial.Milliseconds:0.0} ms."));
        return DecideTrial(trial);
    }

    /// <summary>The trial run's verdict: null when bwrap ran <c>true</c> in its namespaces.</summary>
    internal static StartupProblem? DecideTrial(ChildResult trial)
    {
        if (trial is { Started: true, ExitCode: 0 })
        {
            return null;
        }

        var why = trial switch
        {
            { Started: false } => trial.Output,
            { TimedOut: true } => $"bwrap did not finish within {TrialMilliseconds / 1000} seconds",
            _ => trial.Output.Length > 0 ? trial.Output : $"bwrap exited with {trial.ExitCode}",
        };
        return new StartupProblem(
            StartupProblemKind.Broken,
            "Web content sandbox",
            null,
            null,
            $"The sandbox this app's web content needs cannot start on this computer ({why}). It needs unprivileged user namespaces: in a container, run it with a profile that allows them; on a desktop, ask your administrator.",
            null);
    }

    /// <summary>A program in <c>/usr/bin</c>, where distributions build WebKit to look for it, or on <c>PATH</c>; null when absent.</summary>
    private static string? FindProgram(string name)
    {
        var folders = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries);
        foreach (var folder in folders.Prepend("/usr/bin"))
        {
            var path = Path.Combine(folder, name);
            if (IsExecutable(path))
            {
                return path;
            }
        }

        return null;
    }

    private static bool HasHelpers(string folder) =>
        IsExecutable(Path.Combine(folder, "WebKitWebProcess")) && IsExecutable(Path.Combine(folder, "WebKitNetworkProcess"));

    private static bool IsExecutable(string path)
    {
        try
        {
            return File.Exists(path) && (File.GetUnixFileMode(path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Loads a library by its soname, so a missing one is a message, never a <c>DllNotFoundException</c> later.</summary>
    private static string? Load(string library)
    {
        try
        {
            NativeLibrary.Load(library);
            return null;
        }
        catch (DllNotFoundException exception)
        {
            return exception.Message;
        }
    }

    private static StartupProblem Missing(string component, string failure, Distribution distribution) => new(
        StartupProblemKind.Missing,
        component,
        null,
        component == WebKitComponent ? MinimumWebKit : null,
        $"This app needs {component}, which is not installed on this computer. {distribution.InstallHint} ({failure})",
        null);

    private static StartupProblem NoDisplay(string details) =>
        new(StartupProblemKind.NoGraphicalSession, "Display", null, null, details, null);

    private static Version RequiredWebKit()
    {
        if (SparDevelopment.IsSupported
            && Environment.GetEnvironmentVariable(RequireWebKitVariable) is { Length: > 0 } text
            && Version.TryParse(text, out var raised))
        {
            return new Version(raised.Major, raised.Minor, Math.Max(raised.Build, 0));
        }

        return Version.Parse(MinimumWebKit);
    }
}

/// <summary>
/// What the messages tell the user to run, by the distribution's family in <c>/etc/os-release</c> (<c>ID</c> and
/// <c>ID_LIKE</c>): Debian and Ubuntu, and Fedora, whose package names were checked; anything else gets the package's
/// name on both.
/// </summary>
internal sealed record Distribution(string InstallHint, string ReinstallHint, string UpgradeHint, string SandboxHint)
{
    public static Distribution Read()
    {
        try
        {
            return File.Exists("/etc/os-release") ? FromOsRelease(File.ReadAllText("/etc/os-release")) : FromOsRelease("");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return FromOsRelease("");
        }
    }

    internal static Distribution FromOsRelease(string osRelease)
    {
        var family = "";
        foreach (var line in osRelease.Split('\n'))
        {
            var parts = line.Trim().Split('=', 2);
            if (parts.Length == 2 && parts[0] is "ID" or "ID_LIKE")
            {
                family += " " + parts[1].Trim('"', '\'');
            }
        }

        var words = family.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Contains("debian") || words.Contains("ubuntu"))
        {
            return new Distribution(
                "Install it with: sudo apt install libwebkitgtk-6.0-4",
                "Install it again with: sudo apt install --reinstall libwebkitgtk-6.0-4",
                "Update the system with: sudo apt update && sudo apt upgrade",
                "Install them with: sudo apt install bubblewrap xdg-dbus-proxy");
        }

        if (words.Contains("fedora"))
        {
            return new Distribution(
                "Install it with: sudo dnf install webkitgtk6.0",
                "Install it again with: sudo dnf reinstall webkitgtk6.0",
                "Update the system with: sudo dnf upgrade",
                "Install them with: sudo dnf install bubblewrap xdg-dbus-proxy");
        }

        return new Distribution(
            "Install the WebKitGTK 6.0 package (libwebkitgtk-6.0-4 on Debian and Ubuntu, webkitgtk6.0 on Fedora).",
            "Install the WebKitGTK 6.0 package again.",
            "Update the system's packages.",
            "Install the bubblewrap and xdg-dbus-proxy packages.");
    }
}
