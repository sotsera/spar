using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Spar.Runtime;

namespace Spar.Files;

/// <summary>
/// The page's own file commands, under the host's contract <c>spar</c> (decision "M3c's decide-first questions", items
/// 2 and 3, and "The drop design, from the file-drop spike"): the SDK's <c>files.open</c>, <c>openMany</c>,
/// <c>save</c> and <c>openFolder</c>, which go through the app's <see cref="IFileDialogs"/> (the test host's fake under a
/// test); reads and safe writes of a grant within <see cref="PageLimit"/>; a grant's file as a blob handle for any
/// size; a folder's list, new folders and deletes; and the claim of a drop on one of the page's areas. A grant id the
/// window does not hold is refused as <c>rejected</c> / <c>invalid-request</c>; one that expired answers
/// <c>grantExpired</c>.
/// </summary>
internal sealed partial class FileCommands(SparDispatcher dispatcher, BlobOwner blobs, HostWindow window)
{
    public const string OpenFile = "openFile";
    public const string OpenFiles = "openFiles";
    public const string SaveFile = "saveFile";
    public const string OpenFolder = "openFolder";
    public const string ReadFile = "readFile";
    public const string WriteFile = "writeFile";
    public const string FileBlob = "fileBlob";
    public const string ListFolder = "listFolder";
    public const string CreateFolder = "createFolder";
    public const string DeleteEntry = "deleteEntry";
    public const string Dropped = "dropped";

    /// <summary>The most a page reads or writes in one message, in bytes of the file (decision item 3: the bridge's 4 MiB).</summary>
    public const int PageLimit = 4 * 1024 * 1024;

    /// <summary>The most files one drop gives when the page sets no limit.</summary>
    public const int DefaultDropLimit = 100;

    public static bool Handles(string command) => command is OpenFile or OpenFiles or SaveFile or OpenFolder or ReadFile or WriteFile
        or FileBlob or ListFolder or CreateFolder or DeleteEntry or Dropped;

    /// <summary>
    /// Runs one; <paramref name="dropped"/> holds the paths a host read from the drop message itself (WebView2), never
    /// from its JSON.
    /// </summary>
    public async ValueTask<SparOutcome> InvokeAsync(
        long callId, string command, ReadOnlyMemory<byte> payload, IReadOnlyList<string>? dropped, CancellationToken cancellationToken)
    {
        using var request = FileRequest.Read(payload, out var problem);
        if (request is null)
        {
            return Invalid($"{command}: {problem}");
        }

        try
        {
            return command switch
            {
                OpenFile or OpenFiles or SaveFile or OpenFolder => await DialogAsync(callId, command, request, cancellationToken),
                ReadFile => await ReadAsync(request, cancellationToken),
                WriteFile => await WriteAsync(request, cancellationToken),
                FileBlob => Blob(request),
                ListFolder => await ListAsync(request, cancellationToken),
                CreateFolder or DeleteEntry => await ChangeFolderAsync(command, request, cancellationToken),
                _ => await DropAsync(request, dropped, cancellationToken),
            };
        }
        catch (FormatException exception)
        {
            return Invalid($"{command}: {exception.Message}");
        }
    }

    private async Task<SparOutcome> DialogAsync(long callId, string command, FileRequest request, CancellationToken cancellationToken)
    {
        await using var scope = dispatcher.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<SparCall>().Start(callId, Bridge.HostCommands.Contract, command, SparCaller.Page, cancellationToken, window);
        var dialogs = scope.ServiceProvider.GetRequiredService<IFileDialogs>();
        var title = request.String("title");
        var filters = request.Filters();

        switch (command)
        {
            case OpenFile:
                var opened = await dialogs.OpenAsync(new OpenFileOptions { Title = title, Filters = filters }, cancellationToken);
                return opened.TryGetValue(out var file) ? Value(writer => SparGrants.Write(writer, file)) : DialogError(opened.Error);
            case OpenFiles:
                var many = await dialogs.OpenManyAsync(new OpenFileOptions { Title = title, Filters = filters }, cancellationToken);
                return many.TryGetValue(out var files) ? Value(writer => WriteGrants(writer, files)) : DialogError(many.Error);
            case SaveFile:
                var options = new SaveFileOptions { Title = title, Filters = filters, SuggestedName = request.String("suggestedName") };
                var saved = await dialogs.SaveAsync(options, cancellationToken);
                return saved.TryGetValue(out var target) ? Value(writer => SparGrants.Write(writer, target)) : DialogError(saved.Error);
            default:
                var access = request.String("access") switch
                {
                    null or "read" => FolderAccess.Read,
                    "readWrite" => FolderAccess.ReadWrite,
                    var other => throw new FormatException($"'access' is \"read\" or \"readWrite\", not \"{other}\"."),
                };
                var chosen = await dialogs.OpenFolderAsync(new OpenFolderOptions { Title = title, Access = access }, cancellationToken);
                return chosen.TryGetValue(out var folder) ? Value(writer => SparGrants.Write(writer, folder)) : DialogError(chosen.Error);
        }
    }

    private static void WriteGrants(Utf8JsonWriter writer, IReadOnlyList<FileGrant> grants)
    {
        writer.WriteStartArray();
        foreach (var grant in grants)
        {
            SparGrants.Write(writer, grant);
        }

        writer.WriteEndArray();
    }

    private static SparOutcome Value(Action<Utf8JsonWriter> write) => SparOutcome.FromValue(FileRequest.Json(write));

    private static SparOutcome Invalid(string detail) => SparOutcome.FromFailure(SparFailure.InvalidRequest(detail));

    private static SparOutcome DialogError(FileDialogError error) => SparOutcome.FromError(FileRequest.Json(writer =>
    {
        writer.WriteStartObject();
        switch (error)
        {
            case FileDialogError.Dismissed:
                writer.WriteString("code", "dismissed");
                break;
            case FileDialogError.AccessDenied denied:
                writer.WriteString("code", "accessDenied");
                writer.WriteString("path", denied.Path);
                break;
            case FileDialogError.Unavailable unavailable:
                writer.WriteString("code", "unavailable");
                writer.WriteString("reason", unavailable.Reason);
                break;
            case FileDialogError.Busy:
                writer.WriteString("code", "busy");
                break;
        }

        writer.WriteEndObject();
    }));

    private static SparOutcome AccessError(FileAccessError error) => SparOutcome.FromError(FileRequest.Json(writer =>
    {
        writer.WriteStartObject();
        switch (error)
        {
            case FileAccessError.NotFound:
                writer.WriteString("code", "notFound");
                break;
            case FileAccessError.AccessDenied:
                writer.WriteString("code", "accessDenied");
                break;
            case FileAccessError.InUse:
                writer.WriteString("code", "inUse");
                break;
            case FileAccessError.TooLarge tooLarge:
                writer.WriteString("code", "tooLarge");
                writer.WriteNumber("limit", tooLarge.Limit);
                break;
            case FileAccessError.GrantExpired:
                writer.WriteString("code", "grantExpired");
                break;
            case FileAccessError.OutsideFolder outside:
                writer.WriteString("code", "outsideFolder");
                writer.WriteString("path", outside.Path);
                break;
            case FileAccessError.ReadOnlyGrant:
                writer.WriteString("code", "readOnlyGrant");
                break;
            case FileAccessError.Failed failed:
                writer.WriteString("code", "failed");
                writer.WriteString("reason", failed.Reason);
                break;
        }

        writer.WriteEndObject();
    }));
}
