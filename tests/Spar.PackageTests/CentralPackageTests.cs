using System.Text.Json;

namespace Spar.PackageTests;

/// <summary>
/// A consumer whose solution manages package versions centrally (<c>Directory.Packages.props</c>, with transitive
/// pinning). The packages Spar.Sdk adds itself carry their own version, so they must not trip the rule that a
/// centrally managed project gives no version on a <c>PackageReference</c> (NU1008), in either test lane, and the
/// solution's own pin of a package Spar uses must hold.
/// </summary>
public sealed class CentralPackageTests(Consumer consumer) : IDisposable
{
    private const string Tests = "App.Tests/Notes.App.Tests.csproj";

    [Fact]
    public async Task An_app_whose_solution_manages_package_versions_centrally_builds_and_tests()
    {
        var (exit, output) = await consumer.CentralDotnetAsync("", "test", "--project", Tests);

        Assert.True(exit == 0, output);
        Assert.Contains("succeeded: 1", output, StringComparison.Ordinal);
        var pinned = Path.Combine(consumer.CentralRoot, "Directory.Packages.props");
        var version = System.Xml.Linq.XDocument.Load(pinned).Descendants("PackageVersion").Single().Attribute("Version")!.Value;
        Assert.Contains($"Microsoft.Extensions.DependencyInjection/{version}", RestoredLibraries(Path.Combine("App", "obj")));
    }

    [Fact]
    public async Task The_NativeAOT_lane_restores_under_central_package_management()
    {
        var (exit, output) = await consumer.CentralDotnetAsync("", "restore", Tests, "-p:SparTestLane=Aot");

        Assert.True(exit == 0, output);
        Assert.Contains(RestoredLibraries(Path.Combine("App.Tests", "obj", "aot")), library => library.StartsWith("xunit.v3.aot/", StringComparison.Ordinal));
    }

    public void Dispose() => consumer.Report(TestContext.Current.TestState?.Result == TestResult.Passed);

    /// <summary>The packages a restore chose, as <c>id/version</c>, from the assets file in the <c>obj</c> folder <paramref name="folder"/>.</summary>
    private IReadOnlyList<string> RestoredLibraries(string folder)
    {
        var assets = Path.Combine(consumer.CentralRoot, folder, "project.assets.json");
        using var json = JsonDocument.Parse(File.ReadAllText(assets));
        return [.. json.RootElement.GetProperty("libraries").EnumerateObject().Select(library => library.Name)];
    }
}
