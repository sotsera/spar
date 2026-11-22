using Spar.Tool.Machine;

namespace Spar.Tool.Doctor;

/// <summary>
/// Node.js versions installed by a version manager, found in each manager's documented folders (their variables first,
/// then their defaults), read only: nvm and nvm-windows, fnm, Volta, mise and asdf. A manager puts its Node.js on
/// <c>PATH</c> only in a shell it set up, so one installed for another shell, or not activated, is invisible to the
/// build; each comes with the fix its manager documents.
/// </summary>
internal static class VersionManagers
{
    /// <summary>One Node.js a manager installed: the manager, the version from its folder, the executable and the fix.</summary>
    internal sealed record Installed(string Manager, Version Version, string Path, string Fix);

    public static IReadOnlyList<Installed> Find(IMachine machine)
    {
        var found = new List<Installed>();
        if (machine.Platform == Platform.Windows)
        {
            NvmWindows(machine, found);
        }
        else
        {
            Nvm(machine, found);
        }

        Fnm(machine, found);
        Volta(machine, found);
        Mise(machine, found);
        if (machine.Platform != Platform.Windows)
        {
            Asdf(machine, found);
        }

        return found;
    }

    /// <summary>nvm (POSIX): <c>$NVM_DIR</c> or <c>~/.nvm</c>, each version in <c>versions/node/v&lt;x&gt;/bin/node</c>.</summary>
    private static void Nvm(IMachine machine, List<Installed> found)
    {
        var root = machine.Variable("NVM_DIR") is { Length: > 0 } dir ? dir : Join(machine.Home, ".nvm");
        Versions(machine, found, "nvm", Join(root, "versions", "node"), version => Join("bin", "node"), version =>
            $"Load nvm in the shell that builds (nvm's installer adds it to ~/.bashrc or ~/.zshrc: export NVM_DIR=\"{root}\"; . \"$NVM_DIR/nvm.sh\"), then run nvm alias default {version.ToString(3)} and open a new terminal.");
    }

    /// <summary>
    /// nvm-windows: <c>%NVM_HOME%</c>, else <c>%APPDATA%\nvm</c> or <c>%LOCALAPPDATA%\nvm</c>, each version in
    /// <c>v&lt;x&gt;\node.exe</c>; the active one is linked at <c>%NVM_SYMLINK%</c>, which its installer puts on PATH.
    /// </summary>
    private static void NvmWindows(IMachine machine, List<Installed> found)
    {
        var roots = new[] { machine.Variable("NVM_HOME"), Join(machine.Variable("APPDATA"), "nvm"), Join(machine.Variable("LOCALAPPDATA"), "nvm") }
            .Where(root => root is { Length: > 0 }).Distinct(StringComparer.OrdinalIgnoreCase);
        var link = machine.Variable("NVM_SYMLINK") is { Length: > 0 } symlink ? symlink : "the NVM_SYMLINK folder";
        foreach (var root in roots)
        {
            Versions(machine, found, "nvm-windows", root!, _ => "node.exe", version =>
                $"Run nvm use {version.ToString(3)} (it links the version at {link}, which nvm-windows puts on PATH), then open a new terminal.");
        }
    }

    /// <summary>
    /// fnm: <c>$FNM_DIR</c>, else its default data folder (<c>%APPDATA%\fnm</c>, <c>$XDG_DATA_HOME/fnm</c> or
    /// <c>~/.local/share/fnm</c>, and the older <c>~/.fnm</c>), each version in <c>node-versions/v&lt;x&gt;/installation</c>.
    /// </summary>
    private static void Fnm(IMachine machine, List<Installed> found)
    {
        var windows = machine.Platform == Platform.Windows;
        string?[] candidates = windows
            ? [machine.Variable("FNM_DIR"), Join(machine.Variable("APPDATA"), "fnm")]
            : [machine.Variable("FNM_DIR"), Join(machine.Variable("XDG_DATA_HOME"), "fnm"), Join(machine.Home, ".local", "share", "fnm"), Join(machine.Home, ".fnm")];
        var shell = windows
            ? "fnm env --use-on-cd --shell powershell | Out-String | Invoke-Expression in your PowerShell profile"
            : "eval \"$(fnm env --use-on-cd)\" in ~/.bashrc or ~/.zshrc";
        foreach (var root in candidates.Where(c => c is { Length: > 0 }).Distinct())
        {
            Versions(machine, found, "fnm", Join(root, "node-versions"), _ => windows ? Join("installation", "node.exe") : Join("installation", "bin", "node"), version =>
                $"Set fnm up in the shell that builds ({shell}), run fnm default {version.ToString(3)}, then open a new terminal.");
        }
    }

    /// <summary>Volta: <c>$VOLTA_HOME</c>, else <c>%LOCALAPPDATA%\Volta</c> or <c>~/.volta</c>, each version in <c>tools/image/node/&lt;x&gt;</c>.</summary>
    private static void Volta(IMachine machine, List<Installed> found)
    {
        var windows = machine.Platform == Platform.Windows;
        var root = machine.Variable("VOLTA_HOME") is { Length: > 0 } home ? home : windows ? Join(machine.Variable("LOCALAPPDATA"), "Volta") : Join(machine.Home, ".volta");
        var bin = windows ? "Volta's bin folder" : Join(root, "bin");
        Versions(machine, found, "Volta", Join(root, "tools", "image", "node"), _ => windows ? "node.exe" : Join("bin", "node"), version =>
            $"Put {bin} on PATH (Volta's installer does), run volta install node@{version.ToString(3)}, then open a new terminal.");
    }

    /// <summary>
    /// mise: <c>$MISE_DATA_DIR</c>, else <c>%LOCALAPPDATA%\mise</c>, <c>$XDG_DATA_HOME/mise</c> or
    /// <c>~/.local/share/mise</c>, each version in <c>installs/node/&lt;x&gt;</c>.
    /// </summary>
    private static void Mise(IMachine machine, List<Installed> found)
    {
        var windows = machine.Platform == Platform.Windows;
        var root = machine.Variable("MISE_DATA_DIR") is { Length: > 0 } data
            ? data
            : windows ? Join(machine.Variable("LOCALAPPDATA"), "mise")
            : machine.Variable("XDG_DATA_HOME") is { Length: > 0 } xdg ? Join(xdg, "mise") : Join(machine.Home, ".local", "share", "mise");
        Versions(machine, found, "mise", Join(root, "installs", "node"), _ => windows ? "node.exe" : Join("bin", "node"), version =>
            $"Activate mise in the shell that builds (eval \"$(mise activate bash)\", or its shims folder {Join(root, "shims")} on PATH), run mise use -g node@{version.ToString(3)}, then open a new terminal.");
    }

    /// <summary>asdf: <c>$ASDF_DATA_DIR</c> or <c>~/.asdf</c>, each version in <c>installs/nodejs/&lt;x&gt;/bin/node</c>.</summary>
    private static void Asdf(IMachine machine, List<Installed> found)
    {
        var root = machine.Variable("ASDF_DATA_DIR") is { Length: > 0 } dir ? dir : Join(machine.Home, ".asdf");
        Versions(machine, found, "asdf", Join(root, "installs", "nodejs"), _ => Join("bin", "node"), version =>
            $"Put asdf's shims folder ({Join(root, "shims")}) on PATH as asdf's guide says, run asdf set --home nodejs {version.ToString(3)} (asdf global nodejs {version.ToString(3)} before asdf 0.16), then open a new terminal.");
    }

    /// <summary>Each version folder of <paramref name="folder"/> whose name is a version and that has its executable.</summary>
    private static void Versions(IMachine machine, List<Installed> found, string manager, string folder, Func<Version, string> executable, Func<Version, string> fix)
    {
        foreach (var directory in machine.Directories(folder))
        {
            if (NodeChecks.ParseVersion(System.IO.Path.GetFileName(directory)) is not { } version)
            {
                continue;
            }

            var node = Join(directory, executable(version));
            if (machine.FileExists(node))
            {
                found.Add(new Installed(manager, version, node, fix(version)));
            }
        }
    }

    private static string Join(string? first, params string[] rest) =>
        string.IsNullOrEmpty(first) ? "" : System.IO.Path.Join([first, .. rest]);
}
