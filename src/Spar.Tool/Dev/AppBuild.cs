using System.Diagnostics;
using System.Text.Json;
using Spar.Tool.Machine;

namespace Spar.Tool.Dev;

/// <summary>What one build came to: its diagnostics, its time, and where its executable is when it succeeded.</summary>
internal sealed record BuildResult(bool Succeeded, int ExitCode, TimeSpan Duration, IReadOnlyList<BuildDiagnostic> Diagnostics, string? Executable, string? OutputDirectory)
{
    public IEnumerable<BuildDiagnostic> Errors => Diagnostics.Where(diagnostic => diagnostic.Severity == "error");

    public IEnumerable<BuildDiagnostic> Warnings => Diagnostics.Where(diagnostic => diagnostic.Severity == "warning");
}

/// <summary>
/// The app's Development build as <c>dotnet build</c> makes it, in the job or group of the session: the ordinary output
/// in <c>bin/</c>, so the IDE's builds and <c>spar dev</c>'s are the same. Its errors and warnings come from MSBuild's own
/// file loggers (<c>ErrorsOnly</c>, <c>WarningsOnly</c>) under <c>obj/spar-dev/</c>, its executable from
/// <c>-getProperty</c>. The build's MSBuild nodes and compiler server are the session's own
/// (<c>MSBUILDNODEHANDSHAKESALT</c>, <c>SharedCompilationId</c>): warm from one reload to the next, never shared with an
/// IDE's build, and ended with the session's job.
/// </summary>
internal sealed class AppBuild(IChildProcesses children, string projectFile, string session, Action<string> report)
{
    private const int KeptOutputLines = 200;

    private readonly string _folder = Path.Combine(Path.GetDirectoryName(projectFile)!, "obj", "spar-dev");

    /// <summary>
    /// Builds the project, or with <paramref name="build"/> false only reads where its last build put the executable
    /// (<c>spar snap --no-build</c>), which then must exist.
    /// </summary>
    public async Task<BuildResult> RunAsync(CancellationToken cancellationToken, bool build = true)
    {
        Directory.CreateDirectory(_folder);
        var errorsFile = Path.Combine(_folder, "build-errors.log");
        var warningsFile = Path.Combine(_folder, "build-warnings.log");
        var outputFile = Path.Combine(_folder, "build-output.log");
        File.Delete(errorsFile);
        File.Delete(warningsFile);

        var output = new List<string>();
        var dotnet = Processes.Resolve("dotnet") ?? throw new InvalidOperationException("dotnet is not on PATH.");
        var start = new ChildStart(
            dotnet,
            [
                "build", projectFile, "-c", "Debug", "-nologo", build ? "-t:Build" : "-t:GetTargetPath", build ? "-restore" : "--no-restore",
                "-getProperty:RunCommand", "-getProperty:RunArguments", "-getProperty:TargetDir",
                $"-p:SharedCompilationId=spar-dev-{session}",
                $"-flp1:LogFile={errorsFile};ErrorsOnly;NoSummary;ForceNoAlign",
                $"-flp2:LogFile={warningsFile};WarningsOnly;NoSummary;ForceNoAlign",
            ],
            Path.GetDirectoryName(projectFile)!,
            new Dictionary<string, string?>
            {
                ["MSBUILDNODEHANDSHAKESALT"] = "spar-dev-" + session,
                ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
                ["DOTNET_NOLOGO"] = "1",
                ["MSBUILDTERMINALLOGGER"] = "off",
            });

        report("building " + Path.GetFileName(projectFile) + " (Debug)…");
        var watch = Stopwatch.StartNew();
        using var child = children.Start(start, (line, _) =>
        {
            lock (output)
            {
                output.Add(line);
            }
        });
        var exitCode = await child.Exited.WaitAsync(cancellationToken);
        watch.Stop();

        // The output pipes end a moment after the exit; MSBuild's nodes outlive the build and hold no copy of them.
        await Task.Delay(50, CancellationToken.None);
        string[] lines;
        lock (output)
        {
            lines = [.. output];
        }

        File.WriteAllLines(outputFile, lines.Length > KeptOutputLines ? lines[^KeptOutputLines..] : lines);
        var projectDirectory = Path.GetDirectoryName(projectFile)!;
        var diagnostics = Tidy(BuildDiagnostics.Parse(ReadLines(errorsFile).Concat(ReadLines(warningsFile))), projectDirectory);
        if (exitCode != 0 && !diagnostics.Any(diagnostic => diagnostic.Severity == "error"))
        {
            // A failure MSBuild did not report as a diagnostic (dotnet itself, a crashed node): its last lines say why.
            var last = string.Join(" | ", lines.Where(line => line.Length > 0).TakeLast(5));
            diagnostics = [.. diagnostics, new BuildDiagnostic("error", "", $"dotnet build exited with {exitCode}: {last}", null, null, null, null)];
        }

        var (executable, directory) = exitCode == 0 ? Properties(lines) : (null, null);
        if (!build && executable is not null && !File.Exists(executable))
        {
            diagnostics = [.. diagnostics, new BuildDiagnostic("error", "", $"There is no Debug build at {executable}: build the project, or leave out --no-build.", null, null, null, null)];
            executable = null;
        }

        return new BuildResult(exitCode == 0 && executable is not null, exitCode, watch.Elapsed, diagnostics, executable, directory);
    }

    /// <summary>The executable and its folder from <c>-getProperty</c>'s JSON, which MSBuild writes as its own block.</summary>
    internal static (string? Executable, string? Directory) Properties(IReadOnlyList<string> lines)
    {
        var start = -1;
        for (var index = 0; index < lines.Count; index++)
        {
            if (lines[index] == "{")
            {
                start = index;
            }
            else if (lines[index] == "}" && start >= 0)
            {
                var json = string.Join('\n', lines.Skip(start).Take(index - start + 1));
                return Read(json);
            }
        }

        return (null, null);
    }

    private static (string?, string?) Read(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("Properties", out var properties))
            {
                return (null, null);
            }

            string? Text(string name) => properties.TryGetProperty(name, out var value) && value.GetString() is { Length: > 0 } text ? text : null;
            return (Text("RunCommand"), Text("TargetDir"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    /// <summary>
    /// A relative file made full against the project's folder, where a tool run from it reports (the type-check); and
    /// an <c>Exec</c> failure (MSB3073) left out when the tool it ran reported its own errors, which say more.
    /// </summary>
    internal static IReadOnlyList<BuildDiagnostic> Tidy(IReadOnlyList<BuildDiagnostic> diagnostics, string projectDirectory)
    {
        var others = diagnostics.Any(diagnostic => diagnostic.Severity == "error" && diagnostic.Code != "MSB3073");
        var tidy = new List<BuildDiagnostic>(diagnostics.Count);
        foreach (var diagnostic in diagnostics)
        {
            if (others && diagnostic.Code == "MSB3073")
            {
                continue;
            }

            var file = diagnostic.File is { } relative && !Path.IsPathFullyQualified(relative)
                ? Path.GetFullPath(Path.Combine(projectDirectory, relative))
                : diagnostic.File;
            tidy.Add(diagnostic with { File = file });
        }

        return tidy;
    }

    private static IEnumerable<string> ReadLines(string file) => File.Exists(file) ? File.ReadAllLines(file) : [];
}
