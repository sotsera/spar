using Spar.Tool.Init;

namespace Spar.Tool.Tests;

/// <summary>
/// <c>spar init frontend</c>'s flow on app folders of their own, with a fake npm: the scaffolder writes Vite's react-ts
/// files as create-vite 9.3.4 does, and the install writes the lockfile and npm's record. The real scaffolder runs in
/// Spar.PackageTests.
/// </summary>
public sealed class InitCommandTests
{
    private const string ScaffoldConfig = "import react from '@vitejs/plugin-react'\nimport { defineConfig } from 'vite'\n\n// https://vite.dev/config/\nexport default defineConfig({\n  plugins: [react()],\n})\n";

    private const string ScaffoldPackage = "{\n  \"name\": \"frontend\",\n  \"private\": true,\n  \"type\": \"module\",\n  \"scripts\": {\n    \"dev\": \"vite\",\n    \"build\": \"tsc -b && vite build\"\n  },\n  \"dependencies\": {\n    \"react\": \"^19.3.1\"\n  },\n  \"devDependencies\": {\n    \"vite\": \"^8.3.6\"\n  }\n}\n";

    [Fact]
    public void Without_a_terminal_and_without_yes_it_shows_the_diff_and_writes_nothing()
    {
        using var app = UnwiredApp();
        var before = Snapshot(app);
        var (exit, output, npm) = Run(app, interactive: false, input: "", "frontend");

        Assert.Equal(ExitCodes.NotWritten, exit);
        Assert.Contains("+    \"@spar/app\": \"file:../obj/spar/client\",", output, StringComparison.Ordinal);
        Assert.Contains("+  plugins: [react(), spar()],", output, StringComparison.Ordinal);
        Assert.Contains("Nothing written", output, StringComparison.Ordinal);
        Assert.Empty(npm.Calls);
        Assert.Equal(before, Snapshot(app));
    }

    [Fact]
    public void Asked_in_a_terminal_it_writes_only_on_yes()
    {
        using var app = UnwiredApp();
        var before = Snapshot(app);

        var (declined, _, _) = Run(app, interactive: true, input: "n\n", "frontend");
        Assert.Equal(ExitCodes.NotWritten, declined);
        Assert.Equal(before, Snapshot(app));

        var (accepted, output, npm) = Run(app, interactive: true, input: "y\n", "frontend");
        Assert.Equal(ExitCodes.Success, accepted);
        Assert.Contains("Write these changes? [y/N]", output, StringComparison.Ordinal);
        Assert.Contains("\"@spar/app\": \"file:../obj/spar/client\"", app.Read("frontend/package.json"), StringComparison.Ordinal);
        Assert.Contains("import spar from '@spar/app/vite'", app.Read("frontend/vite.config.ts"), StringComparison.Ordinal);
        Assert.Equal(["install --ignore-scripts --no-audit --no-fund"], npm.Calls);
        Assert.Equal("{\n  \"name\": \"@spar/app\",\n  \"version\": \"0.0.0\"\n}\n", app.Read("obj/spar/client/package.json"));
    }

    [Fact]
    public void A_wired_frontend_has_nothing_to_do()
    {
        using var app = UnwiredApp();
        Run(app, interactive: false, input: "", "frontend", "--yes");

        var (exit, output, npm) = Run(app, interactive: false, input: "", "frontend");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("already wired", output, StringComparison.Ordinal);
        Assert.Empty(npm.Calls);
    }

    [Fact]
    public void A_frontend_in_another_folder_gets_SparFrontendPath_and_its_own_relative_dependency()
    {
        using var app = UnwiredApp(folder: "web/page");

        var (exit, _, _) = Run(app, interactive: false, input: "", "frontend", "--frontend", "web/page", "--yes");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("<SparFrontendPath>web/page</SparFrontendPath>", app.Read("MyApp.csproj"), StringComparison.Ordinal);
        Assert.Contains("\"@spar/app\": \"file:../../obj/spar/client\"", app.Read("web/page/package.json"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_framework_scaffolds_into_obj_first_then_moves_the_frontend_into_place_and_wires_it()
    {
        using var app = TemplateApp.New();
        Directory.Delete(Path.Combine(app.Path, "frontend"), recursive: true);

        var (exit, output, npm) = Run(app, interactive: false, input: "", "frontend", "--framework", "react", "--yes");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal($"create --yes --ignore-scripts vite@{InitCommand.CreateViteVersion} frontend -- --no-interactive --no-immediate --template react-ts", npm.Calls[0]);
        Assert.Contains(Path.Combine("obj", "spar", "init"), npm.Folders[0], StringComparison.Ordinal);
        Assert.Contains("New: frontend/, Vite's react-ts template", output, StringComparison.Ordinal);
        Assert.Contains("spar()", app.Read("frontend/vite.config.ts"), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(app.Path, "frontend", "package-lock.json")));
        Assert.False(Directory.Exists(Path.Combine(app.Path, "obj", "spar", "init")) && Directory.EnumerateFileSystemEntries(Path.Combine(app.Path, "obj", "spar", "init")).Any());
    }

    [Fact]
    public void A_framework_without_a_terminal_or_yes_runs_nothing_and_an_existing_folder_is_refused()
    {
        using var app = TemplateApp.New();

        var (refused, _, _) = Run(app, interactive: false, input: "", "frontend", "--framework", "vue", "--yes");
        Directory.Delete(Path.Combine(app.Path, "frontend"), recursive: true);
        var (notAsked, output, npm) = Run(app, interactive: false, input: "", "frontend", "--framework", "vue");

        Assert.Equal(ExitCodes.Errors, refused);
        Assert.Equal(ExitCodes.NotWritten, notAsked);
        Assert.Empty(npm.Calls);
        Assert.Contains("Nothing written", output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(app.Path, "frontend")));
    }

    [Fact]
    public void A_configuration_too_unusual_to_edit_gets_the_snippet_and_the_rest_is_written()
    {
        using var app = UnwiredApp();
        app.Write("frontend/vite.config.ts", "import { defineConfig } from 'vite'\nexport default defineConfig(({ mode }) => config(mode))\n");

        var (exit, output, _) = Run(app, interactive: false, input: "", "frontend", "--yes");

        Assert.Equal(ExitCodes.Partial, exit);
        Assert.Contains("import spar from \"@spar/app/vite\";", output, StringComparison.Ordinal);
        Assert.Contains("@spar/app", app.Read("frontend/package.json"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_folder_outside_the_app_or_a_wrong_option_is_refused_before_anything_runs()
    {
        using var app = UnwiredApp();

        Assert.Equal(ExitCodes.Usage, Run(app, interactive: false, input: "", "frontend", "--frontend", "../elsewhere", "--yes").Exit);
        Assert.Equal(ExitCodes.Usage, Run(app, interactive: false, input: "", "frontend", "--frontend", "obj/x", "--yes").Exit);
        Assert.Equal(ExitCodes.Usage, Run(app, interactive: false, input: "", "frontend", "--framework", "angular").Exit);
        Assert.Equal(ExitCodes.Usage, Run(app, interactive: false, input: "", "backend").Exit);
    }

    /// <summary>The template's app with its frontend replaced by Vite's react-ts scaffold, not yet wired.</summary>
    private static TestFolder UnwiredApp(string folder = "frontend")
    {
        var app = TemplateApp.New();
        Directory.Delete(Path.Combine(app.Path, "frontend"), recursive: true);
        app.Write($"{folder}/package.json", ScaffoldPackage);
        app.Write($"{folder}/vite.config.ts", ScaffoldConfig);
        app.Write($"{folder}/index.html", "<!doctype html>\n");
        return app;
    }

    private static (int Exit, string Output, FakeNpm Npm) Run(TestFolder app, bool interactive, string input, params string[] arguments)
    {
        var output = new StringWriter();
        var npm = new FakeNpm();
        var exit = InitCommand.Run(arguments, new InitConsole(output, output, new StringReader(input), interactive), npm, app.Path);
        return (exit, output.ToString(), npm);
    }

    private static string Snapshot(TestFolder app) => string.Join("\n", Directory.EnumerateFiles(app.Path, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal)
        .Select(file => file + ":" + File.ReadAllText(file).GetHashCode(StringComparison.Ordinal)));

    /// <summary>npm as the tests need it: the scaffolder's files, and an install's lockfile and record.</summary>
    private sealed class FakeNpm : INpm
    {
        public List<string> Calls { get; } = [];

        public List<string> Folders { get; } = [];

        public NpmResult Run(IReadOnlyList<string> arguments, string workingDirectory)
        {
            Calls.Add(string.Join(' ', arguments));
            Folders.Add(workingDirectory);
            if (arguments[0] == "create")
            {
                var target = Path.Combine(workingDirectory, arguments[4]);
                Directory.CreateDirectory(Path.Combine(target, "src"));
                File.WriteAllText(Path.Combine(target, "package.json"), ScaffoldPackage);
                File.WriteAllText(Path.Combine(target, "vite.config.ts"), ScaffoldConfig);
                File.WriteAllText(Path.Combine(target, "src", "main.tsx"), "export {};\n");
            }
            else if (arguments[0] == "install")
            {
                File.WriteAllText(Path.Combine(workingDirectory, "package-lock.json"), "{}\n");
                Directory.CreateDirectory(Path.Combine(workingDirectory, "node_modules"));
                File.WriteAllText(Path.Combine(workingDirectory, "node_modules", ".package-lock.json"), "{}\n");
            }

            return new NpmResult(0, "");
        }
    }
}
