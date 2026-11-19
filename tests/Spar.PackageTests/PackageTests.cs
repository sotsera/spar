using System.Text.Json;

namespace Spar.PackageTests;

/// <summary>
/// An app and its tests built from Spar's packages alone (plan M1a, "the package-only consumer test"): they build and
/// pass, its page works in Chromium against its backend, a black-box test project publishes the app and launches it, the
/// packaged analyzer reports a contract problem at its line, a design-time build runs no Spar step, the app
/// publishes with NativeAOT, and an update and a downgrade of Spar.Sdk while MSBuild is warm each build with their own
/// SDK. The builds share the consumer's folders, so they stay in this one class, whose tests xUnit runs one at a time.
/// </summary>
public sealed class PackageTests(Consumer consumer) : IDisposable
{
    private static Task<(int Exit, string Output)>? s_first;

    [Fact]
    public async Task The_app_and_its_tests_build_and_pass_from_Spars_packages_alone()
    {
        var (exit, output) = await FirstAsync();

        Assert.True(exit == 0, output);
        Assert.Equal(consumer.First, ClientVersion());
        Assert.Equal("0.0.0", ClientPackage().GetProperty("version").GetString());
        var lockfile = File.ReadAllText(Path.Combine(consumer.Root, "App", "ui", "package-lock.json"));
        var committed = File.ReadAllText(Path.Combine(consumer.Repository, "tests", "Spar.PackageTests", "consumer-ui", "package-lock.json"));
        Assert.True(lockfile == committed, "The build changed the app's committed lockfile.");
        Assert.True(File.Exists(Path.Combine(consumer.Root, "App", "Notes.App.contracts.snapshot")), "No contract snapshot.");
        var restored = Path.Combine(consumer.Environment["NUGET_PACKAGES"], "spar", consumer.First.ToLowerInvariant());
        Assert.True(Directory.Exists(restored), $"Spar {consumer.First} was not restored from the run's feed.");
    }

    [Fact]
    public async Task The_apps_page_works_in_a_browser_against_its_backend()
    {
        Assert.Equal(0, (await FirstAsync()).Exit);

        var (exit, output) = await consumer.DotnetAsync("", "test", "--project", Path.Combine("App.EndToEndTests", "Notes.App.EndToEndTests.csproj"));

        Assert.True(exit == 0, output);
        Assert.Contains("succeeded: 1", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_black_box_test_project_publishes_the_app_once_and_launches_it()
    {
        Assert.Equal(0, (await FirstAsync()).Exit);
        var project = Path.Combine("App.BlackBoxTests", "Notes.App.BlackBoxTests.csproj");

        var (exit, output) = await consumer.DotnetAsync("", "test", "--project", project);

        Assert.True(exit == 0, output);
        Assert.Contains(OperatingSystem.IsWindows() ? "succeeded: 1" : "skipped: 1", output, StringComparison.Ordinal);
        if (!OperatingSystem.IsWindows())
        {
            // The publish is on by default where the fixture runs: Windows, until the Linux host (M1c).
            return;
        }

        const string name = "NotesTool.exe";
        var beside = Directory.GetFiles(Path.Combine(consumer.Root, "App.BlackBoxTests", "bin"), name, SearchOption.AllDirectories);
        var published = Assert.Single(beside, path => path.Contains(Path.Combine("spar-apps", "NotesTool"), StringComparison.Ordinal));
        var written = File.GetLastWriteTimeUtc(published);

        // An unchanged build publishes nothing again: the executable beside the tests is the same file.
        var (rebuilt, again) = await consumer.DotnetAsync("", "build", project, "-nologo", "-v:m");

        Assert.True(rebuilt == 0, again);
        Assert.DoesNotContain("Generating native code", again, StringComparison.Ordinal);
        Assert.Equal(written, File.GetLastWriteTimeUtc(published));
    }

    [Fact]
    public async Task The_packaged_analyzer_reports_a_contract_problem_at_its_line()
    {
        Assert.Equal(0, (await FirstAsync()).Exit);
        var broken = Path.Combine(consumer.Root, "App", "Broken.cs");
        File.WriteAllText(broken, "using Spar;\n\nnamespace NotesTool;\n\n[SparContract]\npublic class Broken\n{\n}\n");
        try
        {
            var (exit, output) = await consumer.DotnetAsync("App", "build", "-nologo", "-v:m");

            Assert.NotEqual(0, exit);
            Assert.Contains("Broken.cs(6,14", output, StringComparison.Ordinal);
            Assert.Contains("error SPAR001", output, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(broken);
        }
    }

    [Fact]
    public async Task A_test_project_must_name_its_app_with_SparTestedApplication()
    {
        Assert.Equal(0, (await FirstAsync()).Exit);
        var tests = Path.Combine(consumer.Root, "App.Tests", "Notes.App.Tests.csproj");
        var app = Path.Combine(consumer.Root, "App", "Notes.App.csproj");
        var library = Path.Combine(consumer.Root, "Lib", "Lib.csproj");
        var testsProject = File.ReadAllText(tests);
        var appProject = File.ReadAllText(app);
        Directory.CreateDirectory(Path.GetDirectoryName(library)!);
        File.WriteAllText(library, "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net11.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n");
        try
        {
            // The app through a plain reference: it would build as Development, without the test host.
            File.WriteAllText(tests, testsProject.Replace("<SparTestedApplication ", "<ProjectReference ", StringComparison.Ordinal));
            var (plain, plainOutput) = await consumer.DotnetAsync("App.Tests", "build", "-nologo", "-v:m");
            Assert.NotEqual(0, plain);
            Assert.Contains("error SPAR121", plainOutput, StringComparison.Ordinal);
            // MSBuild writes the item's path with the platform's separator.
            var item = Path.Combine("..", "App", "Notes.App.csproj");
            Assert.Contains($"<SparTestedApplication Include=\"{item}\" />", plainOutput, StringComparison.Ordinal);

            // The item's own metadata reaches the compiler's reference to the app.
            File.WriteAllText(tests, testsProject.Replace("<SparTestedApplication Include=\"..\\App\\Notes.App.csproj\"",
                "<SparTestedApplication Include=\"..\\App\\Notes.App.csproj\" Aliases=\"notes\"", StringComparison.Ordinal));
            var (resolved, resolvedOutput) = await consumer.DotnetAsync("App.Tests", "msbuild", "-t:ResolveReferences",
                "-getItem:ReferencePath", "-nologo");
            Assert.True(resolved == 0, resolvedOutput);
            Assert.Equal("notes", ReferenceAliases(resolvedOutput, "NotesTool.dll"));

            // A library named as the tested app.
            File.WriteAllText(tests, testsProject.Replace("..\\App\\Notes.App.csproj", "..\\Lib\\Lib.csproj", StringComparison.Ordinal));
            var (notAnApp, notAnAppOutput) = await consumer.DotnetAsync("App.Tests", "build", "-nologo", "-v:m");
            Assert.NotEqual(0, notAnApp);
            Assert.Contains("error SPAR122", notAnAppOutput, StringComparison.Ordinal);

            // The item outside a test project.
            File.WriteAllText(tests, testsProject);
            File.WriteAllText(app, appProject.Replace("</Project>", "  <ItemGroup>\n    <SparTestedApplication Include=\"..\\Lib\\Lib.csproj\" />\n  </ItemGroup>\n</Project>", StringComparison.Ordinal));
            var (misplaced, misplacedOutput) = await consumer.DotnetAsync("App", "build", "-nologo", "-v:m");
            Assert.NotEqual(0, misplaced);
            Assert.Contains("error SPAR120", misplacedOutput, StringComparison.Ordinal);
        }
        finally
        {
            File.WriteAllText(tests, testsProject);
            File.WriteAllText(app, appProject);
            Directory.Delete(Path.GetDirectoryName(library)!, recursive: true);
        }
    }

    [Fact]
    public async Task An_app_without_a_valid_app_id_does_not_build()
    {
        Assert.Equal(0, (await FirstAsync()).Exit);
        var app = Path.Combine(consumer.Root, "App", "Notes.App.csproj");
        var appProject = File.ReadAllText(app);
        try
        {
            File.WriteAllText(app, appProject.Replace("<SparAppId>org.example.notes.Tool</SparAppId>", "", StringComparison.Ordinal));
            var (missing, missingOutput) = await consumer.DotnetAsync("App", "build", "-nologo", "-v:m");
            Assert.NotEqual(0, missing);
            Assert.Contains("error SPAR130", missingOutput, StringComparison.Ordinal);
            Assert.Contains("has no SparAppId", missingOutput, StringComparison.Ordinal);

            // The last is 117 characters, one over the cap that keeps "<id>.Development" within Windows' 128.
            var tooLong = "org.example." + new string('n', 105);
            foreach (var invalid in new[] { "NotesTool", "org.example.1notes", "org.example.notes-tool", "org..notes", tooLong })
            {
                File.WriteAllText(app, appProject.Replace("org.example.notes.Tool", invalid, StringComparison.Ordinal));
                var (failed, failedOutput) = await consumer.DotnetAsync("App", "build", "-nologo", "-v:m");
                Assert.NotEqual(0, failed);
                Assert.Contains($"SparAppId '{invalid}' is not a valid app id", failedOutput, StringComparison.Ordinal);
            }
        }
        finally
        {
            File.WriteAllText(app, appProject);
        }
    }

    [Fact]
    public async Task A_design_time_build_runs_no_Spar_step()
    {
        Assert.Equal(0, (await FirstAsync()).Exit);

        var (exit, output) = await consumer.DotnetAsync("App", "msbuild", "-t:Compile", "-p:DesignTimeBuild=true",
            "-p:BuildingInsideVisualStudio=true", "-p:SkipCompilerExecution=true", "-p:ProvideCommandLineArgs=true", "-nologo", "-v:n");

        Assert.True(exit == 0, output);
        Assert.DoesNotContain("Spar client:", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_app_publishes_with_NativeAOT_and_its_executable_runs()
    {
        Assert.Equal(0, (await FirstAsync()).Exit);
        var publish = Path.Combine(consumer.RunRoot, "publish");

        // Published without the Windows host, so running it opens no window here: the host itself is checked on the
        // Notebook (docs/evidence/m1b-host.md), and this run checks the error of a build with no host for its OS.
        var (exit, output) = await consumer.DotnetAsync(
            "App", "publish", "-c", "Release", "-o", publish, "-nologo", "-p:SparPlatforms=linux");

        Assert.True(exit == 0, output);
        var executable = Path.Combine(publish, OperatingSystem.IsWindows() ? "NotesTool.exe" : "NotesTool");
        // The page from ui/, built by Vite and packed by the SDK's task, is in the executable.
        Assert.True(Contains(File.ReadAllBytes(executable), "SPARFE"u8), "The published executable carries no packed page.");
        // The app id reaches the runtime through the generated catalog, a string literal (UTF-16) in the image. On
        // Linux nothing reads it until the Linux host (M1c), so the trimmer drops it there; on Windows the message of
        // a build with no host for its OS logs into the app id's folder.
        if (OperatingSystem.IsWindows())
        {
            Assert.True(Contains(File.ReadAllBytes(executable), System.Text.Encoding.Unicode.GetBytes("org.example.notes.Tool")),
                "The published executable does not carry the app id.");
        }
        // The app's per-user folders follow LOCALAPPDATA, so a run that ever reaches a host stays inside this test's root
        // and never writes into the developer's own profile (AGENTS.md).
        var perUser = Path.Combine(consumer.RunRoot, "local-app-data");
        var (ran, said) = await Tool.RunAsync(publish, executable, [], new Dictionary<string, string> { ["LOCALAPPDATA"] = perUser });

        // No host for this OS in the build (Spar.Linux does not exist yet): the executable says so and exits with 3.
        Assert.Equal(3, ran);
        Assert.Contains("no desktop host", said, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_update_and_a_downgrade_while_MSBuild_is_warm_each_build_with_their_own_SDK()
    {
        Assert.Equal(0, (await FirstAsync()).Exit);
        try
        {
            foreach (var version in new[] { consumer.First, consumer.Second, consumer.First })
            {
                consumer.UseSdk(version);
                var (exit, output) = await WarmAsync("build", "-nologo");

                Assert.True(exit == 0, $"Building with Spar.Sdk {version}:\n{output}");
                Assert.Equal(version, ClientVersion());
            }
        }
        finally
        {
            consumer.UseSdk(consumer.First);

            // Only this run's server: the handshake salt names its pipe. Never every server on the machine.
            await WarmAsync("build-server", "shutdown", "--msbuild");
        }
    }

    public void Dispose() => consumer.Report(TestContext.Current.TestState?.Result == TestResult.Passed);

    /// <summary>The consumer's first build: its tests, restored and built from the feed, then run.</summary>
    private Task<(int Exit, string Output)> FirstAsync() =>
        s_first ??= consumer.DotnetAsync("", "test", "--project", Path.Combine("App.Tests", "Notes.App.Tests.csproj"));

    /// <summary>A build with node reuse and the MSBuild server on, both under this run's handshake salt.</summary>
    private Task<(int Exit, string Output)> WarmAsync(params string[] arguments)
    {
        var environment = new Dictionary<string, string>(consumer.Environment) { ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "true" };
        environment.Remove("MSBUILDDISABLENODEREUSE");
        return Tool.RunAsync(Path.Combine(consumer.Root, "App"), "dotnet", arguments, environment);
    }

    /// <summary>The SDK that generated the app's @spar/app package, from its sparSdk field; its version stays 0.0.0.</summary>
    private string ClientVersion() => ClientPackage().GetProperty("sparSdk").GetString()!;

    /// <summary>The app's generated package.json.</summary>
    private JsonElement ClientPackage()
    {
        var package = Path.Combine(consumer.Root, "App", "obj", "spar", "client", "package.json");
        return JsonDocument.Parse(File.ReadAllText(package)).RootElement.Clone();
    }

    /// <summary>The <c>Aliases</c> metadata of one resolved reference, from <c>-getItem:ReferencePath</c>'s JSON.</summary>
    private static string? ReferenceAliases(string json, string fileName)
    {
        var references = JsonDocument.Parse(json).RootElement.GetProperty("Items").GetProperty("ReferencePath");
        foreach (var reference in references.EnumerateArray())
        {
            var path = reference.GetProperty("Identity").GetString()!;
            if (Path.GetFileName(path).Equals(fileName, StringComparison.OrdinalIgnoreCase))
            {
                return reference.TryGetProperty("Aliases", out var aliases) ? aliases.GetString() : null;
            }
        }

        throw new InvalidOperationException($"No reference to {fileName} among the resolved references:\n{json}");
    }

    private static bool Contains(byte[] haystack, ReadOnlySpan<byte> needle) => haystack.AsSpan().IndexOf(needle) >= 0;
}
