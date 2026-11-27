using Spar;

namespace Notebook.Pages;

/// <summary>Markdown files to add as pages: from the open dialog, or dropped on the list of pages.</summary>
public sealed record ImportFiles(IReadOnlyList<FileGrant> Files);

/// <summary>What became of one imported file: the page it became, or why it did not.</summary>
public sealed record Imported(string FileName, string? PageId, string? Problem);

/// <summary>Why a page was not exported.</summary>
public closed record ExportError
{
    /// <summary>The user closed the save dialog.</summary>
    public sealed record Dismissed : ExportError;

    /// <summary>No save dialog could be shown, or the folder chosen refused the file.</summary>
    public sealed record NotSaved(string Reason) : ExportError;

    /// <summary>No page has this id; it may have been deleted in another window.</summary>
    public sealed record NotFound(string Id) : ExportError;
}

/// <summary>Why a page was not copied.</summary>
public closed record CopyError
{
    /// <summary>Another program holds the clipboard.</summary>
    public sealed record Busy : CopyError;

    /// <summary>No clipboard is reachable, or the page is too long for it.</summary>
    public sealed record NotCopied(string Reason) : CopyError;

    /// <summary>No page has this id; it may have been deleted in another window.</summary>
    public sealed record NotFound(string Id) : CopyError;
}

/// <summary>
/// Pages in and out of the notebook: Markdown files imported as pages (each file the user chose or dropped arrives as
/// a grant the backend reads), a page exported to a file the user names in the save dialog, and a page copied to the
/// clipboard as Markdown.
/// </summary>
[SparContract]
public sealed class TransferService(IPageStore store, PageChanges changes, IFileDialogs dialogs, IClipboard clipboard, TimeProvider time)
{
    /// <summary>The largest Markdown file a page takes: 1 MiB, far more than a page of notes.</summary>
    public const int MaxImportBytes = 1024 * 1024;

    /// <summary>Adds each file as a page titled after the file's name; a title already taken gets a number.</summary>
    [SparCommand]
    public async Task<IReadOnlyList<Imported>> ImportAsync(ImportFiles request, CancellationToken cancellationToken)
    {
        var results = new List<Imported>(request.Files.Count);
        foreach (var file in request.Files)
        {
            results.Add(await ImportAsync(file, cancellationToken));
        }

        return results;
    }

    /// <summary>Writes the page's Markdown to a file the user chooses; the result is the file's name.</summary>
    [SparCommand]
    public async Task<Result<string, ExportError>> ExportAsync(PageRef request, CancellationToken cancellationToken)
    {
        var page = await store.FindAsync(request.Id, cancellationToken);
        if (page is null)
        {
            return new ExportError.NotFound(request.Id);
        }

        var options = new SaveFileOptions
        {
            Title = $"Export {page.Title}",
            SuggestedName = $"{page.Id}.md",
            Filters = [new FileFilter("Markdown", "*.md")],
        };
        var target = await dialogs.SaveAsync(options, cancellationToken);
        if (!target.TryGetValue(out var file))
        {
            return target.Error switch
            {
                FileDialogError.Dismissed => new ExportError.Dismissed(),
                FileDialogError.AccessDenied e => new ExportError.NotSaved($"{e.Path} cannot be written."),
                FileDialogError.Unavailable e => new ExportError.NotSaved(e.Reason),
                FileDialogError.Busy => new ExportError.NotSaved("Another dialog is open."),
            };
        }

        var written = await file.WriteTextAsync(page.Markdown, cancellationToken);
        return written switch
        {
            Success => file.Name,
            FileAccessError error => new ExportError.NotSaved(Describe(error)),
        };
    }

    /// <summary>Puts the page's Markdown on the clipboard, for another program.</summary>
    [SparCommand]
    public async Task<Result<CopyError>> CopyAsync(PageRef request, CancellationToken cancellationToken)
    {
        var page = await store.FindAsync(request.Id, cancellationToken);
        if (page is null)
        {
            return new CopyError.NotFound(request.Id);
        }

        var copied = await clipboard.SetTextAsync(page.Markdown, cancellationToken);
        return copied switch
        {
            Success => Result.Success,
            ClipboardError.Busy => new CopyError.Busy(),
            ClipboardError.TooLarge e => new CopyError.NotCopied($"The page is longer than the clipboard takes ({e.Limit / (1024 * 1024)} MB)."),
            ClipboardError.Unavailable e => new CopyError.NotCopied(e.Reason),
        };
    }

    private async Task<Imported> ImportAsync(FileGrant file, CancellationToken cancellationToken)
    {
        if (file.Size > MaxImportBytes)
        {
            return new Imported(file.Name, null, $"It is larger than {MaxImportBytes / 1024} KB.");
        }

        var read = await file.ReadTextAsync(cancellationToken);
        if (!read.TryGetValue(out var markdown))
        {
            return new Imported(file.Name, null, Describe(read.Error));
        }

        var title = await FreeTitleAsync(Path.GetFileNameWithoutExtension(file.Name).Trim(), cancellationToken);
        if (title is null)
        {
            return new Imported(file.Name, null, "Its name has no letter or digit to title a page with.");
        }

        var page = new Page(PageIds.FromTitle(title), title, markdown, time.GetUtcNow(), Revision: 1);
        await store.WriteAsync(page, cancellationToken);
        changes.Publish(new PageChange(page.Id, PageChangeKind.Created, page.Revision));
        return new Imported(file.Name, page.Id, null);
    }

    /// <summary>The title, or the title with the first number that makes it free; null for one that names no page.</summary>
    private async Task<string?> FreeTitleAsync(string title, CancellationToken cancellationToken)
    {
        if (PageIds.FromTitle(title).Length == 0)
        {
            return null;
        }

        for (var n = 1; ; n++)
        {
            var candidate = n == 1 ? title : $"{title} ({n})";
            if (await store.FindAsync(PageIds.FromTitle(candidate), cancellationToken) is null)
            {
                return candidate;
            }
        }
    }

    private static string Describe(FileAccessError error) => error switch
    {
        FileAccessError.NotFound => "The file is gone.",
        FileAccessError.AccessDenied => "The file cannot be opened.",
        FileAccessError.InUse => "Another program has the file open.",
        FileAccessError.TooLarge e => $"It is larger than {e.Limit / 1024} KB.",
        FileAccessError.GrantExpired => "The notebook's window closed.",
        FileAccessError.OutsideFolder e => $"{e.Path} is outside the folder.",
        FileAccessError.ReadOnlyGrant => "The file can only be read.",
        FileAccessError.Failed e => e.Reason,
    };
}
