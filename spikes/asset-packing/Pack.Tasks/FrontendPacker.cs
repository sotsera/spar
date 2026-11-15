using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Spar.AssetPacking;

/// <summary>
/// Packs a built frontend folder into the files that get embedded. Deterministic: entries are sorted by ordinal path,
/// zip timestamps are fixed, and the same encoder settings give the same bytes. Every output is written only when its
/// bytes change, and stale files in the output folder (which this packer owns) are deleted.
/// </summary>
/// <remarks>
/// Pack layout (little endian), shared by the single-resource formats and the index of the separate format:
/// "SPAK" | u16 version (1) | u16 entry count | u32 data offset | entries | data.
/// Entry: u8 encoding (0 identity, 1 brotli) | u16 path length | UTF-8 path | u8 type length | ASCII content type |
/// u32 offset (into the data section; for the separate format, the resource number) | u32 stored length |
/// u32 original length | 32-byte SHA-256 of the original bytes.
/// </remarks>
public static class FrontendPacker
{
    public const ushort Version = 1;

    public static PackReport Pack(string inputDirectory, string outputDirectory, string format, int quality, int window)
    {
        var files = Directory.EnumerateFiles(inputDirectory, "*", SearchOption.AllDirectories)
            .Select(file => (Path: Path.GetRelativePath(inputDirectory, file).Replace(Path.DirectorySeparatorChar, '/'), File: file))
            .OrderBy(entry => entry.Path, StringComparer.Ordinal)
            .ToList();

        var outputs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var report = new PackReport { Format = format };
        switch (format)
        {
            case "raw":
            case "brotli":
            case "deflate":
                {
                    var entries = files.Select(file => format == "deflate"
                        ? EncodeDeflate(file.Path, File.ReadAllBytes(file.File))
                        : Encode(file.Path, File.ReadAllBytes(file.File), format == "brotli", quality, window)).ToList();
                    outputs["frontend.pack"] = WritePack(entries, separate: false);
                    report.Add(entries);
                    break;
                }

            case "separate":
                {
                    var entries = files.Select(file => Encode(file.Path, File.ReadAllBytes(file.File), compress: true, quality, window)).ToList();
                    outputs["index.bin"] = WritePack(entries, separate: true);
                    for (var i = 0; i < entries.Count; i++)
                    {
                        outputs[ResourceFileName(i)] = entries[i].Stored;
                    }

                    report.Add(entries);
                    break;
                }

            case "zip":
                {
                    outputs["frontend.zip"] = WriteZip(files);
                    report.Add(files.Select(file => Encode(file.Path, File.ReadAllBytes(file.File), compress: false, quality, window)).ToList());
                    break;
                }

            default:
                throw new ArgumentException($"Unknown pack format '{format}'.", nameof(format));
        }

        Directory.CreateDirectory(outputDirectory);
        foreach (var stale in Directory.EnumerateFiles(outputDirectory).Where(file => !outputs.ContainsKey(Path.GetFileName(file))))
        {
            File.Delete(stale);
            report.Deleted++;
        }

        foreach (var (name, bytes) in outputs)
        {
            var path = Path.Combine(outputDirectory, name);
            report.EmbeddedBytes += bytes.Length;
            if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
            {
                continue;
            }

            File.WriteAllBytes(path, bytes);
            report.Written++;
        }

        report.Outputs = outputs.Count;
        return report;
    }

    /// <summary>The separate format's per-entry files; the embedding target names each resource after the file.</summary>
    public static string ResourceFileName(int index) => index.ToString("D4", System.Globalization.CultureInfo.InvariantCulture) + ".bin";

    private static PackedEntry Encode(string path, byte[] original, bool compress, int quality, int window)
    {
        var entry = new PackedEntry(path, ContentType(path), original.Length, SHA256.HashData(original), original, Encoding: 0);
        if (!compress)
        {
            return entry;
        }

        // A window that just covers the file keeps the decoder's ring buffer small; 0 asks for that fitted size.
        var bits = window != 0 ? window : Math.Clamp((int)Math.Ceiling(Math.Log2(Math.Max(original.Length, 1))), 10, 24);
        var buffer = new byte[BrotliEncoder.GetMaxCompressedLength(original.Length)];
        if (!BrotliEncoder.TryCompress(original, buffer, out var written, quality, bits))
        {
            throw new InvalidOperationException($"Brotli failed for {path}.");
        }

        // Already-compressed files (fonts, images) stay as they are: Brotli must save at least 5% to be worth decoding.
        return written <= original.Length - Math.Max(16, original.Length / 20)
            ? entry with { Stored = buffer[..written], Encoding = 1 }
            : entry;
    }

    /// <summary>
    /// The control the research review asked for (spike follow-up, 2026-11-15): per-file raw Deflate at the smallest
    /// size inside the same index, encoding 3, with the same 5% rule as Brotli.
    /// </summary>
    private static PackedEntry EncodeDeflate(string path, byte[] original)
    {
        var entry = new PackedEntry(path, ContentType(path), original.Length, SHA256.HashData(original), original, Encoding: 0);
        using var buffer = new MemoryStream();
        using (var deflate = new System.IO.Compression.DeflateStream(buffer, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true))
        {
            deflate.Write(original);
        }

        var written = (int)buffer.Length;
        return written <= original.Length - Math.Max(16, original.Length / 20)
            ? entry with { Stored = buffer.ToArray(), Encoding = 3 }
            : entry;
    }

    private static byte[] WritePack(List<PackedEntry> entries, bool separate)
    {
        using var index = new MemoryStream();
        var scratch = new byte[4];
        void U16(int value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(scratch, checked((ushort)value));
            index.Write(scratch, 0, 2);
        }

        void U32(long value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(scratch, checked((uint)value));
            index.Write(scratch, 0, 4);
        }

        index.Write("SPAK"u8);
        U16(Version);
        U16(entries.Count);
        U32(0); // Data offset, patched below.
        long offset = 0;
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var path = Encoding.UTF8.GetBytes(entry.Path);
            var type = Encoding.ASCII.GetBytes(entry.ContentType);
            index.WriteByte(entry.Encoding);
            U16(path.Length);
            index.Write(path);
            index.WriteByte(checked((byte)type.Length));
            index.Write(type);
            U32(separate ? i : offset);
            U32(entry.Stored.Length);
            U32(entry.OriginalLength);
            index.Write(entry.Hash);
            offset += separate ? 0 : entry.Stored.Length;
        }

        var dataOffset = index.Length;
        if (!separate)
        {
            foreach (var entry in entries)
            {
                index.Write(entry.Stored);
            }
        }

        var bytes = index.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), checked((uint)dataOffset));
        return bytes;
    }

    private static byte[] WriteZip(List<(string Path, string File)> files)
    {
        // The format the earlier spikes embedded: Deflate at SmallestSize, fixed timestamps, sorted entries.
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, file) in files)
            {
                var entry = archive.CreateEntry(path, CompressionLevel.SmallestSize);
                entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var target = entry.Open();
                using var source = File.OpenRead(file);
                source.CopyTo(target);
            }
        }

        return buffer.ToArray();
    }

    /// <summary>Decided at build time, so the runtime needs no extension table.</summary>
    public static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" or ".mjs" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" or ".map" => "application/json",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".ico" => "image/x-icon",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        ".ttf" => "font/ttf",
        ".wasm" => "application/wasm",
        ".txt" => "text/plain; charset=utf-8",
        _ => "application/octet-stream",
    };
}

public sealed record PackedEntry(string Path, string ContentType, int OriginalLength, byte[] Hash, byte[] Stored, byte Encoding);

public sealed class PackReport
{
    public string Format { get; init; } = string.Empty;

    public int Entries { get; private set; }

    public long OriginalBytes { get; private set; }

    public long StoredBytes { get; private set; }

    public int Compressed { get; private set; }

    public long EmbeddedBytes { get; set; }

    public int Outputs { get; set; }

    public int Written { get; set; }

    public int Deleted { get; set; }

    public List<string> Lines { get; } = [];

    public void Add(List<PackedEntry> entries)
    {
        foreach (var entry in entries)
        {
            Entries++;
            OriginalBytes += entry.OriginalLength;
            StoredBytes += entry.Stored.Length;
            Compressed += entry.Encoding == 0 ? 0 : 1;
            Lines.Add($"{entry.Path} {entry.OriginalLength} -> {entry.Stored.Length} ({(entry.Encoding switch { 1 => "br", 3 => "deflate", _ => "identity" })})");
        }
    }
}
