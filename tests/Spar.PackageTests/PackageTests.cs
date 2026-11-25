using System.Text.Json;

namespace Spar.PackageTests;

/// <summary>
/// The build machinery of an app made from the template, from Spar's packages alone (plan M1a, "the package-only
/// consumer test"): its first build and fast tests, the packaged analyzer reporting a contract problem at its line,
/// the SDK's checks of the tested app and the app id, a design-time build that runs no Spar step, the app's NativeAOT
/// publication, and an update and a downgrade of Spar.Sdk while MSBuild is warm, each building with its own SDK. The
/// template's tests themselves are <see cref="TemplateTests"/>. Both build the main consumer's folders, so they share
/// one collection and run one at a time.
/// </summary>
[Collection(Consumer.MainCollection)]
public sealed class PackageTests(Consumer consumer) : IDisposable
{
    private const string App = Consumer.App;

    [Fact]
    public async Task The_app_and_its_fast_tests_build_and_pass_from_Spars_packages_alone()
    {
        var (exit, output) = await consumer.FirstAsync();

        Assert.True(exit == 0, output);
        Assert.Contains("succeeded: 2", output, StringComparison.Ordinal);
        Assert.Equal(consumer.First, ClientVersion());
        Assert.Equal("0.0.0", ClientPackage().GetProperty("version").GetString());
        var lockfile = File.ReadAllText(Path.Combine(consumer.Root, App, "frontend", "package-lock.json"));
        var committed = File.ReadAllText(Path.Combine(ConsumerFiles.Template(consumer.Repository), "MyApp", "frontend", "package-lock.json"));
        Assert.True(lockfile == ConsumerFiles.Rename(committed, App), "The build changed the template's committed lockfile.");
        Assert.True(File.Exists(Path.Combine(consumer.Root, App, $"{App}.contracts.snapshot")), "No contract snapshot.");
        var restored = Path.Combine(consumer.Environment["NUGET_PACKAGES"], "spar", consumer.First.ToLowerInvariant());
        Assert.True(Directory.Exists(restored), $"Spar {consumer.First} was not restored from the run's feed.");
    }

    [Fact]
    public async Task The_packaged_analyzer_reports_a_contract_problem_at_its_line()
    {
        Assert.Equal(0, (await consumer.FirstAsync()).Exit);
        var broken = Path.Combine(consumer.Root, App, "Broken.cs");
        File.WriteAllText(broken, $"using Spar;\n\nnamespace {App};\n\n[SparContract]\npublic class Broken\n{{\n}}\n");
        try
        {
            var (exit, output) = await consumer.DotnetAsync(App, "build", "-nologo", "-v:m");

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
        Assert.Equal(0, (await consumer.FirstAsync()).Exit);
        var testsFolder = $"{App}.Tests";
        var tests = Path.Combine(consumer.Root, testsFolder, $"{App}.Tests.csproj");
        var app = Path.Combine(consumer.Root, App, $"{App}.csproj");
        var library = Path.Combine(consumer.Root, "Lib", "Lib.csproj");
        var appItem = $"..\\{App}\\{App}.csproj";
        var testsProject = File.ReadAllText(tests);
        var appProject = File.ReadAllText(app);
        Directory.CreateDirectory(Path.GetDirectoryName(library)!);
        File.WriteAllText(library, "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net11.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n");
        try
        {
            // The app through a plain reference: it would build as Development, without the test host.
            File.WriteAllText(tests, testsProject.Replace("<SparTestedApplication ", "<ProjectReference ", StringComparison.Ordinal));
            var (plain, plainOutput) = await consumer.DotnetAsync(testsFolder, "build", "-nologo", "-v:m");
            Assert.NotEqual(0, plain);
            Assert.Contains("error SPAR121", plainOutput, StringComparison.Ordinal);
            // MSBuild writes the item's path with the platform's separator.
            var item = Path.Combine("..", App, $"{App}.csproj");
            Assert.Contains($"<SparTestedApplication Include=\"{item}\" />", plainOutput, StringComparison.Ordinal);

            // The item's own metadata reaches the compiler's reference to the app.
            File.WriteAllText(tests, testsProject.Replace($"<SparTestedApplication Include=\"{appItem}\"",
                $"<SparTestedApplication Include=\"{appItem}\" Aliases=\"notes\"", StringComparison.Ordinal));
            var (resolved, resolvedOutput) = await consumer.DotnetAsync(testsFolder, "msbuild", "-t:ResolveReferences",
                "-getItem:ReferencePath", "-nologo");
            Assert.True(resolved == 0, resolvedOutput);
            Assert.Equal("notes", ReferenceAliases(resolvedOutput, $"{App}.dll"));

            // A library named as the tested app.
            File.WriteAllText(tests, testsProject.Replace(appItem, "..\\Lib\\Lib.csproj", StringComparison.Ordinal));
            var (notAnApp, notAnAppOutput) = await consumer.DotnetAsync(testsFolder, "build", "-nologo", "-v:m");
            Assert.NotEqual(0, notAnApp);
            Assert.Contains("error SPAR122", notAnAppOutput, StringComparison.Ordinal);

            // The item outside a test project.
            File.WriteAllText(tests, testsProject);
            File.WriteAllText(app, appProject.Replace("</Project>", "  <ItemGroup>\n    <SparTestedApplication Include=\"..\\Lib\\Lib.csproj\" />\n  </ItemGroup>\n</Project>", StringComparison.Ordinal));
            var (misplaced, misplacedOutput) = await consumer.DotnetAsync(App, "build", "-nologo", "-v:m");
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
        Assert.Equal(0, (await consumer.FirstAsync()).Exit);
        var app = Path.Combine(consumer.Root, App, $"{App}.csproj");
        var appId = $"com.example.{App}";
        var appProject = File.ReadAllText(app);
        try
        {
            File.WriteAllText(app, appProject.Replace($"<SparAppId>{appId}</SparAppId>", "", StringComparison.Ordinal));
            var (missing, missingOutput) = await consumer.DotnetAsync(App, "build", "-nologo", "-v:m");
            Assert.NotEqual(0, missing);
            Assert.Contains("error SPAR130", missingOutput, StringComparison.Ordinal);
            Assert.Contains("has no SparAppId", missingOutput, StringComparison.Ordinal);

            // The last is 117 characters, one over the cap that keeps "<id>.Development" within Windows' 128.
            var tooLong = "org.example." + new string('n', 105);
            foreach (var invalid in new[] { App, "org.example.1notes", "org.example.notes-tool", "org..notes", tooLong })
            {
                File.WriteAllText(app, appProject.Replace(appId, invalid, StringComparison.Ordinal));
                var (failed, failedOutput) = await consumer.DotnetAsync(App, "build", "-nologo", "-v:m");
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
        Assert.Equal(0, (await consumer.FirstAsync()).Exit);

        var (exit, output) = await consumer.DotnetAsync(App, "msbuild", "-t:Compile", "-p:DesignTimeBuild=true",
            "-p:BuildingInsideVisualStudio=true", "-p:SkipCompilerExecution=true", "-p:ProvideCommandLineArgs=true", "-nologo", "-v:n");

        Assert.True(exit == 0, output);
        Assert.DoesNotContain("Spar client:", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_app_publishes_with_NativeAOT_and_its_executable_runs()
    {
        Assert.Equal(0, (await consumer.FirstAsync()).Exit);

        // Published without the host of this OS, so running it opens no window here: the hosts themselves are checked
        // by the template's black-box test (TemplateTests) and on the Notebook, and this run checks the error of a build
        // with no host for its OS. ToolTests reads the same executable's build identity.
        var (exit, output, executable) = await consumer.PublishedAsync();

        Assert.True(exit == 0, output);
        var publish = Path.GetDirectoryName(executable)!;
        // The page from frontend/, built by Vite and packed by the SDK's task, is in the executable.
        Assert.True(Contains(File.ReadAllBytes(executable), "SPARFE"u8), "The published executable carries no packed page.");
        // The app id reaches the runtime through the generated catalog, a string literal (UTF-16) in the image. On
        // Linux a build without its host never reads it, so the trimmer drops it there; on Windows the message of a
        // build with no host for its OS logs into the app id's folder.
        if (OperatingSystem.IsWindows())
        {
            Assert.True(Contains(File.ReadAllBytes(executable), System.Text.Encoding.Unicode.GetBytes($"com.example.{App}")),
                "The published executable does not carry the app id.");
        }
        // The app's per-user folders follow LOCALAPPDATA, so a run that ever reaches a host stays inside this test's root
        // and never writes into the developer's own profile (AGENTS.md).
        var perUser = Path.Combine(consumer.RunRoot, "local-app-data");
        var (ran, said) = await Tool.RunAsync(publish, executable, [], new Dictionary<string, string> { ["LOCALAPPDATA"] = perUser });

        // No host for this OS in the build: the executable says so and exits with 3.
        Assert.Equal(3, ran);
        Assert.Contains("no desktop host", said, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_update_and_a_downgrade_while_MSBuild_is_warm_each_build_with_their_own_SDK()
    {
        Assert.Equal(0, (await consumer.FirstAsync()).Exit);
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

    /// <summary>A build of the app with node reuse and the MSBuild server on, both under this run's handshake salt.</summary>
    private Task<(int Exit, string Output)> WarmAsync(params string[] arguments)
    {
        var environment = new Dictionary<string, string>(consumer.Environment) { ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "true" };
        environment.Remove("MSBUILDDISABLENODEREUSE");
        return Tool.RunAsync(Path.Combine(consumer.Root, App), "dotnet", arguments, environment);
    }

    /// <summary>The SDK that generated the app's @spar/app package, from its sparSdk field; its version stays 0.0.0.</summary>
    private string ClientVersion() => ClientPackage().GetProperty("sparSdk").GetString()!;

    /// <summary>The app's generated package.json.</summary>
    private JsonElement ClientPackage()
    {
        var package = Path.Combine(consumer.Root, App, "obj", "spar", "client", "package.json");
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
