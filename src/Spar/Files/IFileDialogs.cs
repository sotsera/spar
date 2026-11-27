using Spar.Files;
using Spar.Runtime;

namespace Spar;

/// <summary>
/// Open, save and folder dialogs, owned by the window the current call came from and shown without blocking it; each
/// chosen file or folder becomes a grant of that window. Inject it into a contract. A call with no window (a service's
/// own work, outside a page's call) gets <see cref="FileDialogError.Unavailable"/>. Under the test host every dialog is
/// answered by the test (<c>app.FileDialogs</c>).
/// </summary>
public interface IFileDialogs
{
    /// <summary>Lets the user choose one file to read.</summary>
    Task<Result<FileGrant, FileDialogError>> OpenAsync(OpenFileOptions options, CancellationToken cancellationToken);

    /// <summary>Lets the user choose one or more files to read.</summary>
    Task<Result<IReadOnlyList<FileGrant>, FileDialogError>> OpenManyAsync(OpenFileOptions options, CancellationToken cancellationToken);

    /// <summary>Lets the user choose where to save; the grant can write that file, which need not exist yet.</summary>
    Task<Result<FileGrant, FileDialogError>> SaveAsync(SaveFileOptions options, CancellationToken cancellationToken);

    /// <summary>Lets the user choose a folder.</summary>
    Task<Result<FolderGrant, FileDialogError>> OpenFolderAsync(OpenFolderOptions options, CancellationToken cancellationToken);
}

/// <summary>Spar's <see cref="IFileDialogs"/>: the call's window, its one dialog at a time, and its grants.</summary>
internal sealed class FileDialogs(SparCall call) : IFileDialogs
{
    public async Task<Result<FileGrant, FileDialogError>> OpenAsync(OpenFileOptions options, CancellationToken cancellationToken)
    {
        var request = new FileDialogRequest(FileDialogKind.Open, options.Title, options.Filters, null);
        var picked = await ShowAsync(request, cancellationToken);
        if (!picked.TryGetValue(out var chosen))
        {
            return picked.Error;
        }

        return AddFile(chosen.Window, chosen.Paths[0], canWrite: false, options.Expiry);
    }

    public async Task<Result<IReadOnlyList<FileGrant>, FileDialogError>> OpenManyAsync(OpenFileOptions options, CancellationToken cancellationToken)
    {
        var request = new FileDialogRequest(FileDialogKind.OpenMany, options.Title, options.Filters, null);
        var picked = await ShowAsync(request, cancellationToken);
        if (!picked.TryGetValue(out var chosen))
        {
            return picked.Error;
        }

        // All or none: a choice that does not fit in the window's grants makes none of them.
        if (!chosen.Window.Grants.HasRoom(chosen.Paths.Count))
        {
            return new FileDialogError.Busy();
        }

        var grants = new List<FileGrant>(chosen.Paths.Count);
        foreach (var path in chosen.Paths)
        {
            if (chosen.Window.Grants.AddFile(path, canWrite: false, options.Expiry) is not { } grant)
            {
                return new FileDialogError.Busy();
            }

            grants.Add(grant);
        }

        return grants;
    }

    public async Task<Result<FileGrant, FileDialogError>> SaveAsync(SaveFileOptions options, CancellationToken cancellationToken)
    {
        var request = new FileDialogRequest(FileDialogKind.Save, options.Title, options.Filters, options.SuggestedName);
        var picked = await ShowAsync(request, cancellationToken);
        if (!picked.TryGetValue(out var chosen))
        {
            return picked.Error;
        }

        return AddFile(chosen.Window, chosen.Paths[0], canWrite: true, options.Expiry);
    }

    public async Task<Result<FolderGrant, FileDialogError>> OpenFolderAsync(OpenFolderOptions options, CancellationToken cancellationToken)
    {
        var request = new FileDialogRequest(FileDialogKind.Folder, options.Title, [], null);
        var picked = await ShowAsync(request, cancellationToken);
        if (!picked.TryGetValue(out var chosen))
        {
            return picked.Error;
        }

        return chosen.Window.Grants.AddFolder(chosen.Paths[0], options.Access, options.Expiry) is { } grant
            ? grant
            : new FileDialogError.Busy();
    }

    private static Result<FileGrant, FileDialogError> AddFile(HostWindow window, string path, bool canWrite, TimeSpan? expiry) =>
        window.Grants.AddFile(path, canWrite, expiry) is { } grant ? grant : new FileDialogError.Busy();

    private async Task<Result<Chosen, FileDialogError>> ShowAsync(FileDialogRequest request, CancellationToken cancellationToken)
    {
        if (call.Window is not { } window)
        {
            return new FileDialogError.Unavailable("This call has no window: a file dialog can be shown only during a page's call.");
        }

        if (window.Dialogs is not { } dialogs)
        {
            return new FileDialogError.Unavailable("This window's host has no file dialogs.");
        }

        if (!window.Grants.HasRoom(1) || !window.TryBeginDialog())
        {
            return new FileDialogError.Busy();
        }

        try
        {
            var shown = await dialogs.ShowAsync(request, cancellationToken);
            if (!shown.TryGetValue(out var paths))
            {
                return shown.Error;
            }

            return paths.Count == 0 ? new FileDialogError.Dismissed() : new Chosen(window, paths);
        }
        finally
        {
            window.EndDialog();
        }
    }

    private sealed record Chosen(HostWindow Window, IReadOnlyList<string> Paths);
}
