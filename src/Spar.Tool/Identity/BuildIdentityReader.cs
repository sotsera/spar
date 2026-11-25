using System.Text;
using Spar.Runtime;

namespace Spar.Tool.Identity;

/// <summary>
/// Reads the build's identity block (<see cref="BuildIdentity"/>) from an app's file without running it: the bytes of
/// <c>[spar-build-identity/1 …]</c>, which the generator writes as a UTF-8 literal, so they are in a NativeAOT
/// executable, a CoreCLR app's DLL and a single-file bundle as they are. A CoreCLR build's executable is only the
/// apphost, which holds none: the app's DLL beside it, with the same name, is read instead.
/// </summary>
internal static class BuildIdentityReader
{
    private const int ChunkSize = 1 << 20;

    /// <summary>The identity and the file it was read from, or null with the reason.</summary>
    public static (BuildIdentity? Identity, string File, string? Problem) Read(string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full))
        {
            return (null, full, $"{full} does not exist.");
        }

        var found = Find(full);
        if (found is not null)
        {
            return (found, full, null);
        }

        // An apphost: the app is the DLL of the same name beside it (Notes.exe and Notes.dll, or Notes and Notes.dll).
        var beside = Path.Combine(Path.GetDirectoryName(full)!, Path.GetFileNameWithoutExtension(full) + ".dll");
        if (!full.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && File.Exists(beside) && Find(beside) is { } fromDll)
        {
            return (fromDll, beside, null);
        }

        return (null, full, $"{full} holds no build identity: it was not built by Spar.Sdk 0.1 or later, or it is compressed (a single-file bundle with EnableCompressionInSingleFile).");
    }

    /// <summary>The first valid block in the file, or null when it has none.</summary>
    public static BuildIdentity? Find(string file)
    {
        using var stream = File.OpenRead(file);
        return Find(stream);
    }

    /// <summary>
    /// The first valid block in the stream, read in chunks that overlap by a block's length, so a block across two
    /// chunks is found. A marker that is not followed by a valid block (the reader's own constant, in its own file) is
    /// skipped.
    /// </summary>
    public static BuildIdentity? Find(Stream stream)
    {
        var marker = Encoding.ASCII.GetBytes(BuildIdentity.Marker);
        var buffer = new byte[ChunkSize + BuildIdentity.MaxLength];
        var kept = 0;
        while (true)
        {
            var read = stream.ReadAtLeast(buffer.AsSpan(kept, ChunkSize), ChunkSize, throwOnEndOfStream: false);
            var length = kept + read;
            var span = buffer.AsSpan(0, length);
            var offset = 0;
            while (offset < length)
            {
                var at = span[offset..].IndexOf(marker);
                if (at < 0)
                {
                    break;
                }

                var start = offset + at;
                var window = span[start..Math.Min(length, start + BuildIdentity.MaxLength)];
                var end = window.IndexOf((byte)']');
                if (end >= 0 && BuildIdentity.Parse(window[..(end + 1)]) is { } identity)
                {
                    return identity;
                }

                if (end < 0 && read == ChunkSize && start + BuildIdentity.MaxLength > length)
                {
                    // The block may go on in the next chunk: keep it from its start.
                    break;
                }

                offset = start + 1;
            }

            if (read < ChunkSize)
            {
                return null;
            }

            kept = Math.Min(BuildIdentity.MaxLength, length);
            span[(length - kept)..].CopyTo(buffer);
        }
    }
}
