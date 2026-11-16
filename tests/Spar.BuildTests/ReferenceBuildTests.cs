namespace Spar.BuildTests;

/// <summary>
/// An app's own library, built for the app's test project: the app builds in the Test variant, but the variant and the
/// test lane mean nothing to a plain library, so it is built once, as it would be anywhere. Given them, it was built
/// once per variant into the same obj/, and the two compilations could collide (CS2012).
/// </summary>
public sealed class ReferenceBuildTests(Workspace workspace)
{
    private const string Tests = "tests/WireShapes.Tests/WireShapes.Tests.csproj";

    [Fact]
    public async Task A_library_of_the_tested_app_is_compiled_once()
    {
        var build = await Dotnet.RunAsync(workspace, "wireshapes-tests", "build", Tests);

        Assert.True(build.Exit == 0, build.Output);
        var library = Path.Combine(workspace.Root, "tests", "apps", "WireShapes.Library", "WireShapes.Library.csproj");
        Assert.Equal(1, build.Compilations.GetValueOrDefault(library));
        Assert.All(build.Compilations, compiled => Assert.True(compiled.Value == 1, $"{compiled.Key} was compiled {compiled.Value} times."));
    }
}
