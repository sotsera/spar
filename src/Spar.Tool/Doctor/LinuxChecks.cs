using Spar.Tool.Machine;

namespace Spar.Tool.Doctor;

/// <summary>
/// Linux: what the Linux host loads and runs (GTK 4, WebKitGTK 6.0, and the sandbox's <c>bwrap</c> and
/// <c>xdg-dbus-proxy</c>), what .NET itself loads (ICU, OpenSSL), and what NativeAOT links with (<c>clang</c>, zlib's
/// development library), each with the install command for the distribution's family in <c>/etc/os-release</c>:
/// apt on Debian and Ubuntu, dnf on Fedora (the host's own messages use the same names), the package's name otherwise.
/// </summary>
internal static class LinuxChecks
{
    /// <summary>The Linux host's minimums (<c>Spar.Linux.Prerequisites</c>; a test keeps them equal).</summary>
    public const string MinimumGtk = "4.10.0";

    public const string MinimumWebKit = "2.48.0";

    public const string NativeAotPrerequisites = "https://learn.microsoft.com/dotnet/core/deploying/native-aot/#prerequisites";

    private static readonly string[] LibraryFolders =
        ["/usr/lib/x86_64-linux-gnu", "/usr/lib/aarch64-linux-gnu", "/usr/lib64", "/usr/lib", "/lib/x86_64-linux-gnu", "/lib/aarch64-linux-gnu", "/lib64", "/lib"];

    public static IEnumerable<Finding> Check(IMachine machine)
    {
        var family = Family(machine.ReadText("/etc/os-release") ?? "");
        yield return Versioned(machine, family, FindingIds.Gtk, "GTK 4", "libgtk-4.so.1", "gtk_", MinimumGtk, Package.Gtk, Severity.Error);
        yield return Versioned(machine, family, FindingIds.WebKitGtk, "WebKitGTK 6.0", "libwebkitgtk-6.0.so.4", "webkit_", MinimumWebKit, Package.WebKit, Severity.Error);
        yield return SandboxTools(machine, family);
        yield return Icu(machine, family);
        yield return Loaded(machine, family, FindingIds.OpenSsl, "OpenSSL", ["libssl.so.3", "libssl.so.1.1"], Package.OpenSsl,
            "the .NET runtime loads it for HTTPS and cryptography");
        yield return Clang(machine, family);
        yield return Zlib(machine, family);
    }

    internal static Finding Versioned(IMachine machine, string family, string id, string component, string library, string prefix, string minimum, Package package, Severity severity)
    {
        var answer = machine.Library(library, prefix);
        if (!answer.Loaded)
        {
            return new Finding(id, Area.Machine, severity, $"{component} is not installed ({library} does not load): the app's window cannot start.", Install(family, package));
        }

        if (answer.Version is { } version && version < Version.Parse(minimum))
        {
            return new Finding(id, Area.Machine, severity, $"{component} {version} is older than Spar's minimum, {minimum}.",
                "Update the system's packages, or use a newer release of the distribution.");
        }

        return Finding.Info(id, Area.Machine, answer.Version is { } found ? $"{component} {found}." : $"{component} ({library}).");
    }

    internal static Finding SandboxTools(IMachine machine, string family)
    {
        var missing = new[] { "bwrap", "xdg-dbus-proxy" }.Where(tool => NodeChecks.OnPath(machine, tool) is null).ToList();
        return missing.Count == 0
            ? Finding.Info(FindingIds.Sandbox, Area.Machine, "WebKitGTK's sandbox tools, bwrap and xdg-dbus-proxy.")
            : Finding.Error(FindingIds.Sandbox, Area.Machine, $"{string.Join(" and ", missing)} not on PATH: WebKitGTK's web process sandbox needs them, and the app does not start without them.",
                Install(family, Package.Sandbox));
    }

    internal static Finding Icu(IMachine machine, string family)
    {
        var found = LibraryFolders.SelectMany(folder => machine.Files(folder, "libicuuc.so.*")).FirstOrDefault();
        return found is not null
            ? Finding.Info(FindingIds.Icu, Area.Machine, $"ICU ({found}).")
            : Finding.Warning(FindingIds.Icu, Area.Machine,
                "ICU is not installed: the .NET SDK needs it, and so does an app that sets InvariantGlobalization to false (Spar apps are invariant by default).",
                Install(family, Package.Icu));
    }

    internal static Finding Loaded(IMachine machine, string family, string id, string component, string[] libraries, Package package, string why)
    {
        var library = libraries.FirstOrDefault(name => machine.Library(name).Loaded);
        return library is not null
            ? Finding.Info(id, Area.Machine, $"{component} ({library}).")
            : Finding.Warning(id, Area.Machine, $"{component} is not installed: {why}.", Install(family, package));
    }

    internal static Finding Clang(IMachine machine, string family) =>
        NodeChecks.OnPath(machine, "clang") is { } clang
            ? Finding.Info(FindingIds.Clang, Area.Machine, $"clang, {clang}.")
            : Finding.Warning(FindingIds.Clang, Area.Machine,
                $"clang is not on PATH: dotnet publish with NativeAOT links with it ({NativeAotPrerequisites}). Building and Debug runs do not need it.",
                Install(family, Package.Clang));

    internal static Finding Zlib(IMachine machine, string family)
    {
        var found = LibraryFolders.SelectMany(folder => new[] { Path.Join(folder, "libz.so"), Path.Join(folder, "libz.a") }).FirstOrDefault(machine.FileExists);
        return found is not null
            ? Finding.Info(FindingIds.Zlib, Area.Machine, $"zlib's development library ({found}).")
            : Finding.Warning(FindingIds.Zlib, Area.Machine,
                $"zlib's development library (libz.so or libz.a) is not installed: dotnet publish with NativeAOT links with it ({NativeAotPrerequisites}).",
                Install(family, Package.Zlib));
    }

    /// <summary><c>debian</c>, <c>fedora</c> or empty, from <c>ID</c> and <c>ID_LIKE</c> in <c>/etc/os-release</c>.</summary>
    internal static string Family(string osRelease)
    {
        var words = new List<string>();
        foreach (var line in osRelease.Split('\n'))
        {
            var parts = line.Trim().Split('=', 2);
            if (parts.Length == 2 && parts[0] is "ID" or "ID_LIKE")
            {
                words.AddRange(parts[1].Trim('"', '\'').Split(' ', StringSplitOptions.RemoveEmptyEntries));
            }
        }

        return words.Contains("debian") || words.Contains("ubuntu") ? "debian" : words.Contains("fedora") ? "fedora" : "";
    }

    /// <summary>The install command for a package on the distribution's family, or the packages' names.</summary>
    internal static string Install(string family, Package package)
    {
        var (apt, dnf) = package switch
        {
            Package.Gtk => ("libgtk-4-1", "gtk4"),
            Package.WebKit => ("libwebkitgtk-6.0-4", "webkitgtk6.0"),
            Package.Sandbox => ("bubblewrap xdg-dbus-proxy", "bubblewrap xdg-dbus-proxy"),
            Package.Icu => ("libicu-dev", "libicu"),
            Package.OpenSsl => ("openssl", "openssl-libs"),
            Package.Clang => ("clang", "clang"),
            Package.Zlib => ("zlib1g-dev", "zlib-devel"),
            _ => ("", ""),
        };
        return family switch
        {
            "debian" => $"Install it with: sudo apt install {apt}",
            "fedora" => $"Install it with: sudo dnf install {dnf}",
            _ => $"Install the distribution's package for it ({apt} on Debian and Ubuntu, {dnf} on Fedora).",
        };
    }

    internal enum Package
    {
        Gtk,
        WebKit,
        Sandbox,
        Icu,
        OpenSsl,
        Clang,
        Zlib,
    }
}
