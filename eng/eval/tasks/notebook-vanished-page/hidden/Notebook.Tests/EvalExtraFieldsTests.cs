using Notebook.Pages;
using Spar.Testing;

namespace Notebook.Tests;

/// <summary>The evaluation's hidden check: a page with front matter fields of another editor is listed and opens.</summary>
public sealed class EvalExtraFieldsTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_page_with_more_front_matter_is_listed_and_opens()
    {
        await using var app = await SparTestApp.StartAsync<Program>(cancellationToken: Token);
        var pages = Directory.CreateDirectory(Path.Combine(app.Folders.Data, "pages")).FullName;
        await File.WriteAllTextAsync(
            Path.Combine(pages, "meeting-notes.md"),
            "---\ntitle: Meeting notes\nrevision: 4\nupdated: 2026-11-20T10:00:00.0000000+00:00\ntags: work\naliases: standup\n---\n# Meeting notes\n",
            Token);
        var client = new PagesClient(app);

        var listed = await client.ListAsync(Token);
        var opened = await client.GetAsync(new PageRef("meeting-notes"), Token);

        Assert.Equal(["Meeting notes"], listed.Select(page => page.Title));
        Assert.Equal("Meeting notes", opened.Value.Title);
    }
}
