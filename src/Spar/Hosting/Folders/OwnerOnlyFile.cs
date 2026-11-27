using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace Spar.Hosting;

/// <summary>
/// Files only their owner can read or write, from their creation: the loopback dev session's record and the devtools'
/// discovery file hold a token. On Windows a protected access list (nothing inherited from the folder) grants the
/// current user alone; elsewhere the mode is 0600.
/// </summary>
internal static class OwnerOnlyFile
{
    /// <summary>A new file at <paramref name="path"/>, replacing one that was there, open for writing.</summary>
    public static FileStream Create(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return CreateOnWindows(path);
        }

        var options = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
        };
        return new FileStream(path, options);
    }

    /// <summary>
    /// Writes one JSON object to <paramref name="file"/> through an owner-only temporary file moved over it, so a reader
    /// never sees half a file; false when it cannot (the caller goes on without it).
    /// </summary>
    public static bool WriteJson(string file, Action<Utf8JsonWriter> write)
    {
        var temporary = $"{file}.{Environment.ProcessId}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            using (var stream = Create(temporary))
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                write(writer);
                writer.WriteEndObject();
            }

            File.Move(temporary, file, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDelete(temporary);
            return false;
        }
    }

    public static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A file another process holds, or one already gone; the next writer replaces it.
        }
    }

    [SupportedOSPlatform("windows")]
    private static FileStream CreateOnWindows(string path)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var security = new FileSecurity();
        security.SetOwner(identity.User!);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.FullControl, AccessControlType.Allow));
        return new FileInfo(path).Create(FileMode.Create, FileSystemRights.Write | FileSystemRights.ReadData, FileShare.None, 4096, FileOptions.None, security);
    }
}
