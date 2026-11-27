using System.Security.Cryptography;
using System.Text;

namespace Spar.Files;

/// <summary>
/// The file work behind both kinds of grant: reads, safe writes (a temporary file beside the target, flushed, then
/// moved over it, so a reader never sees half a file and a failed write leaves the old one), and the OS's failures
/// turned into <see cref="FileAccessError"/> cases. Everything runs on the caller's thread or the thread pool.
/// </summary>
internal static class GrantIO
{
    // ERROR_SHARING_VIOLATION and ERROR_LOCK_VIOLATION, as HRESULTs.
    private const int SharingViolation = unchecked((int)0x80070020);
    private const int LockViolation = unchecked((int)0x80070021);

    public static async Task<Result<byte[], FileAccessError>> ReadBytesAsync(string path, long limit, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, useAsync: true);
            if (stream.Length > limit)
            {
                return new FileAccessError.TooLarge(limit);
            }

            var bytes = new byte[stream.Length];
            await stream.ReadExactlyAsync(bytes, cancellationToken);
            return bytes;
        }
        catch (Exception exception) when (ToError(exception) is { } error)
        {
            return error;
        }
    }

    public static async Task<Result<string, FileAccessError>> ReadTextAsync(string path, long limit, CancellationToken cancellationToken)
    {
        var read = await ReadBytesAsync(path, limit, cancellationToken);
        if (!read.TryGetValue(out var bytes))
        {
            return read.Error;
        }

        // UTF-8 unless the file says otherwise with its byte order mark, as File.ReadAllText reads.
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    public static Result<Stream, FileAccessError> OpenRead(string path)
    {
        try
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);
        }
        catch (Exception exception) when (ToError(exception) is { } error)
        {
            return error;
        }
    }

    /// <summary>Writes through a temporary file in the target's folder, then replaces the target with it.</summary>
    public static async Task<Result<FileAccessError>> WriteAsync(string path, Func<Stream, CancellationToken, Task> write, CancellationToken cancellationToken)
    {
        var folder = Path.GetDirectoryName(path) ?? throw new ArgumentException("The path has no folder.", nameof(path));
        var temporary = Path.Combine(folder, $".{Path.GetFileName(path)}.{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6))}.tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await write(stream, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            Replace(temporary, path);
            return Result.Success;
        }
        catch (Exception exception) when (ToError(exception) is { } error)
        {
            TryDelete(temporary);
            return error;
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    public static Task<Result<FileAccessError>> WriteBytesAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken) =>
        WriteAsync(path, (stream, token) => stream.WriteAsync(bytes, token).AsTask(), cancellationToken);

    public static Task<Result<FileAccessError>> WriteTextAsync(string path, string text, CancellationToken cancellationToken) =>
        WriteBytesAsync(path, Encoding.UTF8.GetBytes(text), cancellationToken);

    /// <summary>The OS's failure as a declared case; null for one that is not about the file (a cancellation, a bug).</summary>
    public static FileAccessError? ToError(Exception exception) => exception switch
    {
        FileNotFoundException or DirectoryNotFoundException => new FileAccessError.NotFound(),
        UnauthorizedAccessException => new FileAccessError.AccessDenied(),
        IOException { HResult: SharingViolation or LockViolation } => new FileAccessError.InUse(),
        PathTooLongException => new FileAccessError.Failed(exception.Message),
        IOException => new FileAccessError.Failed(exception.Message),
        _ => null,
    };

    private static void Replace(string temporary, string path)
    {
        // File.Replace keeps the target's attributes and, on Windows, its permissions; a new file is simply moved.
        if (File.Exists(path))
        {
            File.Replace(temporary, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            return;
        }

        File.Move(temporary, path);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A temporary file that cannot go now is left beside the target, named so it is recognisable.
        }
    }
}
