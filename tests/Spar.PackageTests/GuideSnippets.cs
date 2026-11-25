using System.Text;

namespace Spar.PackageTests;

/// <summary>
/// The project-file blocks of the publishing-choices guide (<c>docs/guide/publishing-choices.md</c>), read from the page
/// itself, so a block that stops working fails a test: each is the fenced <c>xml</c> block right after its marker
/// <c>&lt;!-- project-snippet: name --&gt;</c>, a comment the documentation site does not show. The tests add them to new
/// apps' project files as the guide tells an author to, before <c>&lt;/Project&gt;</c>.
/// </summary>
internal static class GuideSnippets
{
    public const string FrameworkDependent = "coreclr-framework-dependent";
    public const string SingleFile = "coreclr-single-file";
    public const string AcceptAotWarnings = "accept-aot-warnings";
    public const string Globalization = "globalization";
    public const string SqlClientPackage = "sqlclient-package";
    public const string SqlClientSingleFile = "sqlclient-single-file";

    /// <summary>The guide's path in the repository.</summary>
    public static string Page(string repository) => Path.Combine(repository, "docs", "guide", "publishing-choices.md");

    /// <summary>Every block on the page, by name.</summary>
    public static IReadOnlyDictionary<string, string> Read(string repository)
    {
        const string Marker = "<!-- project-snippet: ";
        var lines = File.ReadAllLines(Page(repository));
        var snippets = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith(Marker, StringComparison.Ordinal) || !lines[i].EndsWith(" -->", StringComparison.Ordinal))
            {
                continue;
            }

            var name = lines[i][Marker.Length..^" -->".Length];
            if (i + 1 >= lines.Length || lines[i + 1] != "```xml")
            {
                throw new InvalidOperationException($"The marker of '{name}' is not right above an ```xml block.");
            }

            var block = new StringBuilder();
            var end = i + 2;
            for (; end < lines.Length && lines[end] != "```"; end++)
            {
                block.Append(lines[end]).Append('\n');
            }

            if (end == lines.Length)
            {
                throw new InvalidOperationException($"The block of '{name}' has no closing fence.");
            }

            if (!snippets.TryAdd(name, block.ToString()))
            {
                throw new InvalidOperationException($"Two blocks are named '{name}'.");
            }

            i = end;
        }

        return snippets;
    }

    /// <summary>
    /// <paramref name="project"/> (a project file's text) with the named blocks added before its <c>&lt;/Project&gt;</c>,
    /// indented one level and a blank line apart, as an author pastes them.
    /// </summary>
    public static string AddTo(string project, IReadOnlyDictionary<string, string> snippets, params string[] names)
    {
        var end = project.LastIndexOf("</Project>", StringComparison.Ordinal);
        Assert.True(end >= 0, "The project file has no </Project>.");
        var added = new StringBuilder();
        foreach (var name in names)
        {
            Assert.True(snippets.TryGetValue(name, out var block), $"The guide has no block named '{name}'.");
            added.Append('\n');
            foreach (var line in block.TrimEnd('\n').Split('\n'))
            {
                added.Append(line.Length == 0 ? "" : "  " + line).Append('\n');
            }
        }

        return project[..end].TrimEnd() + "\n" + added + "</Project>\n";
    }
}

/// <summary>The guide's blocks are the ones the tests build: a block added to the page without a test fails here.</summary>
public sealed class GuideSnippetTests(Consumer consumer) : IDisposable
{
    [Fact]
    public void Every_project_block_of_the_publishing_guide_is_built_by_a_test()
    {
        string[] tested =
        [
            GuideSnippets.FrameworkDependent, GuideSnippets.SingleFile, GuideSnippets.AcceptAotWarnings,
            GuideSnippets.Globalization, GuideSnippets.SqlClientPackage, GuideSnippets.SqlClientSingleFile,
        ];

        Assert.Equal(tested.Order(StringComparer.Ordinal), GuideSnippets.Read(consumer.Repository).Keys.Order(StringComparer.Ordinal));
    }

    public void Dispose() => consumer.Report(TestContext.Current.TestState?.Result == TestResult.Passed);
}
