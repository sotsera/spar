using Spar.Tool.Doctor;
using Spar.Tool.Project;

namespace Spar.Tool.Tests;

/// <summary>
/// Doctor's project checks on a copy of the app template's app (<c>src/Spar.Templates/content/spar-app/MyApp</c>), as
/// <c>dotnet new spar-app</c> makes it, with what a build adds (the installed packages, the snapshot) written by hand.
/// </summary>
public sealed class ProjectCheckTests
{
    [Fact]
    public void A_built_app_from_the_template_has_nothing_to_fix()
    {
        using var folder = TemplateApp.Built();

        var findings = ProjectChecks.Check(Find(folder)).ToList();

        Assert.All(findings, finding => Assert.True(finding.Severity == Severity.Info, $"{finding.Id}: {finding.Message}"));
        Assert.Equal(
            [FindingIds.FrontendFolder, FindingIds.SparDependency, FindingIds.Scripts, FindingIds.VitePlugin, FindingIds.Lockfile, FindingIds.NodeModules, FindingIds.ContractSnapshot, FindingIds.LaunchProfiles],
            findings.Select(f => f.Id));
    }

    [Fact]
    public void A_removed_node_modules_is_a_warning_that_the_next_build_installs_it()
    {
        using var folder = TemplateApp.Built();
        Directory.Delete(Path.Combine(folder.Path, "frontend", "node_modules"), recursive: true);

        var finding = Assert.Single(ProjectChecks.Check(Find(folder)), f => f.Id == FindingIds.NodeModules);

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Contains("missing", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_lockfile_newer_than_the_install_is_the_same_rule_as_the_builds()
    {
        using var folder = TemplateApp.Built();
        File.SetLastWriteTimeUtc(Path.Combine(folder.Path, "frontend", "package-lock.json"), DateTime.UtcNow.AddMinutes(1));

        var finding = Assert.Single(ProjectChecks.Check(Find(folder)), f => f.Id == FindingIds.NodeModules);

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Contains("older than package-lock.json", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void No_lockfile_no_Spar_dependency_and_no_plugin_are_errors()
    {
        using var folder = TemplateApp.Built();
        File.Delete(Path.Combine(folder.Path, "frontend", "package-lock.json"));
        folder.Write("frontend/package.json", folder.Read("frontend/package.json").Replace("\"@spar/app\": \"file:../obj/spar/client\",", "", StringComparison.Ordinal));
        folder.Write("frontend/vite.config.ts", "import react from \"@vitejs/plugin-react\";\nimport { defineConfig } from \"vite\";\n\nexport default defineConfig({ plugins: [react()] });\n");

        var findings = ProjectChecks.Check(Find(folder)).ToDictionary(f => f.Id, f => f.Severity);

        Assert.Equal(Severity.Error, findings[FindingIds.Lockfile]);
        Assert.Equal(Severity.Error, findings[FindingIds.SparDependency]);
        Assert.Equal(Severity.Error, findings[FindingIds.VitePlugin]);
    }

    [Fact]
    public void A_dependency_on_another_folder_is_an_error_naming_the_right_one()
    {
        using var folder = TemplateApp.Built();
        folder.Write("frontend/package.json", folder.Read("frontend/package.json").Replace("file:../obj/spar/client", "file:../../obj/spar/client", StringComparison.Ordinal));

        var finding = Assert.Single(ProjectChecks.Check(Find(folder)), f => f.Id == FindingIds.SparDependency);

        Assert.Equal(Severity.Error, finding.Severity);
        Assert.Contains("file:../obj/spar/client", finding.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void SparFrontendPath_moves_the_frontend_and_the_dependency_it_must_name()
    {
        using var folder = TemplateApp.Built();
        Directory.CreateDirectory(Path.Combine(folder.Path, "web"));
        Directory.Move(Path.Combine(folder.Path, "frontend"), Path.Combine(folder.Path, "web", "page"));
        folder.Write("MyApp.csproj", folder.Read("MyApp.csproj").Replace("<SparPlatforms>", "<SparFrontendPath>web/page</SparFrontendPath>\n    <SparPlatforms>", StringComparison.Ordinal));

        var findings = ProjectChecks.Check(Find(folder)).ToDictionary(f => f.Id);

        Assert.Equal("The frontend is web/page/.", findings[FindingIds.FrontendFolder].Message);
        Assert.Contains("file:../../obj/spar/client", findings[FindingIds.SparDependency].Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void No_frontend_is_an_error_that_names_spar_init_frontend()
    {
        using var folder = TemplateApp.Built();
        Directory.Delete(Path.Combine(folder.Path, "frontend"), recursive: true);

        var finding = Assert.Single(ProjectChecks.Check(Find(folder)), f => f.Id == FindingIds.FrontendFolder);

        Assert.Equal(Severity.Error, finding.Severity);
        Assert.Contains("dotnet spar init frontend", finding.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void The_contract_snapshot_is_missing_or_compared_with_the_last_builds_as_the_build_compares_it()
    {
        using var folder = TemplateApp.Built();
        folder.Write("MyApp.contracts.snapshot", "fingerprint 0123456789abcdef\r\n");

        var crlf = Assert.Single(ProjectChecks.Check(Find(folder)), f => f.Id == FindingIds.ContractSnapshot);
        folder.Write("MyApp.contracts.snapshot", "fingerprint fedcba9876543210\n");
        var stale = Assert.Single(ProjectChecks.Check(Find(folder)), f => f.Id == FindingIds.ContractSnapshot);
        File.Delete(Path.Combine(folder.Path, "MyApp.contracts.snapshot"));
        var missing = Assert.Single(ProjectChecks.Check(Find(folder)), f => f.Id == FindingIds.ContractSnapshot);

        Assert.Equal(Severity.Info, crlf.Severity);
        Assert.Equal(Severity.Warning, stale.Severity);
        Assert.Contains("SPAR100", stale.Message, StringComparison.Ordinal);
        Assert.Equal(Severity.Warning, missing.Severity);
    }

    [Fact]
    public void Launch_profiles_need_Desktop_first_and_Loopback_with_SPAR_HOST()
    {
        using var folder = TemplateApp.Built();
        folder.Write("Properties/launchSettings.json", """{ "profiles": { "Loopback": { "commandName": "Project" }, "Desktop": { "commandName": "Project" } } }""");
        var notFirst = ProjectChecks.LaunchProfiles(Find(folder));
        folder.Write("Properties/launchSettings.json", """{ "profiles": { "Desktop": { "commandName": "Project" }, "Loopback": { "commandName": "Project" } } }""");
        var noHost = ProjectChecks.LaunchProfiles(Find(folder));

        Assert.Equal(Severity.Warning, notFirst.Severity);
        Assert.Contains("The first launch profile is Loopback", notFirst.Message, StringComparison.Ordinal);
        Assert.Equal(Severity.Warning, noHost.Severity);
    }

    [Fact]
    public void A_tsconfig_that_only_references_others_is_a_warning_that_the_type_check_sees_no_file()
    {
        using var folder = TemplateApp.Built();
        folder.Write("frontend/tsconfig.json", """
            {
              "files": [],
              "references": [{ "path": "./tsconfig.app.json" }, { "path": "./tsconfig.node.json" }]
            }
            """);

        var finding = Assert.Single(ProjectChecks.Check(Find(folder)), f => f.Id == FindingIds.TypeCheck);

        Assert.Equal(Severity.Warning, finding.Severity);
    }

    [Fact]
    public void A_folder_finds_its_one_app_and_a_solution_folder_the_one_below_it()
    {
        using var folder = TemplateApp.Built();
        folder.Write("../MyApp.Tests/MyApp.Tests.csproj", "<Project Sdk=\"Spar.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><IsTestProject>true</IsTestProject></PropertyGroup></Project>");

        var here = AppProject.Find(folder.Path, out _);
        var above = AppProject.Find(Path.GetDirectoryName(folder.Path)!, out _);
        var none = AppProject.Find(Path.Combine(Path.GetDirectoryName(folder.Path)!, "MyApp.Tests"), out var problem);

        Assert.Equal("MyApp", here?.Name);
        Assert.Equal(here?.File, above?.File);
        Assert.Null(none);
        Assert.Contains("No Spar app project", problem, StringComparison.Ordinal);
    }

    private static AppProject Find(TestFolder folder) =>
        AppProject.Find(folder.Path, out var problem) ?? throw new InvalidOperationException(problem);
}
