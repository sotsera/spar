using Spar;

namespace WireShapes;

/// <summary>A grant and text to add to its file, in one request record.</summary>
public sealed record AppendRequest(FileGrant File, string Text);

/// <summary>A secret the page hands the backend to keep; it never comes back.</summary>
public sealed record SecretRequest(string Name, string Secret);

/// <summary>What a pick gave: the file, and the folder when one was chosen too.</summary>
public sealed record Picked(FileGrant File, FolderGrant? Folder);

/// <summary>
/// Grants, the clipboard and credentials through generated code (decision "M3c's decide-first questions", items 2, 5
/// and 6): a dialog's grant returned to the caller and passed back as a parameter, alone or in a record, read and
/// written in C#; the backend's clipboard; and a secret stored that the page cannot read back.
/// </summary>
[SparContract]
public sealed class GrantsService(IFileDialogs dialogs, IClipboard clipboard, ICredentialStore credentials)
{
    [SparCommand]
    public Task<Result<FileGrant, FileDialogError>> PickAsync(string title, CancellationToken cancellationToken) =>
        dialogs.OpenAsync(new OpenFileOptions { Title = title, Filters = [new FileFilter("Text", "*.txt;*.md")] }, cancellationToken);

    [SparCommand]
    public async Task<Result<Picked, FileDialogError>> PickBothAsync(CancellationToken cancellationToken)
    {
        var file = await dialogs.OpenAsync(new OpenFileOptions { Title = "File" }, cancellationToken);
        if (!file.TryGetValue(out var picked))
        {
            return file.Error;
        }

        var folder = await dialogs.OpenFolderAsync(new OpenFolderOptions { Title = "Folder" }, cancellationToken);
        return new Picked(picked, folder.TryGetValue(out var chosen) ? chosen : null);
    }

    [SparCommand]
    public Task<Result<FileGrant, FileDialogError>> SaveAsync(string name, CancellationToken cancellationToken) =>
        dialogs.SaveAsync(new SaveFileOptions { Title = "Save", SuggestedName = name }, cancellationToken);

    /// <summary>The file's size, read in C# from the grant's path.</summary>
    [SparCommand]
    public Task<long> LengthAsync(FileGrant file) => Task.FromResult(new FileInfo(file.Path).Length);

    [SparCommand]
    public async Task<Result<FileAccessError>> AppendAsync(AppendRequest request, CancellationToken cancellationToken)
    {
        var read = await request.File.ReadTextAsync(cancellationToken);
        if (!read.TryGetValue(out var text))
        {
            return read.Error;
        }

        return await request.File.WriteTextAsync(text + request.Text, cancellationToken);
    }

    [SparCommand]
    public async Task<Result<int, FileAccessError>> CountAsync(FolderGrant folder, CancellationToken cancellationToken)
    {
        var listed = await folder.ListAsync("", cancellationToken);
        return listed.TryGetValue(out var entries) ? entries.Count : listed.Error;
    }

    [SparCommand]
    public Task<Result<ClipboardError>> CopyAsync(string text, CancellationToken cancellationToken) =>
        clipboard.SetTextAsync(text, cancellationToken);

    [SparCommand]
    public Task<Result<string, ClipboardError>> PasteAsync(CancellationToken cancellationToken) =>
        clipboard.GetTextAsync(cancellationToken);

    [SparCommand]
    public Task<Result<CredentialError>> RememberAsync(SecretRequest request, CancellationToken cancellationToken) =>
        credentials.SetAsync(request.Name, request.Secret, cancellationToken);

    [SparCommand]
    public Task<Result<bool, CredentialError>> KnowsAsync(string name, CancellationToken cancellationToken) =>
        credentials.ExistsAsync(name, cancellationToken);
}
