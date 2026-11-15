using System.Diagnostics;
using System.Text;

[assembly: AssemblyFixture(typeof(Spar.PackageTests.Consumer))]

namespace Spar.PackageTests;

/// <summary>
/// Spar packed twice, at two versions no build has used, into a feed of this run's own; and a consumer that uses Spar
/// from that feed alone (decisions "Package-only consumer" and "Packages for the package-only consumer"): a folder
/// outside any repository whose path has a space and non-ASCII letters, its own NuGet package folder, a nuget.config
/// that maps Spar's packages to the feed, and its own MSBuild handshake and compiler server. NuGet's HTTP cache is kept
/// apart from the user's and shared across runs, so only the first run downloads. A run that passes deletes its
/// folder; one that fails keeps it, and the next run deletes it.
/// </summary>
public sealed class Consumer : IAsyncLifetime
{
    private static readonly string Base = Path.Combine(Path.GetTempPath(), "spar package tests");

    private static readonly string Run = DateTime.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);

    private int _passed;
    private int _failed;

    public string Repository { get; } = FindRepository();

    public string RunRoot => Path.Combine(Base, Run);

    /// <summary>The consumer's own folder; a space and non-ASCII letters, as a user's documents folder can have.</summary>
    public string Root => Path.Combine(RunRoot, "Nötes Äpp");

    public string Feed => Path.Combine(RunRoot, "feed");

    /// <summary>The first version packed; the consumer starts on it.</summary>
    public string First { get; } = $"0.1.0-dev.{Run}.1";

    /// <summary>A newer version of the same sources, for the update and the downgrade.</summary>
    public string Second { get; } = $"0.1.0-dev.{Run}.2";

    /// <summary>The environment every consumer process gets: isolated NuGet folders and MSBuild nodes.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; private set; } = new Dictionary<string, string>();

    public async ValueTask InitializeAsync()
    {
        DeleteEarlierRuns();
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Feed);
        RequireNoRepositoryAbove(Root);
        Assert.True(File.Exists(Path.Combine(Repository, "src", "Spar.Client", "obj", "dist", "sdk.js")),
            "The TypeScript SDK is not built: run 'npm ci --ignore-scripts' and 'npm run build' in src/Spar.Client.");

        var salt = Guid.NewGuid().ToString("N");
        Environment = new Dictionary<string, string>
        {
            ["NUGET_PACKAGES"] = Path.Combine(RunRoot, "nuget-packages"),
            ["NUGET_HTTP_CACHE_PATH"] = Path.Combine(Base, "http-cache"),
            ["MSBUILDNODEHANDSHAKESALT"] = salt,
            ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "false",

            // No node outlives a command (dotnet test passes -nr:false on to the test application, so it is set here).
            ["MSBUILDDISABLENODEREUSE"] = "1",

            // A fresh app has no committed contract snapshot; under CI's setting its build would verify one (SPAR100).
            ["ContinuousIntegrationBuild"] = "false",
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_NOLOGO"] = "1",
            ["SharedCompilationId"] = $"spar-package-tests-{salt}",
        };

        // Packing must write only under bin/ and obj/: the working tree reads the same before and after.
        var before = await WorkingTreeAsync();
        await PackAsync(First);
        await PackAsync(Second);
        Assert.Equal(before, await WorkingTreeAsync());
        ConsumerFiles.Write(Root, Feed, First, RepositorySdkVersion());
    }

    public ValueTask DisposeAsync()
    {
        if (_passed > 0 && _failed == 0)
        {
            TryDelete(RunRoot);
        }

        return ValueTask.CompletedTask;
    }

    public void Report(bool passed)
    {
        if (passed)
        {
            Interlocked.Increment(ref _passed);
        }
        else
        {
            Interlocked.Increment(ref _failed);
        }
    }

    /// <summary>Points the consumer's global.json at a version of Spar.Sdk, as an update or a downgrade would.</summary>
    public void UseSdk(string version) => ConsumerFiles.WriteGlobalJson(Root, version, RepositorySdkVersion());

    /// <summary>Runs <c>dotnet</c> in the consumer with its isolated environment, from <paramref name="directory"/> (relative to the root).</summary>
    public Task<(int Exit, string Output)> DotnetAsync(string directory, params string[] arguments) =>
        Tool.RunAsync(Path.Combine(Root, directory), "dotnet", arguments, Environment);

    /// <summary>
    /// Packs Spar's five packages at <paramref name="version"/> into the feed, from this repository with its own build
    /// settings: the MSBuild server off and no node reuse, so a pack never shares a node with the consumer.
    /// </summary>
    private async Task PackAsync(string version)
    {
        string[] projects = ["Spar", "Spar.Loopback", "Spar.Testing", "Spar.Testing.Browser", "Spar.Sdk"];
        var suffix = version["0.1.0-".Length..];
        foreach (var project in projects)
        {
            var path = Path.Combine(Repository, "src", project, project + ".csproj");
            var (exit, output) = await Tool.RunAsync(Repository, "dotnet",
                ["pack", path, "-c", "Release", $"-p:VersionSuffix={suffix}", "-o", Feed, "-nologo", "-nr:false", "-v:q"],
                new Dictionary<string, string> { ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "false" });
            Assert.True(exit == 0, $"dotnet pack {project} {version}:\n{output}");
        }
    }

    private async Task<string> WorkingTreeAsync()
    {
        var (exit, output) = await Tool.RunAsync(Repository, "git", ["status", "--porcelain", "--untracked-files=all"], new Dictionary<string, string>());
        Assert.True(exit == 0, output);
        return output;
    }

    private string RepositorySdkVersion()
    {
        var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(Repository, "global.json")));
        return json.RootElement.GetProperty("sdk").GetProperty("version").GetString()!;
    }

    /// <summary>No folder above the consumer may hold what would change its build: it must see Spar's packages alone.</summary>
    private static void RequireNoRepositoryAbove(string folder)
    {
        string[] influences = [".git", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "global.json", "nuget.config", "NuGet.Config"];
        for (var directory = new DirectoryInfo(folder).Parent; directory is not null; directory = directory.Parent)
        {
            var found = influences.FirstOrDefault(name => Path.Exists(Path.Combine(directory.FullName, name)));
            Assert.True(found is null, $"'{Path.Combine(directory.FullName, found ?? "")}' sits above the consumer and would change its build.");
        }
    }

    /// <summary>Removes earlier runs' folders, keeping the shared HTTP cache.</summary>
    private static void DeleteEarlierRuns()
    {
        if (!Directory.Exists(Base))
        {
            return;
        }

        foreach (var earlier in Directory.EnumerateDirectories(Base).Where(d => Path.GetFileName(d) != "http-cache"))
        {
            TryDelete(earlier);
        }
    }

    private static void TryDelete(string directory)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(directory, recursive: true);
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            TestContext.Current.SendDiagnosticMessage($"Could not delete {directory}: {problem.Message}");
        }
    }

    private static string FindRepository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Spar.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("These tests run from a build inside the Spar repository.");
    }
}

/// <summary>Runs a tool and collects what it wrote.</summary>
public static class Tool
{
    public static async Task<(int Exit, string Output)> RunAsync(string directory, string tool, string[] arguments, IReadOnlyDictionary<string, string> environment)
    {
        var start = new ProcessStartInfo(tool)
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in environment)
        {
            start.Environment[name] = value;
        }

        using var process = Process.Start(start)!;
        var output = new StringBuilder();
        process.OutputDataReceived += (_, line) => Append(output, line.Data);
        process.ErrorDataReceived += (_, line) => Append(output, line.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromMinutes(20));
        process.WaitForExit();
        lock (output)
        {
            return (process.ExitCode, output.ToString());
        }
    }

    private static void Append(StringBuilder output, string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (output)
        {
            output.AppendLine(line);
        }
    }
}
