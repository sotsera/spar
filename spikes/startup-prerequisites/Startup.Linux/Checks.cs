using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Startup.Linux;

internal enum ProblemKind
{
    Missing = 3,
    TooOld = 4,
    Broken = 5,
    NoDisplay = 6,
}

internal sealed record StartupProblem(ProblemKind Kind, string Component, string Summary, string Fix, string Details)
{
    public int ExitCode => (int)Kind;
}

// The start-up check, in order: GLib, GTK 4, a display (environment, then gtk_init_check), WebKitGTK 6.0, its version,
// its helper executables, bubblewrap and xdg-dbus-proxy, and a trial run of bubblewrap. The first failure is returned.
internal sealed class Checks(Options o, Log log)
{
    private const string WebKitPackages = "WebKitGTK 6.0";

    public bool GlibLoaded { get; private set; }
    public bool GtkLoaded { get; private set; }
    public bool GtkReady { get; private set; }
    public Distro Distro { get; } = Distro.Detect();

    private string AppId => new(o.AppName.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray());

    public StartupProblem? Run()
    {
        // Bind dlerror before the first dlopen, or the first failure's reason comes back empty.
        _ = LibC.dlerror();
        log.Mark("distro", ("id", Distro.Id), ("like", Distro.Like), ("family", Distro.Family));
        ProbeDisplayEnvironment();
        var problem = Load("GLib", GLib.Lib) ?? Load("GLib", GObject.Lib) ?? Load("GLib", Gio.Lib);
        GlibLoaded = problem is null;
        problem ??= Load("GTK 4", Gtk.Lib);
        GtkLoaded = GlibLoaded && problem is null;
        problem ??= CheckDisplay() ?? Load(WebKitPackages, WebKit.Lib) ?? CheckVersion() ?? CheckHelpers() ?? CheckSandboxTools() ?? TrySandbox();
        return problem;
    }

    private StartupProblem? Load(string what, string file)
    {
        var started = log.Now;
        if (NativeLibrary.TryLoad(file, typeof(Checks).Assembly, null, out _))
        {
            log.Mark("library", ("file", file), ("ok", true), ("ms", log.Now - started));
            return null;
        }

        // NativeLibrary does not say why; dlopen with the same name gives the loader's reason (a missing dependency
        // is named there).
        var reason = LibC.dlopen(file, LibC.RtldNow) == 0 ? Marshal.PtrToStringUTF8(LibC.dlerror()) : "loaded on retry";
        log.Mark("library", ("file", file), ("ok", false), ("reason", reason));
        return new StartupProblem(ProblemKind.Missing, what,
            $"{o.AppName} needs {what}, which is not installed on this system ({file} could not be loaded).",
            $"Install it with: {Distro.InstallWebKit}", $"dlopen({file}): {reason}");
    }

    // A display named in the environment (and, for local X11 and Wayland, its socket). Evaluated before the libraries, so
    // the report knows whether anything graphical can be tried even when GTK is missing.
    public bool DisplayInEnvironment { get; private set; }

    private string _displayDescription = "";

    private void ProbeDisplayEnvironment()
    {
        var wayland = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        var x11 = Environment.GetEnvironmentVariable("DISPLAY");
        var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        var waylandSocket = string.IsNullOrEmpty(wayland) ? null : Path.IsPathRooted(wayland) ? wayland : runtimeDir is null ? null : Path.Combine(runtimeDir, wayland);
        var waylandOk = waylandSocket is not null && File.Exists(waylandSocket);
        var x11Socket = X11Socket(x11);
        var x11Ok = !string.IsNullOrEmpty(x11) && (x11Socket is null || File.Exists(x11Socket));
        log.Mark("display-env", ("WAYLAND_DISPLAY", wayland), ("waylandSocket", waylandOk), ("DISPLAY", x11), ("x11Socket", x11Socket), ("x11Ok", x11Ok));
        DisplayInEnvironment = waylandOk || x11Ok;
        _displayDescription = $"DISPLAY={x11 ?? "(unset)"}, WAYLAND_DISPLAY={wayland ?? "(unset)"}";
    }

    private StartupProblem? CheckDisplay()
    {
        var x11 = Environment.GetEnvironmentVariable("DISPLAY");
        var wayland = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        if (!DisplayInEnvironment)
        {
            return NoDisplay(_displayDescription);
        }

        // The program name becomes the X11 WM_CLASS and the Wayland app_id (how the desktop ties the window to its
        // .desktop file); without it the dialog had no class and openbox titled it "Unnamed Window".
        GLib.g_set_prgname(AppId);
        GLib.g_set_application_name(o.AppName);
        var started = log.Now;
        GtkReady = Gtk.gtk_init_check() != 0;
        log.Mark("gtk-init", ("ok", GtkReady), ("ms", log.Now - started),
            ("gtk", $"{Gtk.gtk_get_major_version()}.{Gtk.gtk_get_minor_version()}.{Gtk.gtk_get_micro_version()}"));
        return GtkReady ? null : NoDisplay($"gtk_init_check failed with DISPLAY={x11 ?? "(unset)"}, WAYLAND_DISPLAY={wayland ?? "(unset)"}");
    }

    // ":1" or ":1.0" is a local X server listening on /tmp/.X11-unix/X1; "host:1" is remote and not checked here.
    private static string? X11Socket(string? display)
    {
        if (string.IsNullOrEmpty(display) || !display.StartsWith(':'))
        {
            return null;
        }

        var number = display[1..].Split('.')[0];
        return $"/tmp/.X11-unix/X{number}";
    }

    private StartupProblem NoDisplay(string details) => new(ProblemKind.NoDisplay, "a graphical session",
        $"{o.AppName} needs a graphical desktop session, and none was found.",
        "Start it from your desktop (or with DISPLAY or WAYLAND_DISPLAY set), not from a text console or an SSH session without forwarding.",
        details);

    private StartupProblem? CheckVersion()
    {
        var found = new Version((int)WebKit.webkit_get_major_version(), (int)WebKit.webkit_get_minor_version(), (int)WebKit.webkit_get_micro_version());
        log.Mark("webkit-version", ("found", found.ToString()), ("minimum", o.MinimumWebKit.ToString()));
        return found >= o.MinimumWebKit ? null : new StartupProblem(ProblemKind.TooOld, WebKitPackages,
            $"{o.AppName} needs WebKitGTK {o.MinimumWebKit} or later. This system has WebKitGTK {found}.",
            $"Update your system ({Distro.Upgrade}), or use a newer release of your distribution.",
            $"webkit_get_*_version = {found}, minimum {o.MinimumWebKit}");
    }

    // WebKit starts WebKitWebProcess and WebKitNetworkProcess from a folder compiled into the library; it is found
    // next to the library actually loaded (/proc/self/maps).
    private StartupProblem? CheckHelpers()
    {
        var loaded = File.ReadLines("/proc/self/maps")
            .Select(line => line.Split(' ', 6, StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length == 6 && parts[5].Contains("libwebkitgtk-6.0.so", StringComparison.Ordinal))
            .Select(parts => parts[5].Trim())
            .FirstOrDefault();
        if (loaded is null)
        {
            return null;
        }

        var folder = Path.Combine(Path.GetDirectoryName(loaded)!, "webkitgtk-6.0");
        var missing = new[] { "WebKitWebProcess", "WebKitNetworkProcess" }.Select(h => Path.Combine(folder, h)).Where(h => !IsExecutable(h)).ToList();
        log.Mark("webkit-helpers", ("library", loaded), ("folder", folder), ("missing", string.Join(',', missing)));
        return missing.Count == 0 ? null : new StartupProblem(ProblemKind.Broken, WebKitPackages,
            $"WebKitGTK is installed, but programs it needs are missing: {string.Join(", ", missing.Select(Path.GetFileName))}.",
            $"Reinstall it: {Distro.ReinstallWebKit}", $"missing or not executable: {string.Join(", ", missing)}");
    }

    private string? Find(string tool) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin").Split(':').Append("/usr/bin").Select(d => Path.Combine(d, tool)).FirstOrDefault(IsExecutable);

    private StartupProblem? CheckSandboxTools()
    {
        var missing = new[] { "bwrap", "xdg-dbus-proxy" }.Where(t => Find(t) is null).ToList();
        log.Mark("sandbox-tools", ("missing", string.Join(',', missing)));
        return missing.Count == 0 ? null : new StartupProblem(ProblemKind.Missing, "the WebKitGTK sandbox tools",
            $"{o.AppName} needs {string.Join(" and ", missing)} to run its web content safely, and they are not installed.",
            $"Install them with: {Distro.InstallSandbox}", $"not found on PATH or in /usr/bin: {string.Join(", ", missing)}");
    }

    // The web process sandbox needs unprivileged user namespaces. A trial bwrap run shows whether this system allows
    // them (Docker's default seccomp profile, some hardened kernels and AppArmor policies do not).
    private StartupProblem? TrySandbox()
    {
        if (o.SkipSandboxTrial)
        {
            return null;
        }

        var started = log.Now;
        var bwrap = Find("bwrap")!;
        var info = new ProcessStartInfo(bwrap) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var a in new[] { "--ro-bind", "/", "/", "--dev", "/dev", "--proc", "/proc", "--unshare-all", "--die-with-parent", "true" })
        {
            info.ArgumentList.Add(a);
        }

        using var p = Process.Start(info)!;
        var error = p.StandardError.ReadToEnd().Trim();
        p.WaitForExit();
        log.Mark("sandbox-trial", ("exit", p.ExitCode), ("ms", log.Now - started), ("stderr", error), ("uid", LibC.getuid()));
        return p.ExitCode == 0 ? null : new StartupProblem(ProblemKind.Broken, "the WebKitGTK sandbox",
            $"The web content sandbox cannot start on this system: {error}",
            "The system does not allow unprivileged user namespaces (for example inside a container, or under a security policy). Ask your administrator to allow them for bubblewrap.",
            $"bwrap trial exited {p.ExitCode}: {error}");
    }

    private static bool IsExecutable(string path) =>
        File.Exists(path) && (File.GetUnixFileMode(path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
}

// The install commands per distribution family, from /etc/os-release (ID and ID_LIKE). Package names: Debian and
// Ubuntu verified in this spike's containers; the others are from the distributions' package indexes, not run here.
internal sealed record Distro(string Id, string Like, string Family)
{
    public static Distro Detect()
    {
        var values = File.Exists("/etc/os-release")
            ? File.ReadLines("/etc/os-release").Select(l => l.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1].Trim('"'))
            : [];
        var id = values.GetValueOrDefault("ID", "");
        var like = values.GetValueOrDefault("ID_LIKE", "");
        var all = $"{id} {like}";
        var family = all.Contains("debian", StringComparison.Ordinal) || all.Contains("ubuntu", StringComparison.Ordinal) ? "debian"
            : all.Contains("fedora", StringComparison.Ordinal) || all.Contains("rhel", StringComparison.Ordinal) ? "fedora"
            : all.Contains("arch", StringComparison.Ordinal) ? "arch"
            : all.Contains("suse", StringComparison.Ordinal) ? "suse" : "other";
        return new Distro(id, like, family);
    }

    public string InstallWebKit => Family switch
    {
        "debian" => "sudo apt install libwebkitgtk-6.0-4",
        "fedora" => "sudo dnf install webkitgtk6.0",
        "arch" => "sudo pacman -S webkitgtk-6.0",
        "suse" => "sudo zypper install libwebkitgtk-6_0-4",
        _ => "your distribution's package manager (package: WebKitGTK 6.0, which brings GTK 4)",
    };

    public string ReinstallWebKit => Family switch
    {
        "debian" => "sudo apt install --reinstall libwebkitgtk-6.0-4",
        "fedora" => "sudo dnf reinstall webkitgtk6.0",
        "arch" => "sudo pacman -S webkitgtk-6.0",
        "suse" => "sudo zypper install --force libwebkitgtk-6_0-4",
        _ => "reinstall WebKitGTK 6.0 with your distribution's package manager",
    };

    public string InstallSandbox => Family switch
    {
        "debian" => "sudo apt install bubblewrap xdg-dbus-proxy",
        "fedora" => "sudo dnf install bubblewrap xdg-dbus-proxy",
        "arch" => "sudo pacman -S bubblewrap xdg-dbus-proxy",
        "suse" => "sudo zypper install bubblewrap xdg-dbus-proxy",
        _ => "your distribution's package manager (packages bubblewrap and xdg-dbus-proxy)",
    };

    public string Upgrade => Family switch
    {
        "debian" => "sudo apt update && sudo apt upgrade",
        "fedora" => "sudo dnf upgrade",
        "arch" => "sudo pacman -Syu",
        "suse" => "sudo zypper update",
        _ => "with your distribution's package manager",
    };
}
