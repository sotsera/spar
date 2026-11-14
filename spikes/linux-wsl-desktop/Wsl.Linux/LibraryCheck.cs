using System.Runtime.InteropServices;

namespace Wsl.Linux;

/// <summary>
/// Start-up check for the native libraries the host imports. The NativeAOT executable links only glibc; GTK and WebKit
/// are loaded at their first call, so without this check a missing library surfaces as an unhandled
/// DllNotFoundException on the UI thread. Every library is tried with the same resolution the imports use
/// (NativeLibrary with this assembly), and a failure is explained with the loader's own message from dlopen.
/// </summary>
internal static class LibraryCheck
{
    private static readonly (string File, string What, string DebianPackage)[] s_libraries =
    [
        ("libglib-2.0.so.0", "GLib", "libglib2.0-0"),
        ("libgobject-2.0.so.0", "GObject", "libglib2.0-0"),
        ("libgio-2.0.so.0", "GIO", "libglib2.0-0"),
        ("libsoup-3.0.so.0", "libsoup 3", "libsoup-3.0-0"),
#if SPAR_GTK4
        ("libgtk-4.so.1", "GTK 4", "libgtk-4-1"),
        ("libjavascriptcoregtk-6.0.so.1", "JavaScriptCore for WebKitGTK 6.0", "libjavascriptcoregtk-6.0-1"),
        ("libwebkitgtk-6.0.so.4", "WebKitGTK 6.0", "libwebkitgtk-6.0-4"),
#else
        ("libgtk-3.so.0", "GTK 3", "libgtk-3-0"),
        ("libgdk-3.so.0", "GDK 3", "libgtk-3-0"),
        ("libjavascriptcoregtk-4.1.so.0", "JavaScriptCore for WebKitGTK 4.1", "libjavascriptcoregtk-4.1-0"),
        ("libwebkit2gtk-4.1.so.0", "WebKitGTK 4.1", "libwebkit2gtk-4.1-0"),
#endif
    ];

    /// <summary>Returns null when every library loads, or the message to print.</summary>
    public static string? Run()
    {
        var problems = new List<string>();
        var packages = new List<string>();

        // Bind dlerror before the first dlopen: the lazy binding of an import goes through the dynamic loader and
        // clears the pending error, which made the first failure's reason come back empty.
        _ = LibC.dlerror();
        foreach (var (file, what, package) in s_libraries)
        {
            if (NativeLibrary.TryLoad(file, typeof(LibraryCheck).Assembly, null, out _))
            {
                continue;
            }

            // NativeLibrary does not say why; dlopen with the same name gives the loader's reason.
            var reason = LibC.dlopen(file, LibC.RtldNow) == 0 ? Marshal.PtrToStringUTF8(LibC.dlerror()) : "dlopen succeeded on retry";
            problems.Add($"  - {what} ({file}): {reason}");
            if (!packages.Contains(package))
            {
                packages.Add(package);
            }
        }

        if (problems.Count == 0)
        {
            return CheckHelpers();
        }

        return "This application needs libraries that could not be loaded:\n" + string.Join('\n', problems) + "\n" +
            "Install them with your distribution's package manager. On Debian and Ubuntu: sudo apt install " + string.Join(' ', packages);
    }

    /// <summary>
    /// WebKit starts helper executables from a folder compiled into the library (its libexec folder) and, for the
    /// sandbox, bwrap and xdg-dbus-proxy. A missing helper ends in a GLib ERROR (SIGTRAP) seconds later, so the folder
    /// is derived from where the library was actually loaded (/proc/self/maps) and checked here.
    /// </summary>
    private static string? CheckHelpers()
    {
#if SPAR_GTK4
        const string Library = "libwebkitgtk-6.0.so.4";
        const string Folder = "webkitgtk-6.0";
        var sandbox = true;
#else
        const string Library = "libwebkit2gtk-4.1.so.0";
        const string Folder = "webkit2gtk-4.1";
        var sandbox = Environment.GetCommandLineArgs().Contains("--sandbox");
#endif
        var loaded = File.ReadLines("/proc/self/maps")
            .Select(line => line.Split(' ', 6, StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length == 6 && parts[5].Contains(Library.Split('.')[0], StringComparison.Ordinal))
            .Select(parts => parts[5].Trim())
            .FirstOrDefault();
        if (loaded is null)
        {
            return null;
        }

        var missing = new List<string>();
        var helpers = Path.Combine(Path.GetDirectoryName(loaded)!, Folder);
        foreach (var helper in new[] { "WebKitWebProcess", "WebKitNetworkProcess" })
        {
            if (!File.Exists(Path.Combine(helpers, helper)))
            {
                missing.Add(Path.Combine(helpers, helper));
            }
        }

        if (sandbox)
        {
            foreach (var tool in new[] { "bwrap", "xdg-dbus-proxy" })
            {
                if (!(Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin").Split(':').Any(directory => IsExecutable(Path.Combine(directory, tool))))
                {
                    missing.Add(tool + " (needed by the web process sandbox; packages bubblewrap and xdg-dbus-proxy)");
                }
            }
        }

        return missing.Count == 0 ? null
            : "WebKitGTK (" + loaded + ") is installed, but files it needs are missing or not executable:\n  - " + string.Join("\n  - ", missing) + "\n" +
              "Reinstall the WebKitGTK package (and bubblewrap and xdg-dbus-proxy) with your distribution's package manager.";
    }

    private static bool IsExecutable(string path) =>
        File.Exists(path) && (File.GetUnixFileMode(path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0
        && new FileInfo(path).Length > 0;
}
