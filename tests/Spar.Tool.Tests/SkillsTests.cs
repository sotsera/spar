using System.Text.Json;
using System.Text.RegularExpressions;
using Spar.Tool.Skills;

namespace Spar.Tool.Tests;

/// <summary>
/// The app-author skills (plan, M2e; decision "M2e's decide-first questions", items 3 and 4): the plugin's skills in
/// Agent Skills form, every code block an excerpt of a file that Spar builds and tests (a sample, the template, or a
/// guide whose blocks the package tests build), the plugin at the SDK's version, and <c>spar skills</c> copying them.
/// </summary>
public sealed partial class SkillsTests
{
    private static readonly string Plugin = Path.Combine(Repository.Root, "plugins", "spar");

    /// <summary>The languages whose blocks must be excerpts; shell and plain text blocks are commands and output.</summary>
    private static readonly HashSet<string> CheckedLanguages = new(StringComparer.Ordinal) { "csharp", "cs", "ts", "tsx", "js", "xml", "json", "yaml" };

    /// <summary>Where an excerpt may come from: code that the solution or the package tests build.</summary>
    private static readonly string[] Sources = ["samples/", "src/Spar.Templates/content/spar-app/", "docs/guide/"];

    public static TheoryData<string> Skills => [.. Directory.GetDirectories(Path.Combine(Plugin, "skills")).Select(Path.GetFileName).OfType<string>()];

    [Fact]
    public void The_plugin_has_the_four_skills_at_the_sdks_version_and_the_marketplace_lists_it()
    {
        var skills = Directory.GetDirectories(Path.Combine(Plugin, "skills")).Select(Path.GetFileName).Order(StringComparer.Ordinal);
        using var plugin = JsonDocument.Parse(File.ReadAllText(Path.Combine(Plugin, ".claude-plugin", "plugin.json")));
        using var marketplace = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repository.Root, ".claude-plugin", "marketplace.json")));
        var prefix = VersionPrefix().Match(File.ReadAllText(Path.Combine(Repository.Root, "Directory.Build.props"))).Groups[1].Value;

        Assert.Equal(["spar-app", "spar-dev-loop", "spar-shipping", "spar-testing"], skills);
        Assert.Equal(prefix, plugin.RootElement.GetProperty("version").GetString());
        var listed = Assert.Single(marketplace.RootElement.GetProperty("plugins").EnumerateArray());
        Assert.Equal("spar", listed.GetProperty("name").GetString());
        Assert.Equal("./plugins/spar", listed.GetProperty("source").GetString());
    }

    [Theory]
    [MemberData(nameof(Skills))]
    public void A_skill_has_its_name_a_description_and_fewer_than_500_lines(string skill)
    {
        var lines = File.ReadAllLines(Path.Combine(Plugin, "skills", skill, "SKILL.md"));
        var end = Array.IndexOf(lines, "---", 1);

        Assert.Equal("---", lines[0]);
        Assert.True(end > 1, "The front matter has no closing ---.");
        var front = lines[1..end];
        Assert.Contains($"name: {skill}", front);
        var description = Assert.Single(front, line => line.StartsWith("description: ", StringComparison.Ordinal));
        Assert.InRange(description.Length - "description: ".Length, 40, 1024);
        Assert.True(lines.Length < 500, $"{skill} has {lines.Length} lines.");
    }

    [Theory]
    [MemberData(nameof(Skills))]
    public void Every_code_block_of_a_skill_is_an_excerpt_of_a_built_file(string skill)
    {
        var problems = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Plugin, "skills", skill), "*.md", SearchOption.AllDirectories))
        {
            problems.AddRange(Excerpts.Check(File.ReadAllLines(file), Repository.Root, Sources, CheckedLanguages)
                .Select(problem => $"{Path.GetRelativePath(Plugin, file)}: {problem}"));
        }

        Assert.True(problems.Count == 0, string.Join('\n', problems));
    }

    [Fact]
    public void An_excerpt_must_match_its_source_line_by_line()
    {
        using var folder = Repository.NewFolder("excerpt");
        folder.Write("samples/A.cs", "class A\n{\n    void One() { }\n\n    void Two() { }\n}\n");
        string[] good = ["<!-- source: samples/A.cs -->", "```csharp", "class A", "{", "// …", "void Two() { }", "}", "```"];
        string[] changed = ["<!-- source: samples/A.cs -->", "```csharp", "void One() { }", "void Three() { }", "```"];
        string[] unmarked = ["```csharp", "class A", "```"];
        string[] elsewhere = ["<!-- source: docs/design/x.md -->", "```csharp", "class A", "```"];

        Assert.Empty(Excerpts.Check(good, folder.Path, Sources, CheckedLanguages));
        Assert.Contains("not consecutive lines of samples/A.cs", Assert.Single(Excerpts.Check(changed, folder.Path, Sources, CheckedLanguages)), StringComparison.Ordinal);
        Assert.Contains("no <!-- source: … --> marker", Assert.Single(Excerpts.Check(unmarked, folder.Path, Sources, CheckedLanguages)), StringComparison.Ordinal);
        Assert.Contains("not a file Spar builds", Assert.Single(Excerpts.Check(elsewhere, folder.Path, Sources, CheckedLanguages)), StringComparison.Ordinal);
    }

    [Fact]
    public void The_tool_ships_the_plugins_skills_beside_it()
    {
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Plugin, "skills"), "*", SearchOption.AllDirectories))
        {
            var shipped = Path.Combine(SkillsCommand.Shipped, Path.GetRelativePath(Path.Combine(Plugin, "skills"), file));
            Assert.True(File.Exists(shipped), $"{shipped} is missing.");
            Assert.Equal(File.ReadAllBytes(file), File.ReadAllBytes(shipped));
        }
    }

    [Fact]
    public void Spar_skills_copies_them_beside_the_solution_writes_only_changes_and_removes_what_it_no_longer_ships()
    {
        using var app = TemplateApp.New();
        File.WriteAllText(Path.Combine(app.Root, "MyApp.slnx"), "<Solution />\n");
        using var shipped = Repository.NewFolder("shipped");
        shipped.Write("spar-app/SKILL.md", "---\nname: spar-app\n---\nOne\n");
        shipped.Write("spar-app/references/more.md", "More\n");
        var target = Path.Combine(app.Root, ".agents", "skills");

        var (first, firstOutput) = CopySkills(app, shipped.Path);
        var (second, secondOutput) = CopySkills(app, shipped.Path);
        File.Delete(Path.Combine(shipped.Path, "spar-app", "references", "more.md"));
        shipped.Write("spar-app/SKILL.md", "---\nname: spar-app\n---\nTwo\n");
        var (third, thirdOutput) = CopySkills(app, shipped.Path);

        Assert.Equal((ExitCodes.Success, ExitCodes.Success, ExitCodes.Success), (first, second, third));
        Assert.Contains("2 file(s) written, 0 removed", firstOutput, StringComparison.Ordinal);
        Assert.Contains("are up to date", secondOutput, StringComparison.Ordinal);
        Assert.Contains("1 file(s) written, 1 removed", thirdOutput, StringComparison.Ordinal);
        Assert.Equal("---\nname: spar-app\n---\nTwo\n", File.ReadAllText(Path.Combine(target, "spar-app", "SKILL.md")));
        Assert.False(File.Exists(Path.Combine(target, "spar-app", "references", "more.md")));
    }

    private static (int Exit, string Output) CopySkills(TestFolder app, string shipped)
    {
        var output = new StringWriter();
        var exit = SkillsCommand.Run([], output, new StringWriter(), app.Path, shipped);
        return (exit, output.ToString());
    }

    [GeneratedRegex("<VersionPrefix>([^<]+)</VersionPrefix>")]
    private static partial Regex VersionPrefix();
}
