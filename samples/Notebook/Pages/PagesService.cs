using Spar;

namespace Notebook.Pages;

/// <summary>The notebook's pages: create, read, list, save and delete, and the changes every window follows.</summary>
[SparContract]
public sealed class PagesService(IPageStore store, PageImageFiles images, PageChanges changes, TimeProvider time)
{
    private const int MaxTitleLength = 120;

    [SparCommand]
    public async Task<Result<Page, PageError>> CreateAsync(CreatePage request, CancellationToken cancellationToken)
    {
        var title = request.Title.Trim();
        var id = PageIds.FromTitle(title);
        if (title.Length == 0 || title.Length > MaxTitleLength || id.Length == 0)
        {
            return new PageError.InvalidTitle(DescribeInvalid(title));
        }

        if (await store.FindAsync(id, cancellationToken) is not null)
        {
            return new PageError.TitleTaken(title);
        }

        var page = new Page(id, title, request.Markdown ?? $"# {title}\n", time.GetUtcNow(), Revision: 1);
        await store.WriteAsync(page, cancellationToken);
        changes.Publish(new PageChange(id, PageChangeKind.Created, page.Revision));
        return page;
    }

    [SparCommand]
    public async Task<Result<Page, PageError>> GetAsync(PageRef request, CancellationToken cancellationToken)
    {
        var page = await store.FindAsync(request.Id, cancellationToken);
        return page is null ? new PageError.NotFound(request.Id) : page;
    }

    [SparCommand]
    public Task<IReadOnlyList<PageSummary>> ListAsync(CancellationToken cancellationToken) => store.ListAsync(cancellationToken);

    [SparCommand]
    public async Task<Result<Page, PageError>> SaveAsync(SavePage request, CancellationToken cancellationToken)
    {
        var current = await store.FindAsync(request.Id, cancellationToken);
        if (current is null)
        {
            return new PageError.NotFound(request.Id);
        }

        if (current.Revision != request.BaseRevision)
        {
            return new PageError.ChangedElsewhere(current.Revision);
        }

        var saved = current with { Markdown = request.Markdown, UpdatedAt = time.GetUtcNow(), Revision = current.Revision + 1 };
        await store.WriteAsync(saved, cancellationToken);
        changes.Publish(new PageChange(saved.Id, PageChangeKind.Saved, saved.Revision));
        return saved;
    }

    [SparCommand]
    public async Task<Result<PageError>> DeleteAsync(PageRef request, CancellationToken cancellationToken)
    {
        var deleted = await store.DeleteAsync(request.Id, cancellationToken);
        if (deleted)
        {
            images.DeleteAll(request.Id);
            changes.Publish(new PageChange(request.Id, PageChangeKind.Deleted, Revision: 0));
        }

        return deleted ? Result.Success : new PageError.NotFound(request.Id);
    }

    /// <summary>
    /// Every page created, saved or deleted from now on, by any window: the list and the open page follow it. Observation
    /// only, so it resumes by itself after a lost connection, with a reset that has the page read the list again.
    /// </summary>
    [SparCommand]
    [SparSubscription]
    public IAsyncEnumerable<PageChange> ChangesAsync(CancellationToken cancellationToken) => changes.WatchAsync(cancellationToken);

    private static string DescribeInvalid(string title) => title.Length switch
    {
        0 => "A page needs a title.",
        > MaxTitleLength => $"A title can have at most {MaxTitleLength} characters.",
        _ => "A title needs at least one letter or digit.",
    };
}
