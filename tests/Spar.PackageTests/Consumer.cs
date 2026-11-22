using System.Diagnostics;
using System.Text;

[assembly: AssemblyFixture(typeof(Spar.PackageTests.Consumer))]

namespace Spar.PackageTests;

/// <summary>
/// Spar packed twice, at two versions no build has used, into a feed of this run's own; and consumers that use Spar
/// from that feed alone (decisions "Package-only consumer" and "Packages for the package-only consumer"), the main one a
/// new app made by <c>dotnet new spar-app</c> from the packed template (plan, M1d step 6), installed into a template hive
/// of this run's own, never the user's. Each is a folder
/// outside any repository whose path has a space and non-ASCII letters, a nuget.config that maps Spar's packages to
/// the feed, and its own MSBuild handshake and compiler server. NuGet's package folder and HTTP cache are kept apart
/// from the user's and shared across runs, so only the first run downloads: the HTTP cache alone is not enough, since
/// NuGet downloads again any entry older than 30 minutes. Spar's packages cannot go stale there, because every run
/// packs new versions, and each run deletes the earlier runs' ones. A run that passes deletes its folder; one that
/// fails keeps it, and the next run deletes it.
/// </summary>
public sealed class Consumer : IAsyncLifetime
{
    // No space here: NativeAOT's Linux link step splits paths into the package folder at spaces. The consumer's own folder
    // keeps its space and non-ASCII letters (<see cref="Root"/>).
    private static readonly string Base = Path.Combine(Path.GetTempPath(), "spar-package-tests");

    /// <summary>The package folder every run shares, under <see cref="Base"/>.</summary>
    private const string PackagesFolder = "nuget-packages";

    /// <summary>The folders under <see cref="Base"/> that outlive a run.</summary>
    private static readonly string[] Shared = [PackagesFolder, "http-cache", "npm-cache"];

    private static readonly string Run = DateTime.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);

    private int _passed;
    private int _failed;
    private Task<(int Exit, string Output)>? _first;
    private Task<(int Exit, string Output, string Executable)>? _published;

    /// <summary>The name the template's app gets in the main consumer (<c>dotnet new spar-app -n Notes</c>).</summary>
    public const string App = "Notes";

    /// <summary>The collection of the test classes that build the main consumer's folders, so they run one at a time.</summary>
    public const string MainCollection = "The main consumer";

    public string Repository { get; } = FindRepository();

    public string RunRoot => Path.Combine(Base, Run);

    /// <summary>The consumer's own folder; a space and non-ASCII letters, as a user's documents folder can have.</summary>
    public string Root => Path.Combine(RunRoot, "Nötes Äpp");

    /// <summary>The template engine's settings and installed templates for this run: <c>--debug:custom-hive</c>.</summary>
    public string TemplateHive => Path.Combine(RunRoot, "template-hive");

    /// <summary>The main consumer's files as <c>dotnet new</c> wrote them, before the run added its nuget.config.</summary>
    public IReadOnlyList<string> NewAppFiles { get; private set; } = [];

    /// <summary>The main consumer's global.json as <c>dotnet new</c> wrote it.</summary>
    public string NewAppGlobalJson { get; private set; } = "";

    /// <summary>A second consumer of the same packages, whose solution manages package versions centrally.</summary>
    public string CentralRoot => Path.Combine(RunRoot, "Central Äpp");

    /// <summary>A third consumer, whose app serves the contract of a library it gets as a package.</summary>
    public string LibraryAppRoot => Path.Combine(RunRoot, "Library Äpp");

    /// <summary>That library's own folder, as another solution would be.</summary>
    public string LibraryRoot => Path.Combine(RunRoot, "Tags Library");

    public string Feed => Path.Combine(RunRoot, "feed");

    /// <summary>The library's feed, apart from Spar's.</summary>
    public string LibraryFeed => Path.Combine(RunRoot, "library-feed");

    /// <summary>The library's version: new each run, so the shared package folder never holds a stale copy.</summary>
    public string LibraryVersion { get; } = $"1.0.0-dev.{Run}";

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
        Directory.CreateDirectory(LibraryFeed);
        RequireNoRepositoryAbove(Root);
        Assert.True(File.Exists(Path.Combine(Repository, "src", "Spar.Client", "obj", "dist", "sdk.js")),
            "The TypeScript SDK is not built: run 'npm ci --ignore-scripts' and 'npm run build' in src/Spar.Client.");

        var salt = Guid.NewGuid().ToString("N");
        Environment = new Dictionary<string, string>
        {
            ["NUGET_PACKAGES"] = Path.Combine(Base, PackagesFolder),
            ["NUGET_HTTP_CACHE_PATH"] = Path.Combine(Base, "http-cache"),
            ["npm_config_cache"] = Path.Combine(Base, "npm-cache"),
            ["MSBUILDNODEHANDSHAKESALT"] = salt,
            ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "false",

            // No node outlives a command (dotnet test passes -nr:false on to the test application, so it is set here).
            ["MSBUILDDISABLENODEREUSE"] = "1",

            // A fresh app has no committed contract snapshot; under CI's setting its build would verify one (SPAR100).
            ["ContinuousIntegrationBuild"] = "false",
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_NOLOGO"] = "1",
            ["SharedCompilationId"] = $"spar-package-tests-{salt}",

            // The consumer's test hosts keep their data roots and browser profiles in this repository's disposable
            // artifacts/ (AGENTS.md), never in the system's temporary or per-user folders.
            ["SPAR_TEST_ARTIFACTS"] = Path.Combine(Repository, "artifacts"),
        };

        // Packing must write only under bin/ and obj/: the working tree reads the same before and after.
        var before = await WorkingTreeAsync();
        await PackAsync(First);
        await PackAsync(Second);
        Assert.Equal(before, await WorkingTreeAsync());
        var page = Path.Combine(Repository, "tests", "Spar.PackageTests", "consumer-ui");
        var (installed, installOutput) = await DotnetInAsync(RunRoot, "", "new", "install",
            Path.Combine(Feed, $"Spar.Templates.{First}.nupkg"), "--debug:custom-hive", TemplateHive);
        Assert.True(installed == 0, $"dotnet new install:\n{installOutput}");
        var (created, createOutput) = await NewAppAsync(Root, "-n", App);
        Assert.True(created == 0, $"dotnet new spar-app:\n{createOutput}");
        NewAppFiles = [.. Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(Root, file).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];
        NewAppGlobalJson = File.ReadAllText(Path.Combine(Root, "global.json"));
        ConsumerFiles.WriteNugetConfig(Root, Feed);
        Directory.CreateDirectory(CentralRoot);
        ConsumerFiles.WriteCentral(CentralRoot, Feed, First, RepositorySdkVersion(), page, PackageVersion("Microsoft.Extensions.DependencyInjection"));
        LibraryConsumerFiles.WriteLibrary(LibraryRoot, Feed, First, RepositorySdkVersion());
        LibraryConsumerFiles.WriteApp(LibraryAppRoot, Feed, LibraryFeed, First, LibraryVersion, RepositorySdkVersion(), page);
    }

    public ValueTask DisposeAsync()
    {
        // Kept whether or not the run passed, so a slow run can be explained; CI uploads this folder.
        var results = Path.Combine(Repository, "artifacts", "test-results", "Spar.PackageTests");
        Directory.CreateDirectory(results);
        File.WriteAllText(Path.Combine(results, "timings.txt"), Tool.Timings());

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

    /// <summary>
    /// The main consumer's first build: its fast tests, restored and built from the feed, then run. Once per run: every
    /// test of the main consumer starts by awaiting it (they share one collection, so they run one at a time).
    /// </summary>
    public Task<(int Exit, string Output)> FirstAsync() =>
        _first ??= DotnetAsync("", "test", "--project", Path.Combine($"{App}.Tests", $"{App}.Tests.csproj"));

    /// <summary>
    /// The main consumer's app published with NativeAOT into the run's <c>publish/</c>, once per run: without the host of
    /// this OS (<c>SparPlatforms</c> the other one), so running it opens no window here. Its executable's path.
    /// </summary>
    public Task<(int Exit, string Output, string Executable)> PublishedAsync() => _published ??= PublishAsync();

    /// <summary>
    /// The environment of the spar tool's commands (<c>dotnet tool restore</c>, <c>dotnet spar</c>): the consumers', with
    /// the .NET CLI's home folder in the run's folder, where the local tools' resolver cache goes, never the user's
    /// <c>~/.dotnet</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string> ToolEnvironment => new Dictionary<string, string>(Environment)
    {
        ["DOTNET_CLI_HOME"] = Path.Combine(RunRoot, "dotnet-cli-home"),
    };

    /// <summary>Runs a tool command (<c>dotnet tool …</c> or <c>dotnet spar …</c>) under <paramref name="root"/>, in <paramref name="directory"/>.</summary>
    public Task<(int Exit, string Output)> ToolInAsync(string root, string directory, params string[] arguments) =>
        Tool.RunAsync(Path.Combine(root, directory), "dotnet", arguments, ToolEnvironment);

    private async Task<(int Exit, string Output, string Executable)> PublishAsync()
    {
        var publish = Path.Combine(RunRoot, "publish");
        var otherPlatform = OperatingSystem.IsWindows() ? "linux" : "windows";
        var (exit, output) = await DotnetAsync(App, "publish", "-c", "Release", "-o", publish, "-nologo", $"-p:SparPlatforms={otherPlatform}");
        return (exit, output, Path.Combine(publish, OperatingSystem.IsWindows() ? $"{App}.exe" : App));
    }

    /// <summary>
    /// Makes a new app from the installed template into <paramref name="folder"/>, with the run's hive and no update check
    /// (which would ask NuGet for a newer Spar.Templates), from the run's folder: <c>dotnet new spar-app -o folder …</c>.
    /// The new folder has no nuget.config: the caller writes the one that names the run's feed.
    /// </summary>
    public Task<(int Exit, string Output)> NewAppAsync(string folder, params string[] options) =>
        DotnetInAsync(RunRoot, "", ["new", "spar-app", "-o", folder, .. options, "--debug:custom-hive", TemplateHive, "--no-update-check"]);

    /// <summary>
    /// Points the consumer's global.json at a version of Spar.Sdk, as an update or a downgrade would: the template's own
    /// file, with only that version changed.
    /// </summary>
    public void UseSdk(string version)
    {
        var path = Path.Combine(Root, "global.json");
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        json["msbuild-sdks"]!["Spar.Sdk"] = version;
        File.WriteAllText(path, json.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    /// <summary>Runs <c>dotnet</c> in the consumer with its isolated environment, from <paramref name="directory"/> (relative to the root).</summary>
    public Task<(int Exit, string Output)> DotnetAsync(string directory, params string[] arguments) =>
        DotnetInAsync(Root, directory, arguments);

    /// <summary>Runs <c>dotnet</c> in the central-package-management consumer, from <paramref name="directory"/>.</summary>
    public Task<(int Exit, string Output)> CentralDotnetAsync(string directory, params string[] arguments) =>
        DotnetInAsync(CentralRoot, directory, arguments);

    /// <summary>Runs <c>dotnet</c> under <paramref name="root"/>, one of the run's folders, with the isolated environment.</summary>
    public Task<(int Exit, string Output)> DotnetInAsync(string root, string directory, params string[] arguments) =>
        Tool.RunAsync(Path.Combine(root, directory), "dotnet", arguments, Environment);

    /// <summary>
    /// Packs Spar's nine packages at <paramref name="version"/> into the feed, from this repository with its own build
    /// settings: the MSBuild server off and no node reuse, so a pack never shares a node with the consumer.
    /// </summary>
    private async Task PackAsync(string version)
    {
        string[] projects = ["Spar", "Spar.Windows", "Spar.Linux", "Spar.Loopback", "Spar.Testing", "Spar.Testing.Browser", "Spar.Sdk", "Spar.Templates", "Spar.Tool"];
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

    /// <summary>A package's version from the repository's central package management, so the consumer uses the same.</summary>
    public string PackageVersion(string id)
    {
        var props = System.Xml.Linq.XDocument.Load(Path.Combine(Repository, "Directory.Packages.props"));
        var version = props.Descendants("PackageVersion").Single(p => (string?)p.Attribute("Include") == id).Attribute("Version");
        return version?.Value ?? throw new InvalidOperationException($"Directory.Packages.props has no version for {id}.");
    }

    public string RepositorySdkVersion()
    {
        var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(Repository, "global.json")));
        return json.RootElement.GetProperty("sdk").GetProperty("version").GetString()!;
    }

    /// <summary>No folder above the consumer may hold what would change its build: it must see Spar's packages alone.</summary>
    private static void RequireNoRepositoryAbove(string folder)
    {
        string[] influences = [".git", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "global.json", "nuget.config", "NuGet.Config", "package.json", "node_modules"];
        for (var directory = new DirectoryInfo(folder).Parent; directory is not null; directory = directory.Parent)
        {
            var found = influences.FirstOrDefault(name => Path.Exists(Path.Combine(directory.FullName, name)));
            Assert.True(found is null, $"'{Path.Combine(directory.FullName, found ?? "")}' sits above the consumer and would change its build.");
        }
    }

    /// <summary>
    /// Removes earlier runs' folders, and their Spar packages from the shared package folder, keeping everything else
    /// there and the NuGet and npm caches. Two runs at once would delete each other's packages; runs are one at a time.
    /// </summary>
    private static void DeleteEarlierRuns()
    {
        if (!Directory.Exists(Base))
        {
            return;
        }

        foreach (var earlier in Directory.EnumerateDirectories(Base).Where(d => !Shared.Contains(Path.GetFileName(d))))
        {
            TryDelete(earlier);
        }

        var packages = Path.Combine(Base, PackagesFolder);
        if (!Directory.Exists(packages))
        {
            return;
        }

        // NuGet names package folders by lower-case id: spar, spar.sdk, spar.testing and the rest, and the library's.
        foreach (var packed in Directory.EnumerateDirectories(packages).Where(IsPackedByRuns))
        {
            TryDelete(packed);
        }
    }

    /// <summary>The packages a run packs itself, at versions of its own: Spar's and the contract library's.</summary>
    private static bool IsPackedByRuns(string folder)
    {
        var id = Path.GetFileName(folder);
        var library = LibraryConsumerFiles.PackageId.ToLowerInvariant();
        return id == "spar" || id.StartsWith("spar.", StringComparison.Ordinal) || id == library;
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

/// <summary>Runs a tool and collects what it wrote, and how long each run took.</summary>
public static class Tool
{
    private static readonly List<string> Runs = [];

    /// <summary>Every run so far, in the order they ended: seconds, exit code, folder and command line.</summary>
    public static string Timings()
    {
        lock (Runs)
        {
            return string.Join("\n", Runs) + "\n";
        }
    }

    public static async Task<(int Exit, string Output)> RunAsync(string directory, string tool, string[] arguments, IReadOnlyDictionary<string, string> environment)
    {
        var clock = Stopwatch.StartNew();
        var result = await StartAsync(directory, tool, arguments, environment);
        var line = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{clock.Elapsed.TotalSeconds,7:0.00} s  exit {result.Exit,-3} {Path.GetFileName(directory)}> {tool} {string.Join(' ', arguments)}");
        lock (Runs)
        {
            Runs.Add(line);
        }

        return result;
    }

    private static async Task<(int Exit, string Output)> StartAsync(string directory, string tool, string[] arguments, IReadOnlyDictionary<string, string> environment)
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
