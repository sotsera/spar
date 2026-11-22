using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Spar.BuildTests;

/// <summary>
/// The console harness (<c>tests/apps/Spar.Harness</c>, plan M1a): the core, meaning the codec, a bridge session,
/// dispatch and the generated serializers, published as a Production NativeAOT executable with the bridge alive. It
/// must write exactly what its CoreCLR build writes, stay within its size budget, and carry none of the test host,
/// the loopback host or the test libraries even though the code around them is kept.
/// </summary>
public sealed class HarnessTests(Workspace workspace) : IDisposable
{
    private const string Harness = "tests/apps/Spar.Harness/Spar.Harness.csproj";

    /// <summary>Plan, "Provisional budgets": the NativeAOT console harness (decision "Console harness built; its budget raised to 3.5 MB; lone surrogates on read").</summary>
    private const long SizeBudget = 3_500_000;

    private static Task<Runs>? s_runs;

    [Fact]
    public async Task The_NativeAOT_harness_writes_what_its_CoreCLR_build_writes()
    {
        var runs = await RunsAsync();

        Assert.True(runs.CoreClr.Exit == 0, runs.CoreClr.Output);
        Assert.True(runs.Native.Exit == 0, runs.Native.Output);
        Assert.Equal(runs.CoreClr.Output, runs.Native.Output);

        // The output is the core's real behaviour, not an empty run that happens to match.
        string[] expected =
        [
            "< {\"type\":\"open\",\"protocol\":1,\"doc\":\"harness\"",
            "lone \\uFFFD end",
            "\"code\":\"refused\",\"reason\":\"said no\"",
            "\"code\":\"serialization\",\"execution\":\"completed\"",
            "{\"correlation\":\"4bf92f3577b34da6a3ce929d0e0e4736\"}",
            "\"code\":\"cancelled\",\"execution\":\"stopped\"",
            "\"code\":\"too-large\"",
            "\"code\":\"contract-mismatch\"",
        ];
        Assert.All(expected, text => Assert.Contains(text, runs.Native.Output, StringComparison.Ordinal));
        Assert.DoesNotContain("\"detail\"", runs.Native.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_Production_harness_fits_its_budget_and_carries_the_bridge_but_no_test_or_loopback_code()
    {
        var runs = await RunsAsync();
        var image = await File.ReadAllBytesAsync(runs.Executable, TestContext.Current.CancellationToken);

        TestContext.Current.SendDiagnosticMessage(string.Create(CultureInfo.InvariantCulture,
            $"NativeAOT harness: {image.Length:N0} bytes (budget {SizeBudget:N0})."));
        Assert.True(image.Length <= SizeBudget, $"The harness is {image.Length:N0} bytes, over its budget of {SizeBudget:N0}.");

        // The harness keeps the stall probe, so only the probe's markers may appear; the Notebook, which does not keep
        // it, must carry none of them (NotebookBuildTests).
        var probe = ProductionBoundary.StallProbe.Select(marker => marker.Text).ToList();
        var found = ProductionBoundary.Find(image).Except(probe).ToList();
        Assert.True(found.Count == 0, $"The Production harness contains: {string.Join(", ", found)}.");
        foreach (var (text, encoding) in ProductionBoundary.StallProbe)
        {
            Assert.True(ProductionBoundary.Contains(image, text, encoding), $"KeepStallProbe() did not keep {text} in the harness.");
        }

        // The control: the bridge survived trimming, so the absence above is not an empty program's.
        foreach (var kept in new[] { "BridgeSession", "SparDispatcher", "BridgeCodec" })
        {
            Assert.True(ProductionBoundary.Contains(image, kept, Encoding.UTF8), $"The harness has no {kept}.");
        }
    }

    [Fact]
    public async Task The_NativeAOT_executable_and_the_CoreCLR_assembly_carry_the_builds_identity()
    {
        var runs = await RunsAsync();
        var expected = await BuildIdentityExpectation.OfAsync(workspace, "Production");

        var (native, nativeFile, nativeProblem) = Spar.Tool.Identity.BuildIdentityReader.Read(runs.Executable);
        var (coreClr, coreClrFile, coreClrProblem) = Spar.Tool.Identity.BuildIdentityReader.Read(runs.Assembly);

        Assert.True(native is not null, nativeProblem);
        Assert.True(coreClr is not null, coreClrProblem);
        Assert.Equal(runs.Executable, nativeFile);
        Assert.Equal(runs.Assembly, coreClrFile);
        expected.Check(native);
        expected.Check(coreClr);
        Assert.Equal(native, coreClr);
    }

    public void Dispose() => workspace.Report(TestContext.Current.TestState?.Result == TestResult.Passed);

    private Task<Runs> RunsAsync() => s_runs ??= BuildAndRunAsync();

    /// <summary>The Release build on CoreCLR and the Production NativeAOT publish, each run on the shared vectors.</summary>
    private async Task<Runs> BuildAndRunAsync()
    {
        var vectors = Path.Combine(workspace.Root, "tests", "conformance", "bridge-messages.json");

        var built = await Dotnet.RunAsync(workspace, "harness-build", "build", Harness, "-c", "Release");
        Assert.True(built.Exit == 0, built.Output);
        var output = Path.Combine(workspace.Root, "tests", "apps", "Spar.Harness", "bin", "Production", "Release");
        var assembly = Assert.Single(Directory.GetFiles(output, "Spar.Harness.dll", SearchOption.AllDirectories));
        var coreClr = await RunAsync("dotnet", assembly, vectors);

        var publish = Path.Combine(workspace.Publish, "Spar.Harness");
        var published = await Dotnet.RunAsync(workspace, "harness-publish", "publish", Harness, "-c", "Release", "-o", publish);
        Assert.True(published.Exit == 0, published.Output);
        var executable = Path.Combine(publish, OperatingSystem.IsWindows() ? "Spar.Harness.exe" : "Spar.Harness");
        var native = await RunAsync(executable, vectors);

        return new Runs(coreClr, native, executable, assembly);
    }

    /// <summary>Runs a program and reads its standard output as the UTF-8 bytes it wrote, standard error apart.</summary>
    private static async Task<Run> RunAsync(string program, params string[] arguments)
    {
        var start = new ProcessStartInfo(program)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        var token = TestContext.Current.CancellationToken;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(token);
        var error = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token).WaitAsync(TimeSpan.FromMinutes(2), token);
        var errorText = await error;
        return new Run(process.ExitCode, await output + (errorText.Length == 0 ? "" : $"\n[stderr]\n{errorText}"));
    }

    private sealed record Run(int Exit, string Output);

    private sealed record Runs(Run CoreClr, Run Native, string Executable, string Assembly);
}
