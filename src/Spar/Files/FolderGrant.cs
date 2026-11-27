using Spar.Files;

namespace Spar;

/// <summary>An entry of a granted folder: its name relative to the folder, never a full path.</summary>
/// <param name="Name">The entry's path relative to the granted folder, with <c>/</c> between its parts.</param>
public sealed record FolderEntry(string Name, bool IsFolder, long Size, DateTimeOffset Modified);

/// <summary>
/// A folder the user chose. Every path is relative to it and cannot leave it: an absolute path, a <c>..</c> segment, or
/// a link whose target is outside the folder is refused with <see cref="FileAccessError.OutsideFolder"/>, checked at
/// each use. A <see cref="FolderAccess.Read"/> grant lists and reads; a <see cref="FolderAccess.ReadWrite"/> one also
/// writes (safely), creates folders and deletes. Its lifetime is a <see cref="FileGrant"/>'s.
/// </summary>
public sealed class FolderGrant
{
    private readonly GrantTable _table;

    internal FolderGrant(GrantTable table, string id, string path, FolderAccess access, DateTimeOffset? expires)
    {
        _table = table;
        Id = id;
        Path = GrantPaths.RealPath(path);
        var name = System.IO.Path.GetFileName(Path);
        Name = name.Length > 0 ? name : Path;
        Access = access;
        Expires = expires;
    }

    /// <summary>The folder's name: the one part of the path the page sees.</summary>
    public string Name { get; }

    /// <summary>The folder's full path, its own links followed, for C#; it never reaches the page.</summary>
    public string Path { get; }

    public FolderAccess Access { get; }

    /// <summary>When the grant ends on its own, or null for when its window closes.</summary>
    public DateTimeOffset? Expires { get; }

    /// <summary>Whether the grant still holds: its window is open and its expiry has not passed.</summary>
    public bool IsValid => _table.Holds(Expires);

    internal string Id { get; }

    /// <summary>The full path of <paramref name="relativePath"/> inside the folder, checked as every other call checks it.</summary>
    public Result<string, FileAccessError> ResolvePath(string relativePath)
    {
        if (!IsValid)
        {
            return new FileAccessError.GrantExpired();
        }

        var resolved = GrantPaths.Resolve(Path, relativePath);

        // A folder the user chose can hold the browser's profile; no grant reaches inside it.
        return resolved.TryGetValue(out var full) && _table.Forbids(full) ? new FileAccessError.AccessDenied() : resolved;
    }

    /// <summary>The entries of the folder, or of one of its folders, sorted by name; not recursive.</summary>
    public Task<Result<IReadOnlyList<FolderEntry>, FileAccessError>> ListAsync(string relativePath, CancellationToken cancellationToken)
    {
        var resolved = ResolvePath(relativePath);
        if (!resolved.TryGetValue(out var full))
        {
            return Task.FromResult<Result<IReadOnlyList<FolderEntry>, FileAccessError>>(resolved.Error);
        }

        return Task.Run(() => List(full), cancellationToken);
    }

    /// <summary>Opens a file of the folder to read; dispose the stream.</summary>
    public Result<Stream, FileAccessError> OpenRead(string relativePath)
    {
        var resolved = ResolvePath(relativePath);
        return resolved.TryGetValue(out var full) ? GrantIO.OpenRead(full) : resolved.Error;
    }

    public Task<Result<string, FileAccessError>> ReadTextAsync(string relativePath, CancellationToken cancellationToken)
    {
        var resolved = ResolvePath(relativePath);
        return resolved.TryGetValue(out var full)
            ? GrantIO.ReadTextAsync(full, Array.MaxLength, cancellationToken)
            : Task.FromResult<Result<string, FileAccessError>>(resolved.Error);
    }

    public Task<Result<byte[], FileAccessError>> ReadBytesAsync(string relativePath, CancellationToken cancellationToken)
    {
        var resolved = ResolvePath(relativePath);
        return resolved.TryGetValue(out var full)
            ? GrantIO.ReadBytesAsync(full, Array.MaxLength, cancellationToken)
            : Task.FromResult<Result<byte[], FileAccessError>>(resolved.Error);
    }

    /// <summary>Writes a file of the folder as UTF-8, safely; its folder must exist.</summary>
    public Task<Result<FileAccessError>> WriteTextAsync(string relativePath, string text, CancellationToken cancellationToken) =>
        Writable(relativePath, out var full) ?? GrantIO.WriteTextAsync(full, text, cancellationToken);

    public Task<Result<FileAccessError>> WriteBytesAsync(string relativePath, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken) =>
        Writable(relativePath, out var full) ?? GrantIO.WriteBytesAsync(full, bytes, cancellationToken);

    /// <summary>Writes any amount to a file of the folder, safely, as <see cref="FileGrant.WriteAsync"/> does.</summary>
    public Task<Result<FileAccessError>> WriteAsync(string relativePath, Func<Stream, CancellationToken, Task> write, CancellationToken cancellationToken) =>
        Writable(relativePath, out var full) ?? GrantIO.WriteAsync(full, write, cancellationToken);

    /// <summary>Creates a folder, and the ones above it inside the grant; one that exists already is no error.</summary>
    public Task<Result<FileAccessError>> CreateFolderAsync(string relativePath, CancellationToken cancellationToken)
    {
        if (Writable(relativePath, out var full) is { } refused)
        {
            return refused;
        }

        return Run(() => Directory.CreateDirectory(full), cancellationToken);
    }

    /// <summary>Deletes a file, or a folder with everything in it; the folder itself cannot be deleted through its grant.</summary>
    public Task<Result<FileAccessError>> DeleteAsync(string relativePath, CancellationToken cancellationToken)
    {
        if (Writable(relativePath, out var full) is { } refused)
        {
            return refused;
        }

        if (string.Equals(full, Path, StringComparison.Ordinal))
        {
            return Task.FromResult<Result<FileAccessError>>(new FileAccessError.OutsideFolder(relativePath));
        }

        return Run(() => Delete(full), cancellationToken);
    }

    public override string ToString() => $"FolderGrant {Name}";

    private Result<IReadOnlyList<FolderEntry>, FileAccessError> List(string full)
    {
        try
        {
            var folder = new DirectoryInfo(full);
            if (!folder.Exists)
            {
                return new FileAccessError.NotFound();
            }

            var entries = new List<FolderEntry>();
            foreach (var entry in folder.EnumerateFileSystemInfos())
            {
                var relative = System.IO.Path.GetRelativePath(Path, entry.FullName).Replace('\\', '/');
                var isFolder = entry is DirectoryInfo;
                var size = entry is FileInfo file ? file.Length : 0;
                entries.Add(new FolderEntry(relative, isFolder, size, entry.LastWriteTimeUtc));
            }

            entries.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return entries;
        }
        catch (Exception exception) when (GrantIO.ToError(exception) is { } error)
        {
            return error;
        }
    }

    private static void Delete(string full)
    {
        if (Directory.Exists(full) && new DirectoryInfo(full).LinkTarget is null)
        {
            Directory.Delete(full, recursive: true);
            return;
        }

        if (!File.Exists(full) && !Directory.Exists(full))
        {
            throw new FileNotFoundException("Nothing to delete.", full);
        }

        // A link is deleted itself, never what it points to.
        if (Directory.Exists(full))
        {
            Directory.Delete(full);
            return;
        }

        File.Delete(full);
    }

    private static Task<Result<FileAccessError>> Run(Action work, CancellationToken cancellationToken) => Task.Run(() =>
    {
        try
        {
            work();
            return (Result<FileAccessError>)Result.Success;
        }
        catch (Exception exception) when (GrantIO.ToError(exception) is { } error)
        {
            return error;
        }
    }, cancellationToken);

    private Task<Result<FileAccessError>>? Writable(string relativePath, out string full)
    {
        full = "";
        var resolved = ResolvePath(relativePath);
        if (!resolved.TryGetValue(out var path))
        {
            return Task.FromResult<Result<FileAccessError>>(resolved.Error);
        }

        if (Access != FolderAccess.ReadWrite)
        {
            return Task.FromResult<Result<FileAccessError>>(new FileAccessError.ReadOnlyGrant());
        }

        full = path;
        return null;
    }
}
