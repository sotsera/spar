namespace Spar;

/// <summary>Why a file dialog, or a drop, gave no file.</summary>
public closed record FileDialogError
{
    /// <summary>The user closed the dialog without choosing.</summary>
    public sealed record Dismissed : FileDialogError;

    /// <summary>The OS or a sandbox refused the chosen location.</summary>
    public sealed record AccessDenied(string Path) : FileDialogError;

    /// <summary>
    /// No dialog can be shown: no file-chooser portal on Linux, no display, or a call that has no window (one made
    /// outside a page's call, such as a background service's).
    /// </summary>
    public sealed record Unavailable(string Reason) : FileDialogError;

    /// <summary>A dialog is already open for this window, or the window holds the most grants it may (1,000).</summary>
    public sealed record Busy : FileDialogError;
}

/// <summary>Why a granted file or folder could not be read or written.</summary>
public closed record FileAccessError
{
    /// <summary>The file was deleted or moved since it was picked, or the path in the folder does not exist.</summary>
    public sealed record NotFound : FileAccessError;

    /// <summary>The OS refused the access.</summary>
    public sealed record AccessDenied : FileAccessError;

    /// <summary>Another program holds the file.</summary>
    public sealed record InUse : FileAccessError;

    /// <summary>The file is larger than this way of reading or writing takes; the limit is in bytes.</summary>
    public sealed record TooLarge(int Limit) : FileAccessError;

    /// <summary>The window that asked has closed, or the grant's expiry passed.</summary>
    public sealed record GrantExpired : FileAccessError;

    /// <summary>The path leaves the granted folder: absolute, <c>..</c>, or a link pointing outside.</summary>
    public sealed record OutsideFolder(string Path) : FileAccessError;

    /// <summary>The grant is read-only.</summary>
    public sealed record ReadOnlyGrant : FileAccessError;

    /// <summary>Any other failure of the file system, such as a full disk; the reason is the OS's message.</summary>
    public sealed record Failed(string Reason) : FileAccessError;
}
