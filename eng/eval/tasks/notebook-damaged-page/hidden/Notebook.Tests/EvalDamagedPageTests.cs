using Notebook.Pages;
using Spar.Testing;

namespace Notebook.Tests;

/// <summary>The evaluation's hidden check: a damaged page file does not hide the others.</summary>
public sealed class EvalDamagedPageTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("revision: two\nupdated: 2026-10-01T10:00:00.0000000+00:00")]
    [InlineData("revision: 4\nupdated: last tuesday")]
    public async Task The_list_shows_the_good_pages_beside_a_damaged_one(string fields)
    {
        await using var app = await SparTestApp.StartAsync<Program>(cancellationToken: Token);
        var pages = Directory.CreateDirectory(Path.Combine(app.Folders.Data, "pages")).FullName;
        await File.WriteAllTextAsync(Path.Combine(pages, "good.md"), "---\ntitle: Good\nrevision: 1\nupdated: 2026-11-19T08:30:00.0000000+00:00\n---\n# Good\n", Token);
        await File.WriteAllTextAsync(Path.Combine(pages, "damaged.md"), $"---\ntitle: Damaged\n{fields}\n---\n# Damaged\n", Token);

        var listed = await new PagesClient(app).ListAsync(Token);

        Assert.Equal(["Good"], listed.Select(page => page.Title));
    }
}
