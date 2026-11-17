using System.Globalization;
using System.Text;
using Spar;

namespace Notebook.Pages;

/// <summary>Where pages live. The app's store keeps them as Markdown files; a test may replace it.</summary>
public interface IPageStore
{
    Task<Page?> FindAsync(string id, CancellationToken cancellationToken);

    Task<IReadOnlyList<PageSummary>> ListAsync(CancellationToken cancellationToken);

    Task WriteAsync(Page page, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken);
}

/// <summary>
/// One Markdown file per page under the app's data folder, with a small front matter for what Markdown cannot hold:
/// <c>title</c>, <c>revision</c> and <c>updated</c>. The files stay readable and editable in any editor.
/// </summary>
public sealed class MarkdownPageStore(SparFolders folders) : IPageStore
{
    private const string Fence = "---";

    private readonly string _folder = Path.Combine(folders.Data, "pages");
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<Page?> FindAsync(string id, CancellationToken cancellationToken)
    {
        var path = PathOf(id);
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        var text = await File.ReadAllTextAsync(path, cancellationToken);
        return Parse(id, text);
    }

    public async Task<IReadOnlyList<PageSummary>> ListAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_folder))
        {
            return [];
        }

        var pages = new List<PageSummary>();
        foreach (var path in Directory.EnumerateFiles(_folder, "*.md"))
        {
            var page = await FindAsync(Path.GetFileNameWithoutExtension(path), cancellationToken);
            if (page is not null)
            {
                pages.Add(new PageSummary(page.Id, page.Title, page.UpdatedAt));
            }
        }

        return [.. pages.OrderByDescending(p => p.UpdatedAt).ThenBy(p => p.Title, StringComparer.CurrentCultureIgnoreCase)];
    }

    public async Task WriteAsync(Page page, CancellationToken cancellationToken)
    {
        var path = PathOf(page.Id) ?? throw new ArgumentException($"'{page.Id}' is not a page id.", nameof(page));
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_folder);

            // Write beside the page, then replace it, so a crash never leaves half a page.
            var temporary = path + ".tmp";
            await File.WriteAllTextAsync(temporary, Format(page), cancellationToken);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        var path = PathOf(id);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (path is null || !File.Exists(path))
            {
                return false;
            }

            File.Delete(path);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The page's file, or null for an id that is not one this store made (no paths from the page).</summary>
    private string? PathOf(string id) => PageIds.IsValid(id) ? Path.Combine(_folder, id + ".md") : null;

    private static string Format(Page page)
    {
        var text = new StringBuilder();
        text.Append(Fence).Append('\n');
        text.Append("title: ").Append(page.Title.Replace("\n", " ", StringComparison.Ordinal)).Append('\n');
        text.Append("revision: ").Append(page.Revision.ToString(CultureInfo.InvariantCulture)).Append('\n');
        text.Append("updated: ").Append(page.UpdatedAt.ToString("O", CultureInfo.InvariantCulture)).Append('\n');
        text.Append(Fence).Append('\n');
        text.Append(page.Markdown);
        return text.ToString();
    }

    private static Page? Parse(string id, string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines.Length < 2 || lines[0] != Fence)
        {
            return null;
        }

        var end = Array.IndexOf(lines, Fence, 1);
        if (end < 0)
        {
            return null;
        }

        var fields = lines[1..end]
            .Select(line => line.Split(": ", 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);
        var markdown = string.Join('\n', lines[(end + 1)..]);

        var complete = fields.TryGetValue("title", out var title)
            & fields.TryGetValue("revision", out var revision)
            & fields.TryGetValue("updated", out var updated);
        if (!complete
            || !long.TryParse(revision, NumberStyles.None, CultureInfo.InvariantCulture, out var revisionNumber)
            || !DateTimeOffset.TryParse(updated, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var updatedAt))
        {
            return null;
        }

        return new Page(id, title!, markdown, updatedAt, revisionNumber);
    }
}

/// <summary>Page ids are the title's slug: lower-case letters and digits joined by single dashes.</summary>
public static class PageIds
{
    public static string FromTitle(string title)
    {
        var slug = new StringBuilder();
        foreach (var c in title.Normalize(NormalizationForm.FormKD))
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                slug.Append(char.ToLowerInvariant(c));
            }
            else if (slug.Length > 0 && slug[^1] != '-' && (char.IsWhiteSpace(c) || c is '-' or '_' or '.'))
            {
                slug.Append('-');
            }
        }

        return slug.ToString().TrimEnd('-');
    }

    public static bool IsValid(string id) =>
        id.Length is > 0 and <= 200 && id.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-') && id[0] != '-' && id[^1] != '-';
}
