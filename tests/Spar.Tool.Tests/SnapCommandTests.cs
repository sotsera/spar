using Spar.Tool.Snap;

namespace Spar.Tool.Tests;

/// <summary>
/// <c>spar snap</c>'s parts that need no build (decision "M2c's decide-first questions", item 7): finding the test project
/// with scenes from an app's folder, its own folder or a project file, and the command lines it refuses. The captures
/// themselves are in <c>docs/evidence/m2c-spar-snap.md</c> and <c>Spar.WindowTests</c>' scene tests.
/// </summary>
public sealed class SnapCommandTests
{
    private const string Scene = """
        public static class Scenes
        {
            [Spar.Testing.SparScene("empty")]
            public static System.Threading.Tasks.Task Empty(Spar.Testing.SparScene scene) => System.Threading.Tasks.Task.CompletedTask;
        }
        """;

    [Fact]
    public void The_project_with_scenes_is_found_from_the_app_beside_it_from_its_folder_and_from_its_file()
    {
        using var folder = Repository.NewFolder("snap-find");
        folder.Write("App/App.csproj", "<Project Sdk=\"Spar.Sdk\" />");
        folder.Write("App.Tests/App.Tests.csproj", "<Project Sdk=\"Spar.Sdk\" />");
        folder.Write("App.Tests/Tests.cs", "public sealed class Tests { }");
        folder.Write("App.EndToEndTests/App.EndToEndTests.csproj", "<Project Sdk=\"Spar.Sdk\" />");
        folder.Write("App.EndToEndTests/Window/Scenes.cs", Scene);
        folder.Write("App.EndToEndTests/obj/Generated.cs", "[SparScene(\"not-read\")]");
        var expected = Path.Combine(folder.Path, "App.EndToEndTests", "App.EndToEndTests.csproj");

        var fromApp = SnapProject.Find(Path.Combine(folder.Path, "App"), out _);
        var fromFolder = SnapProject.Find(Path.Combine(folder.Path, "App.EndToEndTests"), out _);
        var fromFile = SnapProject.Find(expected, out _);
        var fromAppFile = SnapProject.Find(Path.Combine(folder.Path, "App", "App.csproj"), out _);

        Assert.Equal([expected, expected, expected, expected], [fromApp, fromFolder, fromFile, fromAppFile]);
    }

    [Fact]
    public void No_project_with_scenes_or_two_of_them_say_what_to_do()
    {
        using var folder = Repository.NewFolder("snap-none");
        folder.Write("App/App.csproj", "<Project Sdk=\"Spar.Sdk\" />");
        var none = SnapProject.Find(Path.Combine(folder.Path, "App"), out var nothing);
        folder.Write("One/One.csproj", "<Project />");
        folder.Write("One/Scenes.cs", Scene);
        folder.Write("Two/Two.csproj", "<Project />");
        folder.Write("Two/Scenes.cs", Scene);
        var two = SnapProject.Find(Path.Combine(folder.Path, "App"), out var several);

        Assert.Null(none);
        Assert.Contains("add a [SparScene(\"name\")] method", nothing, StringComparison.Ordinal);
        Assert.Null(two);
        Assert.Contains("several test projects have scenes (One.csproj, Two.csproj): name one with --project.", several, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData]
    [InlineData("--list", "--all")]
    [InlineData("empty", "--list")]
    [InlineData("--out")]
    [InlineData("--watch")]
    public void A_wrong_command_line_is_exit_code_64(params string[] arguments)
    {
        using var folder = Repository.NewFolder("snap-usage");
        var error = new StringWriter();

        var exit = SnapCommand.Run(arguments, new StringWriter(), error, folder.Path);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("Usage: spar snap", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_folder_without_scenes_is_an_error_before_anything_builds()
    {
        using var folder = Repository.NewFolder("snap-empty");
        var error = new StringWriter();

        var exit = SnapCommand.Run(["--list"], new StringWriter(), error, folder.Path);

        Assert.Equal(ExitCodes.Errors, exit);
        Assert.Contains("no test project with scenes was found", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_result_names_its_folder_and_counts_or_its_failure()
    {
        var written = new SnapResult("empty", "/app/obj/spar-snap/empty", true, [], 11, 1, 0);
        var failed = new SnapResult("groceries", "/app/obj/spar-snap/groceries", false, ["[FAIL] Scene_groceries"], 0, 0, 0);

        Assert.Equal("empty: /app/obj/spar-snap/empty (11 elements, 1 call, 0 problems)", written.Describe());
        Assert.Equal("groceries: failed", failed.Describe());
    }
}
