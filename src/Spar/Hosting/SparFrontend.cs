using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Spar.Hosting;

/// <summary>One file of the packed page.</summary>
internal sealed record SparFrontendEntry(string Path, string ContentType, bool Compressed, int Offset, int StoredLength, int OriginalLength);

/// <summary>
/// The app's packed page, read from its own assembly (the resource <c>Spar.Frontend</c>, written by the packing step;
/// plan section 1). Stored files are served straight from the image; Brotli files are decoded once and cached, all of
/// them by <see cref="DecodeAll"/> on a background thread when the host starts. The format is documented on the
/// packing step's writer.
/// </summary>
internal sealed class SparFrontend
{
    public const string ResourceName = "Spar.Frontend";
    public const string NoticesResourceName = "Spar.ThirdPartyNotices";

    private readonly Dictionary<string, SparFrontendEntry> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte[]> _decoded = new(StringComparer.Ordinal);
    private readonly Assembly _assembly;
    private readonly ReadOnlyMemory<byte> _data;

    private SparFrontend(Assembly assembly, ReadOnlyMemory<byte> pack)
    {
        _assembly = assembly;
        _data = pack[ReadIndex(pack.Span)..];
    }

    public IReadOnlyCollection<SparFrontendEntry> Entries => _entries.Values;

    /// <summary>
    /// Whether this build carries the Brotli decoder. The packing step sets it off when it packs everything stored
    /// (decision "Frontend packing: stored below the decoder's cost"), and the trimmer then removes the decoder.
    /// </summary>
    [FeatureSwitchDefinition("Spar.Frontend.Brotli")]
    internal static bool IsBrotliSupported { get; } = !AppContext.TryGetSwitch("Spar.Frontend.Brotli", out var on) || on;

    /// <summary>A pack held in memory (tests).</summary>
    internal static SparFrontend Read(ReadOnlyMemory<byte> pack) => new(typeof(SparFrontend).Assembly, pack);

    /// <summary>The app's page, or <c>null</c> when this build embeds none (Development serves it from Vite).</summary>
    public static SparFrontend? Open(Assembly assembly)
    {
        using var stream = assembly.GetManifestResourceStream(ResourceName);
        return stream is null ? null : new SparFrontend(assembly, InImage(stream));
    }

    /// <summary>The bytes of one file, decoding it on first use.</summary>
    public bool TryGet(string path, out ReadOnlyMemory<byte> content, out string contentType)
    {
        if (!_entries.TryGetValue(path, out var entry))
        {
            content = default;
            contentType = "";
            return false;
        }

        contentType = entry.ContentType;
        content = entry.Compressed ? _decoded.GetOrAdd(path, _ => Decode(entry)) : Stored(entry);
        return true;
    }

    /// <summary>
    /// Whether <paramref name="content"/> (from <see cref="TryGet"/>) lies in the executable's mapped image, which lives as
    /// long as the process: a host may then hand it to native code in place, without pinning or copying it.
    /// </summary>
    public static bool IsImageMemory(ReadOnlyMemory<byte> content) =>
        MemoryMarshal.TryGetMemoryManager<byte, ImageMemory>(content, out _);

    /// <summary>Decodes every compressed file into the cache; hosts call it off the UI thread at start.</summary>
    public void DecodeAll()
    {
        foreach (var entry in _entries.Values.Where(e => e.Compressed))
        {
            _decoded.GetOrAdd(entry.Path, _ => Decode(entry));
        }
    }

    /// <summary>The frontend's third-party licences, as the build collected them, or an empty string.</summary>
    public string Notices()
    {
        using var stream = _assembly.GetManifestResourceStream(NoticesResourceName);
        return stream is null ? "" : ReadNotices(stream);
    }

    /// <summary>The notices resource's text: one byte says how it follows, 0 stored or 1 Brotli.</summary>
    internal static string ReadNotices(Stream stream)
    {
        if (stream.Length == 0)
        {
            return "";
        }

        // One byte says how the text follows: 0 stored, 1 Brotli.
        var encoding = stream.ReadByte();
        if (encoding == 0)
        {
            using var plain = new StreamReader(stream, Encoding.UTF8);
            return plain.ReadToEnd();
        }

        using var decoded = new BrotliStream(Brotli(), CompressionMode.Decompress);
        using var reader = new StreamReader(decoded, Encoding.UTF8);
        return reader.ReadToEnd();

        Stream Brotli() => IsBrotliSupported ? stream : throw NoDecoder("the third-party notices");
    }

    private ReadOnlyMemory<byte> Stored(SparFrontendEntry entry) => _data.Slice(entry.Offset, entry.StoredLength);

    private byte[] Decode(SparFrontendEntry entry)
    {
        if (!IsBrotliSupported)
        {
            throw NoDecoder($"'{entry.Path}'");
        }

        var output = new byte[entry.OriginalLength];
        if (!BrotliDecoder.TryDecompress(Stored(entry).Span, output, out var written) || written != output.Length)
        {
            throw new InvalidDataException($"The packed page's '{entry.Path}' could not be decoded.");
        }

        return output;
    }

    private static InvalidDataException NoDecoder(string what) =>
        new($"The packed page's {what} is Brotli-compressed, but this build has no Brotli decoder (Spar.Frontend.Brotli is off).");

    /// <summary>Reads the index; returns where the data starts.</summary>
    private int ReadIndex(ReadOnlySpan<byte> pack)
    {
        if (pack.Length < 12 || !pack[..6].SequenceEqual("SPARFE"u8) || pack[6] != 1)
        {
            throw new InvalidDataException("The embedded page is not a Spar pack of version 1.");
        }

        var count = BinaryPrimitives.ReadUInt32LittleEndian(pack[8..]);
        var position = 12;
        for (var i = 0; i < count; i++)
        {
            var compressed = pack[position] == 1;
            var pathLength = BinaryPrimitives.ReadUInt16LittleEndian(pack[(position + 1)..]);
            var path = Encoding.UTF8.GetString(pack.Slice(position + 3, pathLength));
            position += 3 + pathLength;
            var typeLength = pack[position];
            var type = Encoding.ASCII.GetString(pack.Slice(position + 1, typeLength));
            position += 1 + typeLength;
            var offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(pack[position..]);
            var stored = (int)BinaryPrimitives.ReadUInt32LittleEndian(pack[(position + 4)..]);
            var original = (int)BinaryPrimitives.ReadUInt32LittleEndian(pack[(position + 8)..]);
            position += 12;
            _entries[path] = new SparFrontendEntry(path, type, compressed, offset, stored, original);
        }

        return position;
    }

    /// <summary>
    /// The resource's bytes. In a NativeAOT or CoreCLR app the resource is mapped with the image, so the stream is an
    /// <see cref="UnmanagedMemoryStream"/> and nothing is copied; any other stream is read into memory once.
    /// </summary>
    private static ReadOnlyMemory<byte> InImage(Stream stream)
    {
        if (stream is UnmanagedMemoryStream mapped)
        {
            return ImageMemory.Over(mapped);
        }

        var copy = new byte[stream.Length];
        stream.ReadExactly(copy);
        return copy;
    }

    /// <summary>Memory over bytes that live as long as the image they are mapped from.</summary>
    private sealed class ImageMemory : MemoryManager<byte>
    {
        private readonly nint _start;
        private readonly int _length;

        private ImageMemory(nint start, int length)
        {
            _start = start;
            _length = length;
        }

        public static unsafe ReadOnlyMemory<byte> Over(UnmanagedMemoryStream stream) =>
            new ImageMemory((nint)stream.PositionPointer, checked((int)stream.Length)).Memory;

        public override unsafe Span<byte> GetSpan() => new((void*)_start, _length);

        public override unsafe MemoryHandle Pin(int elementIndex = 0) => new((byte*)_start + elementIndex);

        public override void Unpin()
        {
        }

        protected override void Dispose(bool disposing)
        {
        }
    }
}
