namespace Spar;

/// <summary>Why something could not be opened outside the app.</summary>
public closed record ShellError
{
    /// <summary>Refused by policy: a scheme not allowed, no user action, or a file that can run code.</summary>
    public sealed record NotAllowed(string Reason) : ShellError;

    /// <summary>No program is registered for this file type.</summary>
    public sealed record NoHandler : ShellError;

    public sealed record Failed(string Reason) : ShellError;
}

/// <summary>Opens links, files and folders outside the app; injectable.</summary>
public interface IShell
{
    /// <summary>http(s), mailto and tel by default; never with credentials in the URL.</summary>
    Task<Result<ShellError>> OpenExternalAsync(Uri uri, CancellationToken cancellationToken);

    /// <summary>Opens a granted file with its default program. Only types declared with <see cref="ShellOptions.AllowOpening"/>; dangerous types are refused even then.</summary>
    Task<Result<ShellError>> OpenAsync(FileGrant file, CancellationToken cancellationToken);

    /// <summary>Shows a granted folder in Explorer or the file manager.</summary>
    Task<Result<ShellError>> RevealAsync(FolderGrant folder, CancellationToken cancellationToken);
}

/// <summary>Which file types the app may open with their default program.</summary>
public sealed class ShellOptions
{
    private readonly List<string> _allowed = [];
    private readonly List<string> _dangerous = [];

    internal ShellOptions()
    {
    }

    internal IReadOnlyList<string> Allowed => _allowed;

    /// <summary>File types the app opens. Nothing opens by default.</summary>
    public ShellOptions AllowOpening(params string[] extensions)
    {
        _allowed.AddRange(extensions);
        return this;
    }

    /// <summary>A type the OS reports as dangerous (AssocIsDangerous on Windows; executables, .desktop and scripts on Linux). Documented risk.</summary>
    public ShellOptions AllowDangerous(string extension)
    {
        _dangerous.Add(extension);
        return this;
    }
}
