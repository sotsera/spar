namespace Spar.Generator.Tests;

/// <summary>
/// Scenes (decision "M2c's decide-first questions", item 1): the generator registers each valid <c>[SparScene]</c>
/// method in a module initializer and, in an xUnit project, writes a window test per scene; the analyzer reports a scene
/// of the wrong shape (SPAR110), a wrong name (SPAR111) and two scenes with one name (SPAR112), which the generator
/// leaves out. Spar.Testing and xUnit are stand-ins here: the generator reads them by name.
/// </summary>
public sealed class SceneTests
{
    private const string Testing = """
        namespace Spar.Testing
        {
            [System.AttributeUsage(System.AttributeTargets.Method)]
            public sealed class SparSceneAttribute(string name) : System.Attribute { public string Name { get; } = name; }
            public sealed class SparScene { }
            public static class SparScenes
            {
                public static void Register(string name, System.Func<SparScene, System.Threading.Tasks.Task> run) { }
                public static void RunIfAsked() { }
                public static System.Threading.Tasks.Task RunAsTestAsync(string name, System.Threading.CancellationToken token) => System.Threading.Tasks.Task.CompletedTask;
            }
        }
        """;

    private const string Xunit = """
        namespace Xunit
        {
            public sealed class FactAttribute : System.Attribute { }
            public sealed class TraitAttribute(string name, string value) : System.Attribute { }
            public sealed class TestContext
            {
                public static TestContext Current { get; } = new();
                public System.Threading.CancellationToken CancellationToken => default;
            }
        }
        """;

    [Fact]
    public void Valid_scenes_are_registered_by_name_and_each_is_a_window_test()
    {
        var run = GeneratorHarness.Generate(Testing + Xunit + """
            namespace App
            {
                using Spar.Testing;
                public static class Scenes
                {
                    [SparScene("new-page-dialog")]
                    public static System.Threading.Tasks.Task NewPage(SparScene scene) => System.Threading.Tasks.Task.CompletedTask;

                    [SparScene("empty")]
                    internal static System.Threading.Tasks.Task Empty(SparScene scene) => System.Threading.Tasks.Task.CompletedTask;
                }

                public static class Program { public static void Main() { } }
            }
            """);

        Assert.Empty(run.Errors);
        var scenes = run.Files["Spar.Scenes.g.cs"];
        Assert.Contains("[global::System.Runtime.CompilerServices.ModuleInitializer]", scenes, StringComparison.Ordinal);
        var empty = scenes.IndexOf("Register(\"empty\", global::App.Scenes.Empty)", StringComparison.Ordinal);
        var newPage = scenes.IndexOf("Register(\"new-page-dialog\", global::App.Scenes.NewPage)", StringComparison.Ordinal);
        Assert.True(empty > 0 && newPage > empty, scenes);
        Assert.Contains("SparScenes.RunIfAsked();", scenes, StringComparison.Ordinal);
        Assert.Contains("[global::Xunit.Trait(\"Surface\", \"Window\")]", scenes, StringComparison.Ordinal);
        Assert.Contains("public global::System.Threading.Tasks.Task Scene_new_page_dialog() =>", scenes, StringComparison.Ordinal);
        Assert.Contains("[global::Xunit.Trait(\"SparScene\", \"empty\")]", scenes, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', scenes);
    }

    // The harness compiles against the test process's own assemblies, xUnit's among them, so a project without xUnit
    // cannot be made here; SceneEmitter leaves the tests out when Xunit.FactAttribute is missing.
    [Fact]
    public void Without_the_test_host_there_is_no_file()
    {
        const string Scene = """
            namespace App
            {
                public static class Scenes
                {
                    [Spar.Testing.SparScene("empty")]
                    public static System.Threading.Tasks.Task Empty(Spar.Testing.SparScene scene) => System.Threading.Tasks.Task.CompletedTask;
                }

                public static class Program { public static void Main() { } }
            }
            """;

        var plain = GeneratorHarness.Generate(Testing + Scene);
        var none = GeneratorHarness.Generate("namespace App { public static class Program { public static void Main() { } } }");

        Assert.Empty(plain.Errors);
        Assert.Contains("Register(\"empty\"", plain.Files["Spar.Scenes.g.cs"], StringComparison.Ordinal);
        Assert.False(none.Files.ContainsKey("Spar.Scenes.g.cs"));
    }

    [Fact]
    public async Task A_scene_of_the_wrong_shape_or_name_is_reported_and_left_out()
    {
        const string Scenes = """
            namespace App
            {
                using System.Threading.Tasks;
                using Spar.Testing;
                public class Scenes
                {
                    [SparScene("instance")] public Task Instance(SparScene scene) => Task.CompletedTask;
                    [SparScene("hidden")] private static Task Hidden(SparScene scene) => Task.CompletedTask;
                    [SparScene("value")] public static ValueTask Value(SparScene scene) => default;
                    [SparScene("no-scene")] public static Task NoScene() => Task.CompletedTask;
                    [SparScene("Bad_Name")] public static Task BadName(SparScene scene) => Task.CompletedTask;
                    [SparScene("twice")] public static Task First(SparScene scene) => Task.CompletedTask;
                    [SparScene("twice")] public static Task Second(SparScene scene) => Task.CompletedTask;
                    [SparScene("fine")] public static Task Fine(SparScene scene) => Task.CompletedTask;
                }

                public static class Program { public static void Main() { } }
            }
            """;

        var messages = await GeneratorHarness.MessagesAsync(Testing + Scenes);
        var run = GeneratorHarness.Generate(Testing + Scenes);

        Assert.Equal(
            [
                "SPAR110: Scene method 'Instance' cannot be a scene: it is not static. A scene is public static Task Name(SparScene scene), so spar snap and its test can run it.",
                "SPAR110: Scene method 'Hidden' cannot be a scene: Hidden is private, so the project's generated code cannot call it. A scene is public static Task Name(SparScene scene), so spar snap and its test can run it.",
                "SPAR110: Scene method 'Value' cannot be a scene: it returns System.Threading.Tasks.ValueTask, not Task. A scene is public static Task Name(SparScene scene), so spar snap and its test can run it.",
                "SPAR110: Scene method 'NoScene' cannot be a scene: it does not take exactly one SparScene. A scene is public static Task Name(SparScene scene), so spar snap and its test can run it.",
                "SPAR111: Scene name 'Bad_Name' is not lower case letters, digits and single hyphens (such as new-page-dialog): spar snap takes it on the command line and names its folder after it",
                "SPAR112: Two scenes are named 'twice': spar snap names a scene, so each name is used once in the project",
                "SPAR112: Two scenes are named 'twice': spar snap names a scene, so each name is used once in the project",
            ],
            messages);
        var registered = run.Files["Spar.Scenes.g.cs"];
        Assert.Contains("Register(\"fine\"", registered, StringComparison.Ordinal);
        foreach (var left in new[] { "instance", "hidden", "value", "no-scene", "Bad_Name", "twice" })
        {
            Assert.DoesNotContain($"Register(\"{left}\"", registered, StringComparison.Ordinal);
        }
    }
}
