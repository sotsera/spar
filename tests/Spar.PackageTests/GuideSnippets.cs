using System.Text;

namespace Spar.PackageTests;

/// <summary>
/// The blocks of the guides the package tests build, read from the pages themselves, so a block that stops working fails
/// a test: each is the fenced <c>xml</c> block right after its marker <c>&lt;!-- project-snippet: name --&gt;</c>, or the
/// <c>csharp</c> block after <c>&lt;!-- code-snippet: name --&gt;</c>, comments the documentation site does not show. The
/// tests add the project blocks to new apps' project files as a guide tells an author to, before <c>&lt;/Project&gt;</c>.
/// The publishing-choices guide (<c>docs/guide/publishing-choices.md</c>) has project blocks; the telemetry guide
/// (<c>docs/guide/telemetry.md</c>) has both.
/// </summary>
internal static class GuideSnippets
{
    public const string FrameworkDependent = "coreclr-framework-dependent";
    public const string SingleFile = "coreclr-single-file";
    public const string AcceptAotWarnings = "accept-aot-warnings";
    public const string Globalization = "globalization";
    public const string SqlClientPackage = "sqlclient-package";
    public const string SqlClientSingleFile = "sqlclient-single-file";
    public const string TelemetryPackage = "telemetry-package";
    public const string TelemetryProgram = "telemetry-program";
    public const string AspireAppHost = "aspire-apphost";

    /// <summary>The guide's path in the repository.</summary>
    public static string Page(string repository) => Path.Combine(repository, "docs", "guide", "publishing-choices.md");

    /// <summary>The telemetry guide's path in the repository.</summary>
    public static string TelemetryPage(string repository) => Path.Combine(repository, "docs", "guide", "telemetry.md");

    /// <summary>Every block of the publishing-choices guide, by name.</summary>
    public static IReadOnlyDictionary<string, string> Read(string repository) => ReadPage(Page(repository));

    /// <summary>Every block on a page, by name.</summary>
    public static IReadOnlyDictionary<string, string> ReadPage(string page)
    {
        var lines = File.ReadAllLines(page);
        var snippets = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < lines.Length; i++)
        {
            if (Marked(lines[i]) is not ({ } name, { } fence))
            {
                continue;
            }

            if (i + 1 >= lines.Length || lines[i + 1] != fence)
            {
                throw new InvalidOperationException($"The marker of '{name}' is not right above a {fence} block.");
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

    /// <summary>A marker's block name and the fence its block opens with, or nulls for any other line.</summary>
    private static (string? Name, string? Fence) Marked(string line)
    {
        foreach (var (marker, fence) in new[] { ("<!-- project-snippet: ", "```xml"), ("<!-- code-snippet: ", "```csharp") })
        {
            if (line.StartsWith(marker, StringComparison.Ordinal) && line.EndsWith(" -->", StringComparison.Ordinal))
            {
                return (line[marker.Length..^" -->".Length], fence);
            }
        }

        return (null, null);
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

    [Fact]
    public void Every_block_of_the_telemetry_guide_is_built_by_a_test()
    {
        string[] tested = [GuideSnippets.TelemetryPackage, GuideSnippets.TelemetryProgram, GuideSnippets.AspireAppHost];

        Assert.Equal(tested.Order(StringComparer.Ordinal), GuideSnippets.ReadPage(GuideSnippets.TelemetryPage(consumer.Repository)).Keys.Order(StringComparer.Ordinal));
    }

    public void Dispose() => consumer.Report(TestContext.Current.TestState?.Result == TestResult.Passed);
}
