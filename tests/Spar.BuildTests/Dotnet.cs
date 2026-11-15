using System.Globalization;
using System.Text.RegularExpressions;

namespace Spar.BuildTests;

/// <summary>One isolated build, its binary log, and the Spar steps the log says ran or were skipped.</summary>
public sealed record BuildResult(int Exit, string Output, TimeSpan Elapsed, IReadOnlyDictionary<string, bool> SparTargets)
{
    /// <summary>The Spar targets that ran (true) or were skipped as up to date (false).</summary>
    public IEnumerable<string> Ran => SparTargets.Where(t => t.Value).Select(t => t.Key).Order(StringComparer.Ordinal);
}

/// <summary>
/// <c>dotnet build</c> and <c>dotnet publish</c> isolated from other builds on the machine (plan section 5, issue H):
/// no MSBuild server, no node reuse, a node handshake and a compiler server of their own.
/// </summary>
public static partial class Dotnet
{
    private static readonly string Salt = Guid.NewGuid().ToString("N");

    public static async Task<BuildResult> RunAsync(Workspace workspace, string label, string verb, string project, params string[] arguments)
    {
        var binlog = Path.Combine(workspace.Logs, $"{label}.binlog");
        string[] isolated =
        [
            verb, project, "-nologo", "-nr:false", $"-bl:{binlog}",
            $"-p:SharedCompilationId=spar-build-tests-{Salt}",
            .. arguments,
        ];

        Environment.SetEnvironmentVariable("DOTNET_CLI_USE_MSBUILD_SERVER", "false");
        Environment.SetEnvironmentVariable("MSBUILDNODEHANDSHAKESALT", Salt);
        var (exit, output, elapsed) = await Tool.RunAsync(workspace.Root, "dotnet", isolated);
        var timing = string.Create(CultureInfo.InvariantCulture, $"{label}: {elapsed.TotalSeconds:0.00} s, exit {exit}\n");
        File.AppendAllText(Path.Combine(workspace.Logs, "timings.txt"), timing);
        return new BuildResult(exit, output, elapsed, await SparTargetsAsync(workspace, label, binlog));
    }

    /// <summary>Replays the binary log at diagnostic verbosity and reads, for each Spar target, whether it ran.</summary>
    private static async Task<IReadOnlyDictionary<string, bool>> SparTargetsAsync(Workspace workspace, string label, string binlog)
    {
        var replay = Path.Combine(workspace.Logs, $"{label}.log");
        await Tool.RunAsync(workspace.Root, "dotnet", "msbuild", binlog, "-noconlog", $"-flp:v=diag;logfile={replay}");
        var targets = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (Match match in Step().Matches(File.ReadAllText(replay)))
        {
            var name = match.Groups["name"].Value;
            var ran = match.Groups["verb"].Value == "Building";
            targets[name] = targets.GetValueOrDefault(name) || ran;
        }

        return targets;
    }

    [GeneratedRegex("(?<verb>Building|Skipping) target \"(?<name>Spar[A-Za-z]+)\"")]
    private static partial Regex Step();
}
