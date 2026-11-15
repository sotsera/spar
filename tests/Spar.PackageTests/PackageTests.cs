using System.Text.Json;

namespace Spar.PackageTests;

/// <summary>
/// An app and its tests built from Spar's packages alone (plan M1a, "the package-only consumer test"): they build and
/// pass, the packaged analyzer reports a contract problem at its line, a design-time build runs no Spar step, the app
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
        Assert.True(File.Exists(Path.Combine(consumer.Root, "App", "Notes.App.contracts.snapshot")), "No contract snapshot.");
        var restored = Path.Combine(consumer.Environment["NUGET_PACKAGES"], "spar", consumer.First.ToLowerInvariant());
        Assert.True(Directory.Exists(restored), $"Spar {consumer.First} was not restored from the run's feed.");
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

        var (exit, output) = await consumer.DotnetAsync("App", "publish", "-c", "Release", "-o", publish, "-nologo");

        Assert.True(exit == 0, output);
        var executable = Path.Combine(publish, OperatingSystem.IsWindows() ? "NotesTool.exe" : "NotesTool");
        var (ran, said) = await Tool.RunAsync(publish, executable, [], new Dictionary<string, string>());

        // M1a has no desktop host: the executable says so and exits with 2.
        Assert.Equal(2, ran);
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

    /// <summary>The version of the app's generated @spar/app package, which is the SDK's that built it.</summary>
    private string ClientVersion()
    {
        var package = Path.Combine(consumer.Root, "App", "obj", "spar", "client", "package.json");
        return JsonDocument.Parse(File.ReadAllText(package)).RootElement.GetProperty("version").GetString()!;
    }
}
