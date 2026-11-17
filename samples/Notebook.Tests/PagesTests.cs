using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Notebook.Pages;
using Spar;
using Spar.Runtime;
using Spar.Testing;

namespace Notebook.Tests;

/// <summary>Command tests: the Notebook's real composition, dispatch and serializers, with no page.</summary>
public sealed class PagesTests
{
    private static readonly DateTimeOffset Morning = new(2026, 11, 16, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_new_page_is_stored_as_a_Markdown_file()
    {
        await using var app = await StartAsync();
        var pages = new PagesClient(app);

        var created = await pages.CreateAsync(new CreatePage("Weekly review", "# Wins\n\n- Shipped the notebook\n"), Token);

        var page = Assert.IsType<Page>(Value(created));
        Assert.Equal(("weekly-review", "Weekly review", 1L, Morning), (page.Id, page.Title, page.Revision, page.UpdatedAt));
        var file = await File.ReadAllTextAsync(Path.Combine(app.Folders.Data, "pages", "weekly-review.md"), Token);
        Assert.Equal("---\ntitle: Weekly review\nrevision: 1\nupdated: 2026-11-16T08:30:00.0000000+00:00\n---\n# Wins\n\n- Shipped the notebook\n", file);
    }

    [Fact]
    public async Task A_page_without_text_starts_with_its_title()
    {
        await using var app = await StartAsync();
        var pages = new PagesClient(app);

        var created = await pages.CreateAsync(new CreatePage("  Reading list ", null), Token);

        Assert.Equal("# Reading list\n", Value(created).Markdown);
    }

    [Fact]
    public async Task A_taken_title_is_a_declared_error()
    {
        await using var app = await StartAsync();
        var pages = new PagesClient(app);
        await pages.CreateAsync(new CreatePage("Ideas", null), Token);

        var again = await pages.CreateAsync(new CreatePage("ideas", null), Token);

        Assert.Equal(new PageError.TitleTaken("ideas"), Error(again));
    }

    [Theory]
    [InlineData("", "A page needs a title.")]
    [InlineData("!!!", "A title needs at least one letter or digit.")]
    public async Task An_unusable_title_says_why(string title, string reason)
    {
        await using var app = await StartAsync();

        var created = await new PagesClient(app).CreateAsync(new CreatePage(title, null), Token);

        Assert.Equal(new PageError.InvalidTitle(reason), Error(created));
    }

    [Fact]
    public async Task Saving_over_a_change_made_elsewhere_is_refused()
    {
        await using var app = await StartAsync();
        var pages = new PagesClient(app);
        var page = Value(await pages.CreateAsync(new CreatePage("Plans", null), Token));
        var first = Value(await pages.SaveAsync(new SavePage(page.Id, "# Plans\n\nA\n", page.Revision), Token));

        var stale = await pages.SaveAsync(new SavePage(page.Id, "# Plans\n\nB\n", page.Revision), Token);

        Assert.Equal(2, first.Revision);
        Assert.Equal(new PageError.ChangedElsewhere(2), Error(stale));
        Assert.Equal("# Plans\n\nA\n", Value(await pages.GetAsync(new PageRef(page.Id), Token)).Markdown);
    }

    [Fact]
    public async Task Listing_shows_the_newest_page_first_and_delete_removes_it()
    {
        var clock = new SteppingTime(Morning);
        await using var app = await StartAsync(clock);
        var pages = new PagesClient(app);
        await pages.CreateAsync(new CreatePage("Older", null), Token);
        await pages.CreateAsync(new CreatePage("Newer", null), Token);

        var listed = await pages.ListAsync(Token);
        var deleted = await pages.DeleteAsync(new PageRef("newer"), Token);
        var deletedAgain = await pages.DeleteAsync(new PageRef("newer"), Token);

        Assert.Equal(["Newer", "Older"], listed.Select(p => p.Title));
        Assert.True(deleted.IsSuccess);
        Assert.Equal(new PageError.NotFound("newer"), deletedAgain.Error);
        Assert.Equal(["Older"], (await pages.ListAsync(Token)).Select(p => p.Title));
    }

    [Fact]
    public async Task A_malformed_request_is_rejected_before_the_command_runs()
    {
        await using var app = await StartAsync();

        var missingTitle = await app.InvokeAsync("pages", "create", Encoding.UTF8.GetBytes("{\"markdown\":null}"), Token);
        var unknown = await app.InvokeAsync("pages", "rename", ReadOnlyMemory<byte>.Empty, Token);

        Assert.Equal(("rejected", "invalid-request", "notStarted"), Failure(missingTitle));
        Assert.Equal(("rejected", "unknown-command", "notStarted"), Failure(unknown));
        Assert.Empty(await new PagesClient(app).ListAsync(Token));
    }

    [Fact]
    public async Task An_id_from_the_page_never_reaches_outside_the_notebook_folder()
    {
        await using var app = await StartAsync();

        var escaped = await new PagesClient(app).GetAsync(new PageRef("../../secrets"), Token);

        Assert.Equal(new PageError.NotFound("../../secrets"), Error(escaped));
    }

    [Fact]
    public async Task Two_apps_at_once_keep_their_notebooks_apart()
    {
        var apps = await Task.WhenAll(StartAsync(), StartAsync());
        try
        {
            await new PagesClient(apps[0]).CreateAsync(new CreatePage("Only in the first", null), Token);

            Assert.Single(await new PagesClient(apps[0]).ListAsync(Token));
            Assert.Empty(await new PagesClient(apps[1]).ListAsync(Token));
            Assert.NotEqual(apps[0].Folders.DataRoot, apps[1].Folders.DataRoot);
        }
        finally
        {
            foreach (var app in apps)
            {
                await app.DisposeAsync();
            }
        }
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Task<SparTestApp> StartAsync(TimeProvider? time = null) =>
        SparTestApp.StartAsync<Program>(
            options => options.Services(services => services.Replace(ServiceDescriptor.Singleton(time ?? new FixedTime(Morning)))),
            Token);

    private static Page Value(Result<Page, PageError> result) => result switch
    {
        Page page => page,
        PageError error => throw new Xunit.Sdk.XunitException($"Expected a page, got {error}."),
    };

    private static PageError Error<T>(Result<T, PageError> result) =>
        result.TryGetError(out var error) ? error : throw new Xunit.Sdk.XunitException($"Expected an error, got {result}.");

    private static (string Kind, string Code, string Execution) Failure(SparOutcome outcome)
    {
        Assert.Equal(SparOutcomeKind.Failure, outcome.Kind);
        return (outcome.Failure!.Kind, outcome.Failure.Code, outcome.Failure.Execution);
    }
}

internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>A clock that moves one minute forward each time it is read, so creation order is visible.</summary>
internal sealed class SteppingTime(DateTimeOffset start) : TimeProvider
{
    private int _reads;

    public override DateTimeOffset GetUtcNow() => start.AddMinutes(Interlocked.Increment(ref _reads));
}
