using System.Globalization;
using System.Text.RegularExpressions;

namespace Spar.BuildTests;

/// <summary>One isolated build, its binary log, and what the log says about the Spar steps and the compilations.</summary>
public sealed record BuildResult(
    int Exit,
    string Output,
    TimeSpan Elapsed,
    IReadOnlyDictionary<string, bool> SparTargets,
    IReadOnlyDictionary<string, int> Compilations)
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

        var log = await ReplayAsync(workspace, label, binlog);
        return new BuildResult(exit, output, elapsed, SparTargets(log), Compilations(log));
    }

    /// <summary>
    /// The binary log replayed at diagnostic verbosity. To read a run's log again:
    /// <c>dotnet msbuild &lt;label&gt;.binlog -flp:v=diag</c>, or open it in a binary-log viewer.
    /// </summary>
    private static async Task<string> ReplayAsync(Workspace workspace, string label, string binlog)
    {
        var replay = Path.Combine(workspace.Logs, $"{label}.log");
        await Tool.RunAsync(workspace.Root, "dotnet", "msbuild", binlog, "-noconlog", $"-flp:v=diag;logfile={replay}");
        var text = File.ReadAllText(replay);

        // The replay is 20-40 MB per build and the binary log (under 1 MB) holds the same, so only the log stays.
        File.Delete(replay);
        return text;
    }

    /// <summary>For each Spar target, whether it ran.</summary>
    private static Dictionary<string, bool> SparTargets(string log)
    {
        var targets = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (Match match in Step().Matches(log))
        {
            var name = match.Groups["name"].Value;
            var ran = match.Groups["verb"].Value == "Building";
            targets[name] = targets.GetValueOrDefault(name) || ran;
        }

        return targets;
    }

    /// <summary>
    /// For each project, by full path, how many of its instances entered <c>CoreCompile</c>. A target runs once per
    /// instance, so a project built with two sets of global properties counts two, and the two share its obj/.
    /// </summary>
    private static Dictionary<string, int> Compilations(string log)
    {
        var compilations = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Compile().Matches(log))
        {
            var project = match.Groups["project"].Value;
            compilations[project] = compilations.GetValueOrDefault(project) + 1;
        }

        return compilations;
    }

    [GeneratedRegex("(?<verb>Building|Skipping) target \"(?<name>Spar[A-Za-z]+)\"")]
    private static partial Regex Step();

    // Builds on several nodes name the target "CoreCompile: (TargetId:N)".
    [GeneratedRegex("Target \"CoreCompile(: \\(TargetId:\\d+\\))?\" in file \"[^\"]*\" from project \"(?<project>[^\"]+)\"")]
    private static partial Regex Compile();
}
