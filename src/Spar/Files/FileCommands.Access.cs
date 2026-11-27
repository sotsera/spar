using System.Text.Json;
using Spar.Hosting;
using Spar.Runtime;

namespace Spar.Files;

internal sealed partial class FileCommands
{
    private async Task<SparOutcome> ReadAsync(FileRequest request, CancellationToken cancellationToken)
    {
        var asText = request.RequiredString("as") switch
        {
            "text" => true,
            "bytes" => false,
            var other => throw new FormatException($"'as' is \"text\" or \"bytes\", not \"{other}\"."),
        };
        var target = Target(request, write: false);
        if (target.Refusal is { } refusal)
        {
            return refusal;
        }

        if (asText)
        {
            var text = await GrantIO.ReadTextAsync(target.Path, PageLimit, cancellationToken);
            return text.TryGetValue(out var value) ? Value(writer => writer.WriteStringValue(value)) : AccessError(text.Error);
        }

        var bytes = await GrantIO.ReadBytesAsync(target.Path, PageLimit, cancellationToken);
        return bytes.TryGetValue(out var data) ? Value(writer => writer.WriteBase64StringValue(data)) : AccessError(bytes.Error);
    }

    private async Task<SparOutcome> WriteAsync(FileRequest request, CancellationToken cancellationToken)
    {
        var text = request.String("text");
        var bytes = request.Bytes("bytes");
        if ((text is null) == (bytes is null))
        {
            throw new FormatException("A write has 'text' or 'bytes', not both or neither.");
        }

        var data = bytes ?? System.Text.Encoding.UTF8.GetBytes(text!);
        if (data.Length > PageLimit)
        {
            return AccessError(new FileAccessError.TooLarge(PageLimit));
        }

        var target = Target(request, write: true);
        if (target.Refusal is { } refusal)
        {
            return refusal;
        }

        var written = await GrantIO.WriteBytesAsync(target.Path, data, cancellationToken);
        return written.IsSuccess ? Value(writer => writer.WriteNullValue()) : AccessError(written.Error);
    }

    /// <summary>The file as a handle of the document, read on the app's origin for any size (M3b); nothing is read now.</summary>
    private SparOutcome Blob(FileRequest request)
    {
        var target = Target(request, write: false);
        if (target.Refusal is { } refusal)
        {
            return refusal;
        }

        if (!File.Exists(target.Path))
        {
            return AccessError(new FileAccessError.NotFound());
        }

        var type = request.String("type");
        if (type is null && !SparContentTypes.TryGet(target.Path, out type))
        {
            type = "application/octet-stream";
        }

        if (!BlobContentTypes.IsWellFormed(type))
        {
            throw new FormatException($"'type' is not a media type: {type}.");
        }

        var blob = SparBlob.FromFile(target.Path, type, Path.GetFileName(target.Path));
        string id;
        try
        {
            id = blobs.Add(blob);
        }
        catch (BlobLimitException exception)
        {
            return SparOutcome.FromFailure(SparFailure.ResponseSerialization(exception));
        }

        return Value(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("id"u8, id);
            writer.WriteString("contentType"u8, blob.ContentType);
            writer.WriteNumber("size"u8, blob.Length);
            writer.WriteString("name"u8, blob.Name);
            writer.WriteEndObject();
        });
    }

    private async Task<SparOutcome> ListAsync(FileRequest request, CancellationToken cancellationToken)
    {
        if (Grant(request) is not FolderGrant folder)
        {
            return Invalid("listFolder takes a folder grant of this window.");
        }

        var listed = await folder.ListAsync(request.String("path") ?? "", cancellationToken);
        if (!listed.TryGetValue(out var entries))
        {
            return AccessError(listed.Error);
        }

        return Value(writer =>
        {
            writer.WriteStartArray();
            foreach (var entry in entries)
            {
                writer.WriteStartObject();
                writer.WriteString("name"u8, entry.Name);
                writer.WriteBoolean("isFolder"u8, entry.IsFolder);
                writer.WriteNumber("size"u8, entry.Size);
                writer.WriteString("modified"u8, entry.Modified);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        });
    }

    private async Task<SparOutcome> ChangeFolderAsync(string command, FileRequest request, CancellationToken cancellationToken)
    {
        if (Grant(request) is not FolderGrant folder)
        {
            return Invalid($"{command} takes a folder grant of this window.");
        }

        var path = request.RequiredString("path");
        var changed = command == CreateFolder
            ? await folder.CreateFolderAsync(path, cancellationToken)
            : await folder.DeleteAsync(path, cancellationToken);
        return changed.IsSuccess ? Value(writer => writer.WriteNullValue()) : AccessError(changed.Error);
    }

    /// <summary>
    /// A drop on one of the page's areas: the files the host read from the drop itself (WebView2's message, or the
    /// drag the Linux host saw end in a trusted drop), checked against the area's options, as read-only grants.
    /// </summary>
    private async Task<SparOutcome> DropAsync(FileRequest request, IReadOnlyList<string>? attached, CancellationToken cancellationToken)
    {
        var accept = request.Strings("accept");
        var folders = request.Boolean("folders") ?? false;
        var limit = request.Int32("limit") ?? DefaultDropLimit;
        if (limit < 1)
        {
            throw new FormatException("'limit' is at least 1.");
        }

        // A claim with no drop behind it (a page's own message, a drag that left) gets nothing, and is no error.
        var paths = attached ?? await window.Drops.ClaimAsync(cancellationToken) ?? [];

        if (paths.Count > limit)
        {
            return DropError("tooMany", writer => writer.WriteNumber("limit", limit));
        }

        foreach (var path in paths)
        {
            var isFolder = Directory.Exists(path);
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
            if (isFolder ? !folders : !Accepts(accept, name))
            {
                return DropError("notAccepted", writer => writer.WriteString("name", name));
            }
        }

        if (!window.Grants.HasRoom(paths.Count))
        {
            var room = Math.Max(0, window.Grants.Capacity - window.Grants.Count);
            return DropError("tooMany", writer => writer.WriteNumber("limit", room));
        }

        var files = new List<FileGrant>();
        var granted = new List<FolderGrant>();
        foreach (var path in paths)
        {
            object? grant = Directory.Exists(path)
                ? window.Grants.AddFolder(path, FolderAccess.Read, null)
                : window.Grants.AddFile(path, canWrite: false, null);
            switch (grant)
            {
                case FileGrant file:
                    files.Add(file);
                    break;
                case FolderGrant folder:
                    granted.Add(folder);
                    break;
                default:
                    return DropError("tooMany", writer => writer.WriteNumber("limit", 0));
            }
        }

        return Value(writer =>
        {
            writer.WriteStartObject();
            writer.WriteStartArray("files"u8);
            files.ForEach(file => SparGrants.Write(writer, file));
            writer.WriteEndArray();
            writer.WriteStartArray("folders"u8);
            granted.ForEach(folder => SparGrants.Write(writer, folder));
            writer.WriteEndArray();
            writer.WriteEndObject();
        });
    }

    /// <summary>Whether the area takes this file: any file without <c>accept</c>, otherwise one of its extensions.</summary>
    private static bool Accepts(IReadOnlyList<string> accept, string name) =>
        accept.Count == 0 || accept.Any(extension => name.EndsWith("." + extension.TrimStart('.'), StringComparison.OrdinalIgnoreCase));

    private static SparOutcome DropError(string code, Action<Utf8JsonWriter>? fields = null) => SparOutcome.FromError(FileRequest.Json(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("code", code);
        fields?.Invoke(writer);
        writer.WriteEndObject();
    }));

    /// <summary>The window's grant the request names; null when the window holds none of that id.</summary>
    private object? Grant(FileRequest request) => window.Grants.Find(request.RequiredString("grant"));

    /// <summary>The file a read or write reaches: the file grant's, or a path inside the folder grant.</summary>
    private Reached Target(FileRequest request, bool write)
    {
        var path = request.String("path");
        switch (Grant(request))
        {
            case FileGrant file when path is not null:
                throw new FormatException("A file grant takes no 'path'.");
            case FileGrant file when !file.IsValid:
                return new Reached("", AccessError(new FileAccessError.GrantExpired()));
            case FileGrant file when write && !file.CanWrite:
                return new Reached("", AccessError(new FileAccessError.ReadOnlyGrant()));
            case FileGrant file:
                return new Reached(file.Path, null);
            case FolderGrant folder:
                var resolved = folder.ResolvePath(path ?? throw new FormatException("A folder grant takes a 'path'."));
                if (!resolved.TryGetValue(out var full))
                {
                    return new Reached("", AccessError(resolved.Error));
                }

                return write && folder.Access != FolderAccess.ReadWrite
                    ? new Reached("", AccessError(new FileAccessError.ReadOnlyGrant()))
                    : new Reached(full, null);
            default:
                return new Reached("", Invalid("The grant is not one of this window's."));
        }
    }

    private readonly record struct Reached(string Path, SparOutcome? Refusal);
}
