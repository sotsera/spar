using Spar.Tool.Machine;

namespace Spar.Tool.Doctor;

/// <summary>
/// Node.js and npm, which the build (the frontend's install, type-check and Vite build) and a Debug run (Vite's
/// development server) need on <c>PATH</c>, as the build and the host look for them. Without them on <c>PATH</c>, the
/// version managers' own folders are searched, so a Node.js that nvm, nvm-windows, fnm, Volta, mise or asdf installed
/// but that this shell does not see is named with the way to put it on <c>PATH</c>.
/// </summary>
internal static class NodeChecks
{
    public const string NodeDownload = "https://nodejs.org/en/download";

    /// <summary>The oldest Node.js that Vite 8 runs on: 20.19 in the 20 line, 22.12 after it.</summary>
    public static readonly Version ViteMinimum20 = new(20, 19, 0);

    public static readonly Version ViteMinimum22 = new(22, 12, 0);

    /// <summary>The Node.js line Spar is built and tested with, which the getting-started guide asks for.</summary>
    public const int Recommended = 24;

    public static IEnumerable<Finding> Check(IMachine machine)
    {
        var windows = machine.Platform == Platform.Windows;
        var node = OnPath(machine, windows ? "node.exe" : "node");
        if (node is null)
        {
            var installed = VersionManagers.Find(machine);
            yield return NotOnPath(installed);
            foreach (var found in installed)
            {
                yield return Finding.Info(FindingIds.NodeVersionManager, Area.Machine, $"Node.js {found.Version} installed by {found.Manager}: {found.Path}.");
            }

            yield break;
        }

        yield return CheckVersion(machine, node);

        var npm = OnPath(machine, windows ? "npm.cmd" : "npm");
        if (npm is null)
        {
            yield return Finding.Error(FindingIds.Npm, Area.Machine, $"Node.js is on PATH ({node}) but npm is not.",
                $"Install Node.js again with npm ({NodeDownload}): the build runs npm ci and a Debug run npm run dev.");
            yield break;
        }

        var run = machine.Run(npm, ["--version"], timeout: TimeSpan.FromSeconds(60));
        yield return run.Succeeded
            ? Finding.Info(FindingIds.Npm, Area.Machine, $"npm {run.Output.Trim()}, {npm}.")
            : Finding.Error(FindingIds.Npm, Area.Machine, $"{npm} --version failed: {(run.Error + run.Output).Trim()}",
                $"Install Node.js again with npm ({NodeDownload}).");
    }

    internal static Finding CheckVersion(IMachine machine, string node)
    {
        var run = machine.Run(node, ["--version"]);
        if (!run.Succeeded || ParseVersion(run.Output) is not { } version)
        {
            return Finding.Error(FindingIds.Node, Area.Machine, $"{node} --version failed: {(run.Error + run.Output).Trim()}",
                $"Install Node.js {Recommended} or later ({NodeDownload}).");
        }

        var runsVite = version.Major == 20 ? version >= ViteMinimum20 : version >= ViteMinimum22;
        if (!runsVite)
        {
            return Finding.Error(FindingIds.Node, Area.Machine, $"Node.js {version} ({node}) is older than Vite 8 runs on (20.19, or 22.12 and later).",
                $"Install Node.js {Recommended} or later ({NodeDownload}).");
        }

        if (version.Major < Recommended)
        {
            return Finding.Warning(FindingIds.Node, Area.Machine, $"Node.js {version} ({node}) runs Vite 8, but Spar is built and tested with Node.js {Recommended}.",
                $"Install Node.js {Recommended} or later ({NodeDownload}).");
        }

        return Finding.Info(FindingIds.Node, Area.Machine, $"Node.js {version}, {node}.");
    }

    internal static Finding NotOnPath(IReadOnlyList<VersionManagers.Installed> installed)
    {
        if (installed.Count == 0)
        {
            return Finding.Error(FindingIds.Node, Area.Machine, "Node.js is not on PATH, and no version manager's Node.js was found.",
                $"Install Node.js {Recommended} or later, which comes with npm ({NodeDownload}), then open a new terminal.");
        }

        var newest = installed.OrderByDescending(i => i.Version).First();
        return Finding.Error(FindingIds.Node, Area.Machine,
            $"Node.js is not on PATH, but {newest.Manager} installed Node.js {newest.Version} ({newest.Path}).",
            newest.Fix);
    }

    /// <summary>The first <paramref name="name"/> in a folder of <c>PATH</c>, as the build and the host find it.</summary>
    internal static string? OnPath(IMachine machine, string name)
    {
        var separator = machine.Platform == Platform.Windows ? ';' : ':';
        foreach (var folder in (machine.Variable("PATH") ?? "").Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Join(folder.Trim('"'), name);
            if (machine.FileExists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary><c>v24.1.0</c> or <c>24.1.0</c> as a version; null when it is neither.</summary>
    internal static Version? ParseVersion(string text)
    {
        var trimmed = text.Trim().TrimStart('v');
        return System.Version.TryParse(trimmed, out var version) ? version : null;
    }
}
