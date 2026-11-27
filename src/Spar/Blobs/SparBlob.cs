namespace Spar;

/// <summary>
/// Binary data a command gives the page without putting it in the message (decision "M3b's decide-first questions",
/// item 2). The page gets an object with an address on the app's own origin, so an <c>&lt;img&gt;</c>, a download or
/// <c>fetch</c> reads the bytes at the origin's speed, and the default page policy allows it.
/// <list type="bullet">
/// <item>Returned by a command, or inside its result's records, it is a <b>handle</b>: it stays valid until the page
/// releases it or its document goes. A <c>byte[]</c> handle counts against <see cref="BridgeOptions.MaxBlobBytes"/>;
/// a file handle keeps only its path; a stream is copied to a temporary file first, so neither holds memory.</item>
/// <item>Returned by a <see cref="SparResourceAttribute"/> command, it is read once for the load that ran the command
/// and dropped: nothing is kept between loads.</item>
/// </list>
/// Images, audio, video, plain text, CSV, JSON and PDF are served with their type; every other type is served as a
/// download (<c>Content-Disposition: attachment</c>), so a handle can never become a page of the app's origin.
/// Below about 64 KB a <c>byte[]</c> in the result is as fast; above it, a handle or a resource is several times faster.
/// </summary>
public sealed class SparBlob
{
    private readonly byte[]? _bytes;
    private readonly string? _path;
    private readonly bool _ownsFile;
    private int _handles;

    private SparBlob(byte[]? bytes, string? path, bool ownsFile, long length, string contentType, string? name)
    {
        _bytes = bytes;
        _path = path;
        _ownsFile = ownsFile;
        Length = length;
        ContentType = contentType;
        Name = name;
    }

    /// <summary>The media type the page and the browser see, as <c>image/png</c>.</summary>
    public string ContentType { get; }

    /// <summary>The name a download takes, and the page's <c>name</c>; null for none.</summary>
    public string? Name { get; }

    /// <summary>The size in bytes.</summary>
    public long Length { get; }

    /// <summary>Whether its bytes are held in memory, and so count against <see cref="BridgeOptions.MaxBlobBytes"/>.</summary>
    internal bool InMemory => _bytes is not null;

    /// <summary>Data already in memory. The array is not copied: do not change it while the page may read it.</summary>
    public static SparBlob FromBytes(byte[] bytes, string contentType, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return new SparBlob(bytes, null, ownsFile: false, bytes.LongLength, CheckType(contentType), name);
    }

    /// <summary>
    /// A file, read each time the page loads the handle; only the path is kept, so the file must stay until the handle
    /// is released. Its size is read now.
    /// </summary>
    public static SparBlob FromFile(string path, string contentType, string? name = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var full = Path.GetFullPath(path);
        var length = new FileInfo(full).Length;
        return new SparBlob(null, full, ownsFile: false, length, CheckType(contentType), name ?? Path.GetFileName(full));
    }

    /// <summary>
    /// Data in a stream, copied to a temporary file in the app's cache folder (<paramref name="folder"/>, from
    /// <see cref="SparBlobs"/>), which is deleted when the last handle to it goes. The stream is read to its end and
    /// not disposed.
    /// </summary>
    internal static async Task<SparBlob> FromStreamAsync(
        Stream stream, string folder, string contentType, string? name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var type = CheckType(contentType);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".blob");
        try
        {
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 81920, useAsync: true);
            await stream.CopyToAsync(file, cancellationToken);
            return new SparBlob(null, path, ownsFile: true, file.Length, type, name);
        }
        catch
        {
            File.Delete(path);
            throw;
        }
    }

    /// <summary>Opens the bytes for reading, from the start; the caller disposes the stream.</summary>
    public Stream OpenRead() => _bytes is not null
        ? new MemoryStream(_bytes, writable: false)
        : new FileStream(_path!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, useAsync: true);

    /// <summary>The bytes in full, for a host that answers with a buffer: the array itself, or the file read off the caller's thread.</summary>
    internal async Task<ReadOnlyMemory<byte>> ReadAllAsync(CancellationToken cancellationToken) =>
        _bytes ?? await File.ReadAllBytesAsync(_path!, cancellationToken);

    /// <summary>A resource's blob, served once: a stream's temporary file goes, unless a handle still refers to it.</summary>
    internal void Served()
    {
        if (Volatile.Read(ref _handles) == 0)
        {
            DeleteOwnFile();
        }
    }

    /// <summary>Counts one more handle that refers to this data.</summary>
    internal void AddHandle() => Interlocked.Increment(ref _handles);

    /// <summary>Counts one handle gone; the temporary file of a stream goes with the last one.</summary>
    internal void RemoveHandle()
    {
        if (Interlocked.Decrement(ref _handles) == 0)
        {
            DeleteOwnFile();
        }
    }

    /// <summary>Deletes the temporary file of a stream, when this blob made one; a file still open is left to the cache's cleanup.</summary>
    internal void DeleteOwnFile()
    {
        if (!_ownsFile)
        {
            return;
        }

        try
        {
            File.Delete(_path!);
        }
        catch (IOException)
        {
            // Still being read: the cache folder's cleanup at exit or the next start removes it.
        }
        catch (UnauthorizedAccessException)
        {
            // As above.
        }
    }

    private static string CheckType(string contentType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        if (!BlobContentTypes.IsWellFormed(contentType))
        {
            throw new ArgumentException($"'{contentType}' is not a media type such as image/png.", nameof(contentType));
        }

        return contentType;
    }
}
