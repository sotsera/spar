using System.Text;
using System.Text.Json;
using Spar.Runtime;
using Spar.Tool.Doctor;
using Spar.Tool.Init;
using Spar.Tool.Machine;

namespace Spar.Tool.Tests;

/// <summary>The command line: doctor's report in text and JSON, its exit codes, the identity it reads, help and the version.</summary>
public sealed class DoctorCommandTests
{
    [Fact]
    public void A_good_machine_and_a_built_app_give_exit_code_0_and_the_identity_in_JSON()
    {
        using var app = TemplateApp.Built();
        WriteBuild(app, new BuildIdentity("Development", null, null, "0123456789abcdef"));

        var (exit, output) = Doctor(app, GoodMachine(), "--json");

        Assert.Equal(ExitCodes.Success, exit);
        using var json = JsonDocument.Parse(output);
        var root = json.RootElement;
        Assert.Equal("spar", root.GetProperty("tool").GetString());
        Assert.Equal(0, root.GetProperty("errors").GetInt32());
        Assert.Equal(0, root.GetProperty("exitCode").GetInt32());
        Assert.Equal("Development", root.GetProperty("identity").GetProperty("variant").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("identity").GetProperty("revision").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("identity").GetProperty("dirty").ValueKind);
        var first = root.GetProperty("findings")[0];
        Assert.Equal(["id", "area", "severity", "message", "fix"], first.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public void A_warning_is_exit_code_1_and_an_error_2_with_the_fix_under_it_in_text()
    {
        using var app = TemplateApp.Built();
        Directory.Delete(Path.Combine(app.Path, "frontend", "node_modules"), recursive: true);

        var (warned, text) = Doctor(app, GoodMachine());
        File.Delete(Path.Combine(app.Path, "frontend", "package-lock.json"));
        var (failed, _) = Doctor(app, GoodMachine());

        Assert.Equal(ExitCodes.Warnings, warned);
        Assert.Contains("  warn  frontend-node-modules", text, StringComparison.Ordinal);
        Assert.Contains("        fix: Build the app (dotnet build)", text, StringComparison.Ordinal);
        Assert.Matches(@"\n0 errors, 1 warning; \d+ checks passed \(spar doctor --verbose lists them\)\.\n$", text.ReplaceLineEndings("\n"));
        Assert.Equal(ExitCodes.Errors, failed);
    }

    [Fact]
    public void Text_shows_what_needs_attention_and_the_project_and_verbose_shows_every_check()
    {
        using var app = TemplateApp.Built();
        Directory.Delete(Path.Combine(app.Path, "frontend", "node_modules"), recursive: true);

        var (_, text) = Doctor(app, GoodMachine());
        var (verboseExit, verbose) = Doctor(app, GoodMachine(), "--verbose");
        var findings = DoctorCommand.Diagnose(GoodMachine(), app.Path, null, null, "0.1.0-dev.7").Findings;

        var lines = text.ReplaceLineEndings("\n").Split('\n');
        Assert.Equal("spar doctor 0.1.0-dev.7", lines[0]);
        Assert.StartsWith("Project: MyApp, ", lines[1], StringComparison.Ordinal);
        Assert.DoesNotContain(lines, line => line.StartsWith("  info ", StringComparison.Ordinal));
        Assert.Single(lines, line => line.StartsWith("  warn  frontend-node-modules", StringComparison.Ordinal));
        Assert.EndsWith($"{findings.Count(f => f.Severity == Severity.Info)} checks passed (spar doctor --verbose lists them).", lines[^2], StringComparison.Ordinal);

        Assert.Equal(ExitCodes.Warnings, verboseExit);
        string[] labels = ["  info ", "  warn ", "  error"];
        var findingLines = verbose.ReplaceLineEndings("\n").Split('\n')
            .Count(line => labels.Any(label => line.StartsWith(label, StringComparison.Ordinal)));
        Assert.Equal(findings.Count, findingLines);
        Assert.EndsWith("0 errors, 1 warning.\n", verbose.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_build_whose_contracts_differ_from_the_page_s_client_and_a_dirty_Production_build_are_warnings()
    {
        using var app = TemplateApp.Built();
        WriteBuild(app, new BuildIdentity("Production", "abc1234", true, "ffffffffffffffff"));

        var findings = DoctorCommand.Diagnose(GoodMachine(), app.Path, null, null, "0.1.0-dev.7").Findings;

        Assert.Equal(Severity.Warning, Assert.Single(findings, f => f.Id == FindingIds.IdentityContracts).Severity);
        Assert.Equal(Severity.Warning, Assert.Single(findings, f => f.Id == FindingIds.IdentityDirty).Severity);
    }

    [Fact]
    public void App_names_the_file_and_a_file_without_an_identity_is_a_warning()
    {
        using var app = TemplateApp.Built();
        var other = app.Write("bin/elsewhere/Tool.dll", "no identity here");

        var report = DoctorCommand.Diagnose(GoodMachine(), app.Path, null, other, "0.1.0-dev.7");

        Assert.Equal(Severity.Warning, Assert.Single(report.Findings, f => f.Id == FindingIds.Identity).Severity);
        Assert.Null(report.Identity);
        Assert.Contains(report.Findings, f => f.Id == FindingIds.Project && f.Severity == Severity.Info);
    }

    [Fact]
    public void Outside_an_app_the_project_checks_are_skipped_and_a_wrong_project_is_an_error()
    {
        using var empty = Repository.NewFolder("empty");

        var skipped = DoctorCommand.Diagnose(GoodMachine(), empty.Path, null, null, "0.1.0-dev.7");
        var wrong = DoctorCommand.Diagnose(GoodMachine(), empty.Path, "nowhere", null, "0.1.0-dev.7");

        Assert.Equal(Severity.Info, Assert.Single(skipped.Findings, f => f.Id == FindingIds.Project).Severity);
        Assert.Equal(Severity.Error, Assert.Single(wrong.Findings, f => f.Id == FindingIds.Project).Severity);
    }

    [Fact]
    public void Help_the_version_and_a_wrong_command_line_have_their_exit_codes()
    {
        Assert.Equal((ExitCodes.Success, "0.1.0-dev.7+abc\n"), Cli("--version"));
        Assert.Equal(ExitCodes.Success, Cli("doctor", "--help").Exit);
        Assert.Equal(ExitCodes.Success, Cli("init", "frontend", "--help").Exit);
        Assert.Equal(ExitCodes.Usage, Cli().Exit);
        Assert.Equal(ExitCodes.Usage, Cli("cure").Exit);
        Assert.Equal(ExitCodes.Usage, Cli("doctor", "--app").Exit);
    }

    private static (int Exit, string Output) Cli(params string[] arguments)
    {
        var output = new StringWriter();
        var exit = Tool.Cli.Run(arguments, "0.1.0-dev.7+abc", output, output, new StringReader(""), false, GoodMachine(), new RealNpm(), Repository.Root);
        return (exit, output.ToString().ReplaceLineEndings("\n"));
    }

    private static (int Exit, string Output) Doctor(TestFolder app, FakeMachine machine, params string[] arguments)
    {
        var output = new StringWriter();
        var exit = DoctorCommand.Run(arguments, machine, output, output, "0.1.0-dev.7", app.Path);
        return (exit, output.ToString());
    }

    /// <summary>A build's DLL under the app's bin/ with the identity's block, as the generator writes it.</summary>
    private static void WriteBuild(TestFolder app, BuildIdentity identity)
    {
        var dll = Path.Combine(app.Path, "bin", "Development", "Debug", "net11.0", "MyApp.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(dll)!);
        File.WriteAllBytes(dll, [.. new byte[512], .. Encoding.ASCII.GetBytes(identity.ToBlock()), .. new byte[512]]);
    }

    /// <summary>A Linux machine with everything: the SDK at global.json's version, Spar.Sdk restored, Node, npm, the libraries.</summary>
    private static FakeMachine GoodMachine()
    {
        var machine = new FakeMachine { Platform = Platform.Linux };
        machine.Variables["PATH"] = "/usr/bin";
        machine.Variables["NUGET_PACKAGES"] = "/nuget";
        machine.Texts["/etc/os-release"] = "ID=ubuntu\n";
        machine.Libraries["libgtk-4.so.1"] = new LibraryAnswer(true, new Version(4, 14, 2));
        machine.Libraries["libwebkitgtk-6.0.so.4"] = new LibraryAnswer(true, new Version(2, 50, 1));
        machine.Libraries["libssl.so.3"] = new LibraryAnswer(true, null);
        machine.WithProgram("dotnet", "--version", FakeMachine.Says("11.0.100"))
            .WithProgram("/usr/bin/node", "--version", FakeMachine.Says("v24.21.0"))
            .WithProgram("/usr/bin/npm", "--version", FakeMachine.Says("11.19.0"))
            .WithFile("/usr/bin/bwrap", "/usr/bin/xdg-dbus-proxy", "/usr/bin/clang", "/usr/lib/x86_64-linux-gnu/libz.so", "/usr/lib/x86_64-linux-gnu/libicuuc.so.74");
        return machine;
    }
}
