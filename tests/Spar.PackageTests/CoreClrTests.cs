using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Spar.PackageTests;

/// <summary>
/// A new app published on CoreCLR framework-dependent, as the publishing-choices guide's block makes it (plan, M1d step
/// 7; decision "M1d step 7's shape: runtime choices"): the publication's files, the build identity doctor reads from it,
/// and the app started to <c>ui-ready</c> by the template's own black-box tests, which publish it the same way.
/// </summary>
public sealed class FrameworkDependentTests(Consumer consumer) : IDisposable
{
    private static readonly CoreClrShape Shape = new("Framework Äpp", "Folio", GuideSnippets.FrameworkDependent);

    [Fact]
    public async Task A_framework_dependent_publication_holds_the_app_its_libraries_and_the_WebView2_loader()
    {
        var app = await Shape.PublishedAsync(consumer);

        var files = app.Files();
        Assert.Contains(app.Executable, files);
        Assert.Contains($"{Shape.App}.dll", files);
        Assert.Contains("Spar.dll", files);
        Assert.Equal(OperatingSystem.IsWindows(), files.Contains("WebView2Loader.dll"));
        Assert.DoesNotContain(files, file => file.StartsWith("hostfxr", StringComparison.Ordinal) || file.StartsWith("libhostfxr", StringComparison.Ordinal));

        // The .NET runtime alone: a Production build has no loopback host, so no ASP.NET Core.
        using var configuration = JsonDocument.Parse(File.ReadAllText(Path.Combine(app.Publish, $"{Shape.App}.runtimeconfig.json")));
        var framework = configuration.RootElement.GetProperty("runtimeOptions").GetProperty("framework");
        Assert.Equal("Microsoft.NETCore.App", framework.GetProperty("name").GetString());
        Assert.False(configuration.RootElement.GetProperty("runtimeOptions").TryGetProperty("frameworks", out _));

        // The executable is the apphost; doctor reads the identity from the app's DLL beside it.
        var identity = await app.IdentityAsync(consumer);
        Assert.Equal(Path.Combine(app.Publish, $"{Shape.App}.dll"), identity.GetProperty("file").GetString());
        Assert.Equal("Production", identity.GetProperty("variant").GetString());
        Assert.Equal(app.SnapshotFingerprint(), identity.GetProperty("fingerprint").GetString());
    }

    [Fact]
    public async Task The_framework_dependent_app_starts_to_ui_ready_from_its_black_box_publication() =>
        await (await Shape.PublishedAsync(consumer)).StartsAsync(consumer, beside => Assert.Contains($"{Shape.App}.runtimeconfig.json", beside));

    public void Dispose() => consumer.Report(TestContext.Current.TestState?.Result == TestResult.Passed);
}

/// <summary>
/// A new app published on CoreCLR as a self-contained single file, as the guide's block makes it: the executable alone
/// (the WebView2 loader inside it on Windows, extracted at the start), its identity, its start through the black box;
/// and on Windows, without native self-extraction, the loader beside it.
/// </summary>
public sealed class SingleFileTests(Consumer consumer) : IDisposable
{
    private static readonly CoreClrShape Shape = new("Single Äpp", "Atlas", GuideSnippets.SingleFile);

    [Fact]
    public async Task A_self_contained_single_file_publication_is_the_executable_alone()
    {
        var app = await Shape.PublishedAsync(consumer);

        Assert.Equal([app.Executable], app.Files());
        var identity = await app.IdentityAsync(consumer);
        Assert.Equal(Path.Combine(app.Publish, app.Executable), identity.GetProperty("file").GetString());
        Assert.Equal("Production", identity.GetProperty("variant").GetString());
        Assert.Equal(app.SnapshotFingerprint(), identity.GetProperty("fingerprint").GetString());
    }

    [Fact]
    public async Task Without_native_self_extraction_the_WebView2_loader_stays_beside_the_single_file()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The WebView2 loader is Windows' only native library of a Spar app.");
        var app = await Shape.PublishedAsync(consumer);
        var beside = Path.Combine(consumer.RunRoot, "publish-single-file-beside");

        // Before Spar marked the loader ExcludeFromSingleFile, this publish failed with NETSDK1236.
        var (exit, output) = await consumer.DotnetInAsync(app.Root, "", "publish", Shape.App, "-c", "Release", "-o", beside, "-nologo",
            "-p:IncludeNativeLibrariesForSelfExtract=false");

        Assert.True(exit == 0, output);
        Assert.Equal([app.Executable, "WebView2Loader.dll"], CoreClrApp.FilesIn(beside));
    }

    [Fact]
    public async Task The_fast_tests_of_a_self_contained_app_build_and_pass()
    {
        var app = await Shape.PublishedAsync(consumer);

        // A framework-dependent test project referencing a self-contained app was NETSDK1151 before Spar.Sdk turned
        // the check off for its test projects, as the .NET SDK does for VSTest ones.
        var (exit, output) = await consumer.DotnetInAsync(app.Root, "", "test", "--project", Path.Combine($"{Shape.App}.Tests", $"{Shape.App}.Tests.csproj"));

        Assert.True(exit == 0, output);
        Assert.Contains("succeeded: 2", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_single_file_app_starts_to_ui_ready_from_its_black_box_publication() =>
        await (await Shape.PublishedAsync(consumer)).StartsAsync(consumer, beside => Assert.Equal([Shape.ExecutableName], beside));

    public void Dispose() => consumer.Report(TestContext.Current.TestState?.Result == TestResult.Passed);
}

/// <summary>One CoreCLR shape: a new app of its own (with the end-to-end project) whose project has one guide block.</summary>
internal sealed record CoreClrShape(string Folder, string App, string Snippet)
{
    public string ExecutableName => OperatingSystem.IsWindows() ? $"{App}.exe" : App;

    /// <summary>The app made, its project given the block, and published with the guide's command; once per run.</summary>
    public Task<CoreClrApp> PublishedAsync(Consumer consumer) => consumer.OnceAsync($"coreclr {Snippet}", async () =>
    {
        var root = Path.Combine(consumer.RunRoot, Folder);
        var (created, createOutput) = await consumer.NewAppAsync(root, "-n", App);
        Assert.True(created == 0, createOutput);
        ConsumerFiles.WriteNugetConfig(root, consumer.Feed);
        var project = Path.Combine(root, App, $"{App}.csproj");
        File.WriteAllText(project, GuideSnippets.AddTo(File.ReadAllText(project), GuideSnippets.Read(consumer.Repository), Snippet));
        File.WriteAllText(Path.Combine(root, $"{App}.EndToEndTests", "BlackBox", "StartTimes.cs"), CoreClrApp.StartTimesTest(App));

        var publish = Path.Combine(consumer.RunRoot, $"publish-{Snippet}");
        var (exit, output) = await consumer.DotnetInAsync(root, "", "publish", App, "-c", "Release", "-o", publish, "-nologo");
        Assert.True(exit == 0, output);
        var app = new CoreClrApp(this, root, publish);
        consumer.Measure(string.Create(CultureInfo.InvariantCulture, $"{Snippet} publication: {app.Files().Count} file(s), {app.Bytes():N0} B (debug symbols aside)"));
        return app;
    });
}

/// <summary>A published CoreCLR shape and what its tests check.</summary>
internal sealed class CoreClrApp(CoreClrShape shape, string root, string publish)
{
    /// <summary>The variable through which the consumer's start-time test learns where to write its lines.</summary>
    private const string StartTimesVariable = "PACKAGE_TESTS_START_TIMES";

    public string Root { get; } = root;

    public string Publish { get; } = publish;

    public string Executable => shape.ExecutableName;

    /// <summary>The publication's files, by name, debug symbols aside (they are not shipped).</summary>
    public IReadOnlyList<string> Files() => FilesIn(Publish);

    public static IReadOnlyList<string> FilesIn(string folder) =>
        [.. Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(file => Path.GetExtension(file) is not (".pdb" or ".dbg"))
            .Select(file => Path.GetRelativePath(folder, file).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];

    public long Bytes() =>
        Files().Sum(file => new FileInfo(Path.Combine(Publish, file)).Length);

    /// <summary>The build identity <c>dotnet spar doctor --app</c> reads from the published executable.</summary>
    public async Task<JsonElement> IdentityAsync(Consumer consumer)
    {
        var (restored, restoreOutput) = await consumer.ToolInAsync(Root, "", "tool", "restore");
        Assert.True(restored == 0, restoreOutput);
        var (_, output) = await consumer.ToolInAsync(Root, shape.App, "spar", "doctor", "--json", "--app", Path.Combine(Publish, Executable));
        var start = output.IndexOf('{', StringComparison.Ordinal);
        Assert.True(start >= 0, output);
        using var report = JsonDocument.Parse(output[start..(output.LastIndexOf('}') + 1)]);
        return report.RootElement.GetProperty("identity").Clone();
    }

    /// <summary>The fingerprint in the app's contract snapshot, which its build wrote.</summary>
    public string SnapshotFingerprint() =>
        File.ReadAllLines(Path.Combine(Root, shape.App, $"{shape.App}.contracts.snapshot"))
            .Single(line => line.StartsWith("fingerprint ", StringComparison.Ordinal))["fingerprint ".Length..];

    /// <summary>
    /// The end-to-end project's black-box tests, as their own invocation: the build publishes the app in its own shape
    /// into the project's <c>obj/</c> and copies it beside the tests (whose files <paramref name="checkBeside"/> checks),
    /// the template's launch test drives its page and closes it, and the start-time test launches it five more times.
    /// On Windows no folder on <c>PATH</c> may hold a WebView2 loader, which would hide a publication without one.
    /// </summary>
    public async Task StartsAsync(Consumer consumer, Action<IReadOnlyList<string>> checkBeside)
    {
        TemplateTests.SkipWithoutLinuxDesktop();
        var times = Path.Combine(consumer.RunRoot, $"start-times-{shape.Snippet}.txt");
        var tests = Path.Combine($"{shape.App}.EndToEndTests", $"{shape.App}.EndToEndTests.csproj");
        var (exit, output) = await Tool.RunAsync(Root, "dotnet", ["test", "--project", tests, "--filter-trait", "Surface=BlackBox"],
            StartTimesEnvironment(consumer, times));

        Assert.True(exit == 0, output);
        Assert.Contains("succeeded: 2", output, StringComparison.Ordinal);
        checkBeside(FilesIn(BesideTheTests(Root, shape.App)));
        ReportStartTimes(consumer, shape.Snippet, times);
    }

    /// <summary>The tested app's folder beside the end-to-end tests: <c>bin/…/spar-apps/&lt;app&gt;/</c>.</summary>
    public static string BesideTheTests(string root, string app) =>
        Directory.GetDirectories(Path.Combine(root, $"{app}.EndToEndTests", "bin"), app, SearchOption.AllDirectories)
            .Single(folder => Path.GetFileName(Path.GetDirectoryName(folder)) == "spar-apps");

    /// <summary>
    /// The consumers' environment for a black-box run with the start-time test: where it writes, and on Windows no
    /// folder on <c>PATH</c> that holds a WebView2 loader (the Windows SDK's Performance Toolkit has one), which would
    /// hide a publication without its own.
    /// </summary>
    public static Dictionary<string, string> StartTimesEnvironment(Consumer consumer, string times)
    {
        var environment = new Dictionary<string, string>(consumer.Environment) { [StartTimesVariable] = times };
        if (OperatingSystem.IsWindows())
        {
            environment["PATH"] = string.Join(Path.PathSeparator, (Environment.GetEnvironmentVariable("PATH") ?? "")
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Where(folder => !File.Exists(Path.Combine(folder, "WebView2Loader.dll"))));
        }

        return environment;
    }

    /// <summary>The start-time test's five lines, as measurements: each launch, and the median and range of ui-ready.</summary>
    public static void ReportStartTimes(Consumer consumer, string label, string times)
    {
        var lines = File.ReadAllLines(times);
        Assert.Equal(5, lines.Length);
        var ready = lines.Select(line => double.Parse(Regex.Match(line, @"ui-ready at (\d+(\.\d+)?) ms").Groups[1].Value, CultureInfo.InvariantCulture)).Order().ToList();
        consumer.Measure(string.Create(CultureInfo.InvariantCulture,
            $"{label} start to ui-ready (the host's log, 5 launches): median {ready[2]:0} ms, {ready[0]:0}-{ready[4]:0} ms"));
        foreach (var line in lines)
        {
            consumer.Measure($"{label} launch: {line}");
        }
    }

    /// <summary>
    /// The consumer's own start-time test, added to its end-to-end project: five launches of the published app, each
    /// to the host's <c>ui-ready</c> line (the time since the process started) and its window, then closed.
    /// </summary>
    public static string StartTimesTest(string app) => $$"""
        using System.Globalization;
        using Spar.Testing;

        namespace {{app}}.EndToEndTests;

        [Trait("Surface", "BlackBox")]
        public sealed class StartTimes
        {
            [Fact]
            public async Task The_published_app_starts_to_ui_ready_five_times()
            {
                var token = TestContext.Current.CancellationToken;
                var lines = new List<string>();
                for (var launch = 1; launch <= 5; launch++)
                {
                    await using var app = await SparBlackBox.LaunchAsync(Projects.{{app}}, cancellationToken: token);
                    using var log = new FileStream(app.LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    var ready = new StreamReader(log).ReadToEnd().Split('\n').First(line => line.Contains(" ui-ready at ", StringComparison.Ordinal)).Trim();
                    lines.Add(string.Create(CultureInfo.InvariantCulture, $"launch {launch}, ready to the fixture after {app.StartedAfter.TotalMilliseconds:0} ms: {ready}"));
                    Assert.Equal(0, await app.CloseAsync(token));
                }

                File.WriteAllLines(Environment.GetEnvironmentVariable("{{StartTimesVariable}}")!, lines);
            }
        }

        """;
}
