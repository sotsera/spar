using Spar;

namespace Notebook.Pages;

/// <summary>An image pasted or dropped into a page: its page, its type and its bytes.</summary>
public sealed record AddImage(string PageId, string ContentType, byte[] Bytes);

/// <summary>An image of a page, as the page's Markdown names it: <c>![name](image:&lt;ImageId&gt;)</c>.</summary>
public sealed record ImageRef(string PageId, string ImageId);

/// <summary>Why an image was not added.</summary>
public closed record ImageError
{
    /// <summary>No page has this id; it may have been deleted in another window.</summary>
    public sealed record PageNotFound(string Id) : ImageError;

    /// <summary>Only PNG, JPEG, GIF and WebP images can go in a page.</summary>
    public sealed record NotAnImage(string ContentType) : ImageError;

    /// <summary>The image is larger than a page takes.</summary>
    public sealed record TooLarge(int MaxBytes) : ImageError;
}

/// <summary>
/// A page's images: added as bytes from the page (a paste or a drop), kept beside the page's Markdown file in
/// <c>pages/&lt;page&gt;.images/</c>, and shown by the preview through a resource, so the backend keeps nothing between
/// loads and a page may show any number of them.
/// </summary>
[SparContract]
public sealed class ImagesService(IPageStore store, PageImageFiles files)
{
    /// <summary>The largest image a page takes: its bytes cross the bridge as base64, within its 4 MiB message limit.</summary>
    public const int MaxBytes = 3 * 1024 * 1024;

    [SparCommand]
    public async Task<Result<ImageRef, ImageError>> AddAsync(AddImage request, CancellationToken cancellationToken)
    {
        if (PageImageFiles.ExtensionOf(request.ContentType) is null)
        {
            return new ImageError.NotAnImage(request.ContentType);
        }

        if (request.Bytes.Length > MaxBytes)
        {
            return new ImageError.TooLarge(MaxBytes);
        }

        if (await store.FindAsync(request.PageId, cancellationToken) is null)
        {
            return new ImageError.PageNotFound(request.PageId);
        }

        var id = await files.AddAsync(request.PageId, request.ContentType, request.Bytes, cancellationToken);
        return new ImageRef(request.PageId, id);
    }

    /// <summary>The image itself, for the preview's <c>&lt;img&gt;</c>; none when the page or the image is gone.</summary>
    [SparCommand]
    [SparResource]
    public Task<SparBlob?> ImageAsync(ImageRef request) => Task.FromResult(files.Find(request.PageId, request.ImageId));
}

/// <summary>The image files beside the pages: one folder per page, one file per image, named by a random id.</summary>
public sealed class PageImageFiles(SparFolders folders)
{
    private static readonly (string Type, string Extension)[] Types =
        [("image/png", ".png"), ("image/jpeg", ".jpg"), ("image/gif", ".gif"), ("image/webp", ".webp")];

    private readonly string _folder = Path.Combine(folders.Data, "pages");

    /// <summary>The file extension of an image type a page takes, or null for any other type.</summary>
    public static string? ExtensionOf(string contentType) =>
        Types.FirstOrDefault(t => string.Equals(t.Type, contentType, StringComparison.OrdinalIgnoreCase)).Extension;

    public async Task<string> AddAsync(string pageId, string contentType, byte[] bytes, CancellationToken cancellationToken)
    {
        var folder = FolderOf(pageId) ?? throw new ArgumentException($"'{pageId}' is not a page id.", nameof(pageId));
        Directory.CreateDirectory(folder);
        var id = Guid.NewGuid().ToString("N");
        await File.WriteAllBytesAsync(Path.Combine(folder, id + ExtensionOf(contentType)), bytes, cancellationToken);
        return id;
    }

    /// <summary>The image as a blob that reads its file; null when the page id or the image id names no image.</summary>
    public SparBlob? Find(string pageId, string imageId)
    {
        if (FolderOf(pageId) is not { } folder || imageId.Length != 32 || !imageId.All(char.IsAsciiHexDigitLower))
        {
            return null;
        }

        foreach (var (type, extension) in Types)
        {
            var path = Path.Combine(folder, imageId + extension);
            if (File.Exists(path))
            {
                return SparBlob.FromFile(path, type);
            }
        }

        return null;
    }

    /// <summary>Removes a deleted page's images.</summary>
    public void DeleteAll(string pageId)
    {
        if (FolderOf(pageId) is { } folder && Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private string? FolderOf(string pageId) => PageIds.IsValid(pageId) ? Path.Combine(_folder, pageId + ".images") : null;
}
