using Spar.Files;

namespace Spar;

/// <summary>
/// A file the user chose, in a dialog or by dropping it on one of the page's drop areas. C# reads its
/// <see cref="Path"/>; the page sees only its name, size and access. It belongs to the window that asked: it outlives
/// a reload of the page and is revoked when the window closes, or when its expiry passes. A command can return one to
/// the page, and the page can pass one back as a <see cref="FileGrant"/> parameter, which Spar resolves to the
/// window's own grant before the command runs.
/// </summary>
public sealed class FileGrant
{
    private readonly GrantTable _table;

    internal FileGrant(GrantTable table, string id, string path, bool canWrite, DateTimeOffset? expires)
    {
        _table = table;
        Id = id;
        Path = path;
        Name = System.IO.Path.GetFileName(path);
        Size = File.Exists(path) ? new FileInfo(path).Length : 0;
        CanWrite = canWrite;
        Expires = expires;
    }

    /// <summary>The file's name, without its folder: the one part of the path the page sees.</summary>
    public string Name { get; }

    /// <summary>The file's full path, for C#; it never reaches the page.</summary>
    public string Path { get; }

    /// <summary>The file's size in bytes when it was chosen; 0 for a new file from a save dialog.</summary>
    public long Size { get; }

    /// <summary>Whether the grant may write: a save dialog's does, an open dialog's and a drop's do not.</summary>
    public bool CanWrite { get; }

    /// <summary>When the grant ends on its own, or null for when its window closes.</summary>
    public DateTimeOffset? Expires { get; }

    /// <summary>Whether the grant still holds: its window is open and its expiry has not passed.</summary>
    public bool IsValid => _table.Holds(Expires);

    internal string Id { get; }

    /// <summary>Opens the file to read; dispose the stream.</summary>
    public Result<Stream, FileAccessError> OpenRead() => IsValid ? GrantIO.OpenRead(Path) : new FileAccessError.GrantExpired();

    /// <summary>Reads the whole file as text (UTF-8, unless a byte order mark says otherwise).</summary>
    public Task<Result<string, FileAccessError>> ReadTextAsync(CancellationToken cancellationToken) =>
        IsValid ? GrantIO.ReadTextAsync(Path, Array.MaxLength, cancellationToken) : Expired<string>();

    /// <summary>Reads the whole file.</summary>
    public Task<Result<byte[], FileAccessError>> ReadBytesAsync(CancellationToken cancellationToken) =>
        IsValid ? GrantIO.ReadBytesAsync(Path, Array.MaxLength, cancellationToken) : Expired<byte[]>();

    /// <summary>Writes the text as UTF-8, safely: a temporary file beside the target, then a replace.</summary>
    public Task<Result<FileAccessError>> WriteTextAsync(string text, CancellationToken cancellationToken) =>
        Writable() ?? GrantIO.WriteTextAsync(Path, text, cancellationToken);

    /// <summary>Writes the bytes, safely.</summary>
    public Task<Result<FileAccessError>> WriteBytesAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken) =>
        Writable() ?? GrantIO.WriteBytesAsync(Path, bytes, cancellationToken);

    /// <summary>
    /// Writes any amount, safely: <paramref name="write"/> fills a temporary file beside the target, which then replaces
    /// it. A write that throws or is cancelled leaves the old file as it was.
    /// </summary>
    public Task<Result<FileAccessError>> WriteAsync(Func<Stream, CancellationToken, Task> write, CancellationToken cancellationToken) =>
        Writable() ?? GrantIO.WriteAsync(Path, write, cancellationToken);

    public override string ToString() => $"FileGrant {Name}";

    private Task<Result<FileAccessError>>? Writable()
    {
        if (!IsValid)
        {
            return Task.FromResult<Result<FileAccessError>>(new FileAccessError.GrantExpired());
        }

        return CanWrite ? null : Task.FromResult<Result<FileAccessError>>(new FileAccessError.ReadOnlyGrant());
    }

    private static Task<Result<T, FileAccessError>> Expired<T>() => Task.FromResult<Result<T, FileAccessError>>(new FileAccessError.GrantExpired());
}
