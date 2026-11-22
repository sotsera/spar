using Spar.Tool.Machine;
using Spar.Tool.Project;

namespace Spar.Tool.Doctor;

/// <summary>
/// The .NET side of the machine: the SDK the app's <c>global.json</c> asks for, as the .NET host resolves it
/// (<c>dotnet --version</c> in the folder), and <c>Spar.Sdk</c> at the version <c>msbuild-sdks</c> names, in NuGet's
/// package folder, against the tool's own version.
/// </summary>
internal static class DotnetChecks
{
    public const string SdkDownload = "https://dotnet.microsoft.com/download/dotnet";

    public static IEnumerable<Finding> Check(IMachine machine, string folder, GlobalJson? globalJson, string toolVersion)
    {
        yield return Sdk(machine, folder, globalJson);
        if (globalJson is null)
        {
            yield break;
        }

        foreach (var finding in SparSdk(machine, folder, globalJson, toolVersion))
        {
            yield return finding;
        }
    }

    internal static Finding Sdk(IMachine machine, string folder, GlobalJson? globalJson)
    {
        var dotnet = machine.Variable("DOTNET_HOST_PATH") is { Length: > 0 } host ? host : "dotnet";
        var run = machine.Run(dotnet, ["--version"], folder);
        var asked = globalJson?.SdkVersion is { } version
            ? $" (global.json asks for {version}{(globalJson.RollForward is { } roll ? $", rollForward {roll}" : "")})"
            : "";
        if (run.Succeeded)
        {
            return Finding.Info(FindingIds.DotnetSdk, Area.Machine, $".NET SDK {run.Output.Trim()}{asked}.");
        }

        if (!run.Started)
        {
            return Finding.Error(FindingIds.DotnetSdk, Area.Machine, $"dotnet could not be started: {run.Error.Trim()}",
                $"Install the .NET SDK ({SdkDownload}) and put dotnet on PATH.");
        }

        var said = FirstLines(run.Error.Length > 0 ? run.Error : run.Output, 3);
        var wanted = globalJson?.SdkVersion is { } sdk ? $"the .NET SDK {sdk} or a version global.json's rollForward accepts" : "a .NET SDK";
        var major = globalJson?.SdkVersion?.Split('.') is [var first, var second, ..] ? $"/{first}.{second}" : "";
        return Finding.Error(FindingIds.DotnetSdk, Area.Machine, $"The .NET host found no SDK for this folder{asked}: {said}",
            $"Install {wanted} ({SdkDownload}{major}), or change sdk.version in {globalJson?.File ?? "global.json"}.");
    }

    internal static IEnumerable<Finding> SparSdk(IMachine machine, string folder, GlobalJson globalJson, string toolVersion)
    {
        if (globalJson.Problem is { } problem)
        {
            yield return Finding.Error(FindingIds.SparSdk, Area.Machine, $"{globalJson.File} cannot be read: {problem}",
                "Fix the file: the .NET host and MSBuild read it before anything builds.");
            yield break;
        }

        if (globalJson.SparSdkVersion is not { Length: > 0 } version)
        {
            yield return Finding.Error(FindingIds.SparSdk, Area.Machine, $"{globalJson.File} names no Spar.Sdk version in msbuild-sdks.",
                $"Add \"msbuild-sdks\": {{ \"Spar.Sdk\": \"{Plain(toolVersion)}\" }} to it: <Project Sdk=\"Spar.Sdk\"> resolves the version there.");
            yield break;
        }

        var packages = PackagesFolder(machine, folder);
        var sdkProps = packages is null ? null : Path.Combine(packages, "spar.sdk", version.ToLowerInvariant(), "Sdk", "Sdk.props");
        if (sdkProps is not null && machine.FileExists(sdkProps))
        {
            yield return Finding.Info(FindingIds.SparSdk, Area.Machine, $"Spar.Sdk {version}, from {Path.GetDirectoryName(Path.GetDirectoryName(sdkProps))}.");
        }
        else
        {
            var where = packages is null ? "NuGet's package folder" : packages;
            yield return Finding.Info(FindingIds.SparSdk, Area.Machine,
                $"Spar.Sdk {version} (global.json) is not in {where} yet: the next build or restore downloads it from the NuGet sources in nuget.config.");
        }

        // 0.0.0-source is Spar.Sdk from Spar's own source tree (eng/source-sdk), which no packed tool matches.
        if (version.EndsWith("-source", StringComparison.Ordinal))
        {
            yield return Finding.Info(FindingIds.ToolVersion, Area.Machine, $"Spar.Sdk {version} is Spar's source tree: the tool's version ({Plain(toolVersion)}) is not compared with it.");
        }
        else if (!string.Equals(Plain(toolVersion), version, StringComparison.OrdinalIgnoreCase))
        {
            yield return Finding.Warning(FindingIds.ToolVersion, Area.Machine,
                $"This spar tool is {Plain(toolVersion)} and the app builds with Spar.Sdk {version}: the tool checks what its own version expects.",
                $"Use the tool at the SDK's version: dotnet tool update Spar.Tool --version {version} (the app's .config/dotnet-tools.json).");
        }
    }

    /// <summary>NuGet's global package folder: <c>NUGET_PACKAGES</c>, else what <c>dotnet nuget locals</c> says for the folder.</summary>
    internal static string? PackagesFolder(IMachine machine, string folder)
    {
        if (machine.Variable("NUGET_PACKAGES") is { Length: > 0 } variable)
        {
            return variable;
        }

        var dotnet = machine.Variable("DOTNET_HOST_PATH") is { Length: > 0 } host ? host : "dotnet";
        var run = machine.Run(dotnet, ["nuget", "locals", "global-packages", "--list"], folder);
        if (!run.Succeeded)
        {
            return null;
        }

        // "global-packages: C:\Users\…\.nuget\packages\"
        var line = run.Output.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("global-packages:", StringComparison.Ordinal));
        return line?["global-packages:".Length..].Trim().TrimEnd('\\', '/');
    }

    /// <summary>A version without its build metadata: <c>0.1.0-dev.3+4f2c…</c> is <c>0.1.0-dev.3</c>.</summary>
    public static string Plain(string version) => version.Split('+')[0];

    private static string FirstLines(string text, int count) =>
        string.Join(" ", text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Take(count));
}
