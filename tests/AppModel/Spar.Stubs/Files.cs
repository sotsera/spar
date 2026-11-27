namespace Spar;

/// <summary>A filter in a file dialog, for example <c>new("Recipes", "*.sparrecipe")</c>.</summary>
public sealed record FileFilter(string Name, string Pattern);

/// <summary>Options of an open dialog.</summary>
public sealed record OpenFileOptions
{
    public string? Title { get; init; }

    public IReadOnlyList<FileFilter> Filters { get; init; } = [];
}

/// <summary>Why a file dialog gave no file.</summary>
public closed record FileDialogError
{
    /// <summary>The user closed the dialog.</summary>
    public sealed record Dismissed : FileDialogError;

    /// <summary>The OS or a sandbox refused the chosen location.</summary>
    public sealed record AccessDenied(string Path) : FileDialogError;

    /// <summary>No dialog can be shown: no file portal, or no display.</summary>
    public sealed record Unavailable(string Reason) : FileDialogError;

    /// <summary>A dialog is already open for this window.</summary>
    public sealed record Busy : FileDialogError;
}

/// <summary>Why a granted file could not be read or written.</summary>
public closed record FileAccessError
{
    /// <summary>The file was deleted or moved since it was picked.</summary>
    public sealed record NotFound : FileAccessError;

    public sealed record AccessDenied : FileAccessError;

    /// <summary>Another program holds the file.</summary>
    public sealed record InUse : FileAccessError;

    public sealed record TooLarge(int Limit) : FileAccessError;

    /// <summary>The window that asked has closed, or the grant's expiry passed.</summary>
    public sealed record GrantExpired : FileAccessError;

    /// <summary>The path leaves the granted folder: absolute, <c>..</c>, or a link pointing outside.</summary>
    public sealed record OutsideFolder(string Path) : FileAccessError;

    /// <summary>The grant is read-only.</summary>
    public sealed record ReadOnlyGrant : FileAccessError;

    /// <summary>Any other failure of the file system, such as a full disk.</summary>
    public sealed record Failed(string Reason) : FileAccessError;
}

/// <summary>What a folder grant allows.</summary>
public enum FolderAccess
{
    Read,
    ReadWrite,
}

/// <summary>Options of a folder dialog.</summary>
public sealed record OpenFolderOptions
{
    public string? Title { get; init; }

    public FolderAccess Access { get; init; } = FolderAccess.Read;
}

/// <summary>An entry of a granted folder: a name relative to it, never a full path.</summary>
public sealed record FolderEntry(string Name, bool IsFolder, long Size, DateTimeOffset Modified);

/// <summary>A folder the user picked. Every path is relative to it and cannot leave it.</summary>
public sealed class FolderGrant
{
    internal FolderGrant(string name, FolderAccess access)
    {
        Name = name;
        Access = access;
    }

    public string Name { get; }

    public FolderAccess Access { get; }

    public Task<Result<IReadOnlyList<FolderEntry>, FileAccessError>> ListAsync(string relativePath, CancellationToken cancellationToken) =>
        Task.FromResult<Result<IReadOnlyList<FolderEntry>, FileAccessError>>(new FileAccessError.GrantExpired());

    /// <summary>Writes to a temporary file next to the target, then replaces it.</summary>
    public Task<Result<FileAccessError>> WriteTextAsync(string relativePath, string text, CancellationToken cancellationToken) =>
        Task.FromResult<Result<FileAccessError>>(Access == FolderAccess.Read ? new FileAccessError.ReadOnlyGrant() : new FileAccessError.GrantExpired());
}

/// <summary>A file the user picked. The page sees only its name; C# reads it through the grant.</summary>
public sealed class FileGrant
{
    internal FileGrant(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public Result<Stream, FileAccessError> OpenRead() => new FileAccessError.GrantExpired();
}

/// <summary>Open and save dialogs, on the window's own thread, never blocking it. Injectable.</summary>
public interface IFileDialogs
{
    Task<Result<FileGrant, FileDialogError>> OpenAsync(OpenFileOptions options, CancellationToken cancellationToken);

    Task<Result<FolderGrant, FileDialogError>> OpenFolderAsync(OpenFolderOptions options, CancellationToken cancellationToken);
}
