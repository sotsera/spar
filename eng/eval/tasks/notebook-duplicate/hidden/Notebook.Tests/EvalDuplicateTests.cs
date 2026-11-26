using Notebook.Pages;
using Spar.Testing;

namespace Notebook.Tests;

/// <summary>The evaluation's hidden check of pages.duplicate.</summary>
public sealed class EvalDuplicateTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_page_is_copied_under_its_title_with_copy()
    {
        await using var app = await SparTestApp.StartAsync<Program>(cancellationToken: Token);
        var pages = new PagesClient(app);
        var created = await pages.CreateAsync(new CreatePage("Plan", "# Plan\n\n- one\n"), Token);

        var copy = await pages.DuplicateAsync(new PageRef(created.Value!.Id), Token);

        Assert.Equal("Plan (copy)", copy.Value!.Title);
        Assert.Equal("# Plan\n\n- one\n", copy.Value.Markdown);
        Assert.Equal(2, (await pages.ListAsync(Token)).Count);
    }

    [Fact]
    public async Task A_missing_page_is_not_found()
    {
        await using var app = await SparTestApp.StartAsync<Program>(cancellationToken: Token);

        var copy = await new PagesClient(app).DuplicateAsync(new PageRef("nothing-here"), Token);

        Assert.IsType<PageError.NotFound>(copy.Error);
    }

    [Fact]
    public async Task A_second_copy_is_a_taken_title()
    {
        await using var app = await SparTestApp.StartAsync<Program>(cancellationToken: Token);
        var pages = new PagesClient(app);
        var created = await pages.CreateAsync(new CreatePage("Plan", null), Token);
        await pages.DuplicateAsync(new PageRef(created.Value!.Id), Token);

        var again = await pages.DuplicateAsync(new PageRef(created.Value.Id), Token);

        Assert.IsType<PageError.TitleTaken>(again.Error);
    }
}
