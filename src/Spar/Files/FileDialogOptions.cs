namespace Spar;

/// <summary>A filter in a file dialog: a name and its patterns, for example <c>new("Recipes", "*.sparrecipe")</c>.</summary>
/// <param name="Name">What the dialog shows, such as <c>Markdown</c>.</param>
/// <param name="Pattern">One pattern or several separated by <c>;</c>, such as <c>*.md;*.markdown</c>.</param>
public sealed record FileFilter(string Name, string Pattern);

/// <summary>What a folder grant allows.</summary>
public enum FolderAccess
{
    /// <summary>List and read.</summary>
    Read,

    /// <summary>List, read, write, create folders and delete, inside the folder only.</summary>
    ReadWrite,
}

/// <summary>Options of an open dialog.</summary>
public sealed record OpenFileOptions
{
    public string? Title { get; init; }

    public IReadOnlyList<FileFilter> Filters { get; init; } = [];

    /// <summary>How long the grant lasts; null for as long as the window that asked stays open.</summary>
    public TimeSpan? Expiry { get; init; }
}

/// <summary>Options of a save dialog. The grant it gives can write the chosen file, which need not exist yet.</summary>
public sealed record SaveFileOptions
{
    public string? Title { get; init; }

    public IReadOnlyList<FileFilter> Filters { get; init; } = [];

    /// <summary>The name the dialog suggests, such as <c>notes.md</c>.</summary>
    public string? SuggestedName { get; init; }

    /// <summary>How long the grant lasts; null for as long as the window that asked stays open.</summary>
    public TimeSpan? Expiry { get; init; }
}

/// <summary>Options of a folder dialog.</summary>
public sealed record OpenFolderOptions
{
    public string? Title { get; init; }

    public FolderAccess Access { get; init; } = FolderAccess.Read;

    /// <summary>How long the grant lasts; null for as long as the window that asked stays open.</summary>
    public TimeSpan? Expiry { get; init; }
}
