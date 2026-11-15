namespace Notebook.Pages;

/// <summary>A page of the notebook: its Markdown, and the revision a save must start from.</summary>
public sealed record Page(string Id, string Title, string Markdown, DateTimeOffset UpdatedAt, long Revision);

/// <summary>A page in the notebook's list, without its text.</summary>
public sealed record PageSummary(string Id, string Title, DateTimeOffset UpdatedAt);

/// <summary>A new page. Without Markdown it starts with its title as a heading.</summary>
public sealed record CreatePage(string Title, string? Markdown);

public sealed record PageRef(string Id);

/// <summary>New text for a page, written over <paramref name="BaseRevision"/>, the revision the editor started from.</summary>
public sealed record SavePage(string Id, string Markdown, long BaseRevision);

/// <summary>Why a page operation did not happen.</summary>
public closed record PageError
{
    /// <summary>Another page already has this title (titles name the page's file).</summary>
    public sealed record TitleTaken(string Title) : PageError;

    /// <summary>The title cannot name a page: empty, too long, or without a letter or digit.</summary>
    public sealed record InvalidTitle(string Reason) : PageError;

    /// <summary>No page has this id; it may have been deleted in another window.</summary>
    public sealed record NotFound(string Id) : PageError;

    /// <summary>The page changed since the editor loaded it; saving now would overwrite that change.</summary>
    public sealed record ChangedElsewhere(long CurrentRevision) : PageError;
}
