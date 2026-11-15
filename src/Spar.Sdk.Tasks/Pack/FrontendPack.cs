using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Microsoft.Build.Framework;
using Spar.Hosting;

namespace Spar.Sdk.Tasks.Pack;

/// <summary>One file of the built page, by its path relative to the build output with <c>/</c> separators.</summary>
internal sealed record PageFile(string Path, AbsolutePath Source);

/// <summary>A file the packing step refuses, with why (plan section 3c, "asset checks").</summary>
internal sealed record PackProblem(string Path, string Reason);

/// <summary>
/// The page packed into one resource (plan section 1, "Frontend packing"; decision "Frontend packing: the Deflate control
/// keeps Brotli"): an index, then each file, Brotli at quality 11 with a window fitted to the file, or stored as is when
/// Brotli saves less than 5% (fonts, images). Little-endian throughout:
/// <code>
/// "SPARFE" u8 version u8 0 | u32 count | entries | data
/// entry: u8 encoding (0 stored, 1 brotli) | u16 path length | path (UTF-8) | u8 type length | type (ASCII)
///        | u32 offset into data | u32 stored length | u32 original length
/// </code>
/// </summary>
internal static class FrontendPack
{
    public const byte Version = 1;

    /// <summary>Vite's own metadata folder; only its licence file is used, and never served.</summary>
    public const string ViteFolder = ".vite/";

    public const string LicenseFile = ".vite/license.md";

    private const int BrotliQuality = 11;

    private static readonly string[] SecretExtensions = [".pem", ".key", ".p12", ".pfx", ".jks", ".keystore", ".kdbx", ".env"];

    private static readonly string[] SecretNames = ["id_rsa", "id_dsa", "id_ecdsa", "id_ed25519"];

    /// <summary>
    /// The files that must not ship: source maps, hidden files and folders (version control, <c>.env</c>, <c>.npmrc</c>),
    /// key and secret files, links, names that differ only by case, and types no host serves.
    /// </summary>
    public static List<PackProblem> Check(IReadOnlyList<PageFile> files)
    {
        var problems = new List<PackProblem>();
        foreach (var file in files)
        {
            var name = file.Path[(file.Path.LastIndexOf('/') + 1)..];
            if (file.Path.EndsWith(".map", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add(new(file.Path, "a source map: they never ship"));
            }
            else if (file.Path.Split('/').Any(segment => segment.StartsWith('.')))
            {
                problems.Add(new(file.Path, "a hidden file or folder (version control, .env, .npmrc)"));
            }
            else if (SecretExtensions.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase))
                || SecretNames.Any(s => name.StartsWith(s, StringComparison.OrdinalIgnoreCase)))
            {
                problems.Add(new(file.Path, "a key or secret file"));
            }
            else if (new FileInfo(file.Source).LinkTarget is not null)
            {
                problems.Add(new(file.Path, "a link: the page ships files, not links to them"));
            }
            else if (!SparContentTypes.TryGet(file.Path, out _))
            {
                problems.Add(new(file.Path, $"a file type the hosts do not serve ({System.IO.Path.GetExtension(file.Path)})"));
            }
        }

        foreach (var clash in files.GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            problems.Add(new(string.Join(" and ", clash.Select(f => f.Path)), "names that differ only by case, which collide on Windows"));
        }

        return problems;
    }

    /// <summary>The pack's bytes; entries are sorted by ordinal path, so the same files always give the same bytes.</summary>
    public static byte[] Write(IReadOnlyList<PageFile> files)
    {
        var entries = files.OrderBy(f => f.Path, StringComparer.Ordinal).Select(Encode).ToList();

        using var index = new MemoryStream();
        var header = new byte[12];
        "SPARFE"u8.CopyTo(header);
        header[6] = Version;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), (uint)entries.Count);
        index.Write(header);

        var offset = 0;
        foreach (var entry in entries)
        {
            var path = Encoding.UTF8.GetBytes(entry.Path);
            var type = Encoding.ASCII.GetBytes(entry.ContentType);
            var fields = new byte[1 + 2 + path.Length + 1 + type.Length + 12];
            var span = fields.AsSpan();
            span[0] = entry.Encoding;
            BinaryPrimitives.WriteUInt16LittleEndian(span[1..], checked((ushort)path.Length));
            path.CopyTo(span[3..]);
            span[3 + path.Length] = checked((byte)type.Length);
            type.CopyTo(span[(4 + path.Length)..]);
            var numbers = span[(4 + path.Length + type.Length)..];
            BinaryPrimitives.WriteUInt32LittleEndian(numbers, (uint)offset);
            BinaryPrimitives.WriteUInt32LittleEndian(numbers[4..], (uint)entry.Stored.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(numbers[8..], (uint)entry.OriginalLength);
            index.Write(fields);
            offset += entry.Stored.Length;
        }

        foreach (var entry in entries)
        {
            index.Write(entry.Stored);
        }

        return index.ToArray();
    }

    /// <summary>A whole Brotli stream at quality 11, for the notices, which are read rarely and in full.</summary>
    public static byte[] Compress(byte[] original)
    {
        var buffer = new byte[BrotliEncoder.GetMaxCompressedLength(original.Length)];
        if (!BrotliEncoder.TryCompress(original, buffer, out var written, BrotliQuality, 22))
        {
            throw new InvalidOperationException("Brotli could not compress the third-party notices.");
        }

        return buffer[..written];
    }

    private static Entry Encode(PageFile file)
    {
        var original = File.ReadAllBytes(file.Source);
        SparContentTypes.TryGet(file.Path, out var type);

        // A window that just covers the file keeps the decoder's buffer small.
        var bits = Math.Clamp((int)Math.Ceiling(Math.Log2(Math.Max(original.Length, 1))), 10, 24);
        var buffer = new byte[BrotliEncoder.GetMaxCompressedLength(original.Length)];
        var compressed = BrotliEncoder.TryCompress(original, buffer, out var written, BrotliQuality, bits);
        var worthIt = compressed && written <= original.Length - Math.Max(16, original.Length / 20);
        return worthIt
            ? new Entry(file.Path, type, 1, original.Length, buffer[..written])
            : new Entry(file.Path, type, 0, original.Length, original);
    }

    private sealed record Entry(string Path, string ContentType, byte Encoding, int OriginalLength, byte[] Stored);
}
