using System.Text;
using System.Text.Json;
using Spar.Tool.Dev;

namespace Spar.Tool.Tests;

/// <summary>
/// <c>spar dev</c>'s parts that need no running app (decision "M2b's decide-first questions"): MSBuild's diagnostic
/// lines, the launch profiles, the build's properties, the run's copy, the session file, the reload's answer and the
/// command lines. The supervisor against real apps is in <c>docs/evidence/m2b-spar-dev.md</c>.
/// </summary>
public sealed class DevCommandTests
{
    [Theory]
    [InlineData(@"E:\app\Program.cs(3,35): error CS1002: ; expected [E:\app\app.csproj]", "error", "CS1002", "; expected", @"E:\app\Program.cs", 3, 35)]
    [InlineData(@"   1:7>E:\app\Pages.cs(12,5): warning CS0168: The variable 'x' is declared but never used [E:\app\app.csproj]", "warning", "CS0168", "The variable 'x' is declared but never used", @"E:\app\Pages.cs", 12, 5)]
    [InlineData("/home/u/app/frontend/src/App.tsx(7): error TS2322: Type 'string' is not assignable to type 'number'. [/home/u/app/app.csproj]", "error", "TS2322", "Type 'string' is not assignable to type 'number'.", "/home/u/app/frontend/src/App.tsx", 7, null)]
    [InlineData(@"E:\app\Contracts.cs(4,1,4,20): error SPAR001: A contract needs a command.", "error", "SPAR001", "A contract needs a command.", @"E:\app\Contracts.cs", 4, 1)]
    public void A_diagnostic_line_gives_its_place_code_and_message(string line, string severity, string code, string message, string file, int lineNumber, int? column)
    {
        var diagnostic = BuildDiagnostics.ParseLine(line);

        Assert.NotNull(diagnostic);
        Assert.Equal((severity, code, message, file, lineNumber, column), (diagnostic.Severity, diagnostic.Code, diagnostic.Message, diagnostic.File, diagnostic.Line ?? 0, diagnostic.Column));
    }

    [Fact]
    public void A_tool_diagnostic_has_no_file_and_repeats_are_listed_once()
    {
        string[] lines =
        [
            "MSBUILD : error MSB1009: Project file does not exist.",
            "CSC : warning CS8032: An instance of analyzer cannot be created.",
            "CSC : warning CS8032: An instance of analyzer cannot be created.",
            "Build started 26/11/2026 10:00:00.",
        ];

        var diagnostics = BuildDiagnostics.Parse(lines);

        Assert.Equal(2, diagnostics.Count);
        Assert.Equal(("MSB1009", (string?)null), (diagnostics[0].Code, diagnostics[0].File));
        Assert.Equal("warning", diagnostics[1].Severity);
    }

    [Fact]
    public void A_relative_file_is_the_project_folders_and_an_exec_failure_after_the_tools_own_errors_is_left_out()
    {
        using var folder = Repository.NewFolder("dev-tidy");
        BuildDiagnostic[] parsed =
        [
            new("error", "TS2322", "Type 'string' is not assignable to type 'number'.", "frontend/src/main.tsx", 27, 14, null),
            new("error", "MSB3073", "The command \"node … tsc\" exited with code 1.", Path.Combine(folder.Path, "Sdk.targets"), 785, 5, null),
        ];
        BuildDiagnostic[] alone = [parsed[1]];

        var tidy = AppBuild.Tidy(parsed, folder.Path);
        var kept = AppBuild.Tidy(alone, folder.Path);

        var only = Assert.Single(tidy);
        Assert.Equal(Path.Combine(folder.Path, "frontend", "src", "main.tsx"), only.File);
        Assert.Equal("MSB3073", Assert.Single(kept).Code);
    }

    [Fact]
    public void The_first_profile_that_runs_the_project_is_the_default_and_a_named_one_is_found()
    {
        var json = """
            {
              // A comment, as the IDEs allow.
              "profiles": {
                "Docker": { "commandName": "Docker" },
                "Desktop": { "commandName": "Project", "commandLineArgs": "--open \"My notes\" --fast", "workingDirectory": "data" },
                "Loopback": { "commandName": "Project", "environmentVariables": { "SPAR_HOST": "Loopback" } },
              }
            }
            """;

        var first = LaunchProfile.Parse(Encoding.UTF8.GetBytes(json), null, "launchSettings.json", out _);
        var loopback = LaunchProfile.Parse(Encoding.UTF8.GetBytes(json), "Loopback", "launchSettings.json", out _);
        var missing = LaunchProfile.Parse(Encoding.UTF8.GetBytes(json), "Docker", "launchSettings.json", out var problem);

        Assert.Equal("Desktop", first?.Name);
        Assert.Equal(["--open", "My notes", "--fast"], first?.Arguments);
        Assert.Equal("data", first?.WorkingDirectory);
        Assert.Equal("Loopback", loopback?.Environment["SPAR_HOST"]);
        Assert.Null(missing);
        Assert.Contains("it has: Desktop, Loopback", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_project_without_launch_settings_runs_with_no_profile_unless_one_is_named()
    {
        using var folder = Repository.NewFolder("dev-profile");

        var none = LaunchProfile.Find(folder.Path, null, out _);
        var named = LaunchProfile.Find(folder.Path, "Loopback", out var problem);

        Assert.Same(LaunchProfile.None, none);
        Assert.Null(named);
        Assert.Contains("there is no launch profile 'Loopback'", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void The_build_properties_come_from_their_block_among_the_build_output()
    {
        string[] lines =
        [
            "{",
            "  \"Properties\": {",
            "    \"RunCommand\": \"E:\\\\app\\\\bin\\\\Development\\\\Debug\\\\net11.0\\\\win-x64\\\\App.exe\",",
            "    \"RunArguments\": \"\",",
            "    \"TargetDir\": \"E:\\\\app\\\\bin\\\\Development\\\\Debug\\\\net11.0\\\\win-x64\\\\\"",
            "  }",
            "}",
            @"E:\app\Program.cs(3,26): warning CS8602: Dereference of a possibly null reference. [E:\app\app.csproj]",
        ];

        var (executable, directory) = AppBuild.Properties(lines);

        Assert.Equal(@"E:\app\bin\Development\Debug\net11.0\win-x64\App.exe", executable);
        Assert.Equal(@"E:\app\bin\Development\Debug\net11.0\win-x64\", directory);
    }

    [Fact]
    public void The_run_copy_takes_turns_and_copies_only_what_changed()
    {
        using var folder = Repository.NewFolder("dev-copy");
        var output = Path.Combine(folder.Path, "bin");
        folder.Write("bin/App.dll", "one");
        folder.Write("bin/runtimes/win-x64/native/Loader.dll", "native");
        folder.Write("bin/Old.dll", "old");

        var first = RunCopy.NextSlot(folder.Path, running: null);
        var copiedFirst = RunCopy.Sync(output, first);
        File.Delete(Path.Combine(output, "Old.dll"));
        File.WriteAllText(Path.Combine(output, "App.dll"), "two");
        var copiedAgain = RunCopy.Sync(output, first);
        var second = RunCopy.NextSlot(folder.Path, running: Path.Combine(first, "App.exe"));

        Assert.Equal((3, 1), (copiedFirst, copiedAgain));
        Assert.Equal("two", File.ReadAllText(Path.Combine(first, "App.dll")));
        Assert.True(File.Exists(Path.Combine(first, "runtimes", "win-x64", "native", "Loader.dll")));
        Assert.False(File.Exists(Path.Combine(first, "Old.dll")));
        Assert.NotEqual(first, second);
        Assert.Equal(first, RunCopy.NextSlot(folder.Path, running: Path.Combine(second, "App.exe")));
    }

    [Fact]
    public void The_session_file_round_trips_and_is_deleted_only_by_its_own_session()
    {
        using var folder = Repository.NewFolder("dev-file");
        var path = DevFile.PathFor(folder.Path);
        var mine = new DevFile(4242, DateTimeOffset.UtcNow, "pipe", "spar-dev-abc", "token-1", "Loopback", Private: true);
        var other = mine with { Token = "token-2" };

        Assert.True(mine.Write(path));
        var read = DevFile.Read(path);
        other.DeleteIfOurs(path);
        var kept = File.Exists(path);
        mine.DeleteIfOurs(path);

        Assert.Equal(("spar-dev-abc", "Loopback", true), (read?.Address, read?.Profile, read?.Private));
        Assert.True(kept);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void A_failed_reload_lists_each_error_at_its_place_and_keeps_the_running_app()
    {
        var build = new BuildResult(
            false, 1, TimeSpan.FromSeconds(2.34),
            [new BuildDiagnostic("error", "CS1002", "; expected", @"E:\app\Program.cs", 3, 35, null), new BuildDiagnostic("warning", "CS0168", "unused", @"E:\app\Pages.cs", 2, null, null)],
            null, null);
        var result = new ReloadResult(build, ReloadResult.Kept, 1234, "5177cff9", null, null, null, [], null);

        using var document = Json(result);
        var text = ReloadResult.Describe(document.RootElement);

        Assert.Equal(ExitCodes.Errors, result.ExitCodeForTool);
        Assert.Equal(
            "build failed in 2.3 s (1 error, 1 warning)\n"
            + "  E:\\app\\Program.cs(3,35): error CS1002: ; expected\n"
            + "  E:\\app\\Pages.cs(2): warning CS0168: unused\n"
            + "the previous run is still running (pid 1234, run 5177cff9): fix the errors and reload",
            text);
        Assert.Equal(3, document.RootElement.GetProperty("build").GetProperty("errors")[0].GetProperty("line").GetInt32());
    }

    [Fact]
    public void A_good_reload_names_the_new_run_and_a_failed_start_its_last_lines()
    {
        var build = new BuildResult(true, 0, TimeSpan.FromSeconds(1.5), [], @"E:\app\App.exe", @"E:\app\");
        var ready = new ReloadResult(build, ReloadResult.Ready, 77, "0f3a9c21", 412.5, 1.25, null, [], "11111111");
        var exited = new ReloadResult(build, ReloadResult.Exited, 78, null, null, 0.4, 5, ["spar fail +12.0 ms: [22222222] The WebView2 Runtime is broken."], null);

        using var readyJson = Json(ready);
        using var exitedJson = Json(exited);

        Assert.Equal(ExitCodes.Success, ready.ExitCodeForTool);
        Assert.Equal(ExitCodes.Errors, exited.ExitCodeForTool);
        Assert.EndsWith("running: pid 77, run 0f3a9c21, ui-ready at 412.5 ms (1.3 s from its start); the previous run 11111111", ReloadResult.Describe(readyJson.RootElement), StringComparison.Ordinal);
        Assert.EndsWith("exited with 5 before its page was ready; its last lines:\n  | spar fail +12.0 ms: [22222222] The WebView2 Runtime is broken.", ReloadResult.Describe(exitedJson.RootElement), StringComparison.Ordinal);
    }

    [Fact]
    public void A_loopback_run_is_ready_once_it_listens_for_its_page_in_a_browser()
    {
        var build = new BuildResult(true, 0, TimeSpan.FromSeconds(2.8), [], @"E:\app\App.exe", @"E:\app\");
        var listening = new ReloadResult(build, ReloadResult.Ready, 77, "a4343afc", 156.1, 0.3, null, [], null, "loopback listening");

        using var json = Json(listening);

        Assert.Equal("loopback listening", json.RootElement.GetProperty("app").GetProperty("readyPhase").GetString());
        Assert.EndsWith("running: pid 77, run a4343afc, listening for its page in a browser at 156.1 ms (0.3 s from its start)", ReloadResult.Describe(json.RootElement), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("dev", "--launch-profile")]
    [InlineData("dev", "--watch")]
    [InlineData("reload", "--private")]
    public void A_wrong_command_line_is_exit_code_64(params string[] arguments)
    {
        using var app = TemplateApp.New();
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = arguments[0] == "dev"
            ? DevCommand.RunDev(arguments[1..], output, error, app.Path)
            : DevCommand.RunReload(arguments[1..], output, error, app.Path);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("Usage: spar " + arguments[0], error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Reload_without_a_running_session_says_how_to_start_one()
    {
        using var app = TemplateApp.New();
        var error = new StringWriter();
        new DevFile(int.MaxValue, DateTimeOffset.UtcNow, "pipe", "spar-dev-gone", "token", "Desktop", Private: false).Write(DevFile.PathFor(app.Path));

        var exit = DevCommand.RunReload([], new StringWriter(), error, app.Path);

        Assert.Equal(ExitCodes.Errors, exit);
        Assert.Contains("no spar dev runs for", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("Start one with spar dev.", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_named_profile_the_project_lacks_is_a_usage_error_before_anything_starts()
    {
        using var app = TemplateApp.New();
        var error = new StringWriter();

        var exit = DevCommand.RunDev(["-lp", "NoSuchProfile"], new StringWriter(), error, app.Path);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("no profile 'NoSuchProfile'", error.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(DevFile.PathFor(app.Path)));
    }

    private static JsonDocument Json(ReloadResult result)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            result.Write(writer);
        }

        return JsonDocument.Parse(buffer.WrittenMemory);
    }
}
