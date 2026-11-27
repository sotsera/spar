using Spar;

namespace WireShapes;

/// <summary>A result that carries a blob in a record, as an export with its title.</summary>
public sealed record Export(string Title, SparBlob File);

/// <summary>A resource's key: a record of scalars, which travels in its address.</summary>
public sealed record PhotoKey(int Album, int Number);

/// <summary>
/// Binary data from the backend (decision "M3b's decide-first questions", item 2), for the browser and window tests and
/// the desktop budget: handles made from bytes, from a stream and in a record, and resources by a scalar or a record.
/// </summary>
[SparContract]
public sealed class FilesService(SparBlobs blobs)
{
    /// <summary>A 1×1 PNG, so an <c>&lt;img&gt;</c> can show a resource under the default page policy.</summary>
    private static readonly byte[] Pixel = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    /// <summary><paramref name="size"/> bytes, each its index modulo 251, as a <c>byte[]</c> handle.</summary>
    [SparCommand]
    public Task<SparBlob> MakeAsync(int size) =>
        Task.FromResult(SparBlob.FromBytes(Pattern(size), "application/octet-stream", "made.bin"));

    /// <summary>The same bytes copied from a stream into a temporary file, so the handle holds no memory.</summary>
    [SparCommand]
    public async Task<SparBlob> CopyAsync(int size, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(Pattern(size));
        return await blobs.FromStreamAsync(stream, "application/octet-stream", "copied.bin", cancellationToken);
    }

    /// <summary>A CSV in a record, served as a download with its name.</summary>
    [SparCommand]
    public Task<Export> ExportAsync(string title) =>
        Task.FromResult(new Export(title, SparBlob.FromBytes("a,b\n1,2\n"u8.ToArray(), "text/csv", $"{title}.csv")));

    /// <summary>The pixel for any number but a negative one, which has no photo (a 404).</summary>
    [SparCommand]
    [SparResource]
    public Task<SparBlob?> PhotoAsync(PhotoKey request) =>
        Task.FromResult(request.Number < 0 ? null : SparBlob.FromBytes(Pixel, "image/png"));

    /// <summary>A resource by a scalar: the text <c>n=…</c>, so a test sees each load run the command.</summary>
    [SparCommand]
    [SparResource]
    public Task<SparBlob> LabelAsync(int n) =>
        Task.FromResult(SparBlob.FromBytes(System.Text.Encoding.UTF8.GetBytes($"n={n}"), "text/plain; charset=utf-8"));

    /// <summary><paramref name="size"/> bytes, each its index modulo 251, so a reader can tell every byte arrived in place.</summary>
    public static byte[] Pattern(int size)
    {
        var bytes = new byte[size];
        for (var i = 0; i < size; i++)
        {
            bytes[i] = (byte)(i % 251);
        }

        return bytes;
    }
}
