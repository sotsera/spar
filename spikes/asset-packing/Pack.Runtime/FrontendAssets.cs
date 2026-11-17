using System.Buffers;
using System.Buffers.Binary;
using System.Reflection;
using System.Text;
#if SPAR_PACK_BROTLI || SPAR_PACK_SEPARATE || SPAR_PACK_ZIP || SPAR_PACK_DEFLATE
using System.IO.Compression;
#endif

namespace Spar.AssetPacking;

/// <summary>One packed file. <see cref="Offset"/> is into the data section, or the resource number (separate format).</summary>
internal sealed record AssetEntry(string Path, string ContentType, byte Encoding, int Offset, int StoredLength, int OriginalLength);

/// <summary>
/// Reads the embedded frontend in the format this build was compiled for (SPAR_PACK_RAW, _BROTLI, _SEPARATE, _ZIP or
/// _NONE). Embedded resources of a NativeAOT image are read in place: the stream is an
/// <see cref="UnmanagedMemoryStream"/> over the image, so stored bytes are never copied. Decoded content is cached.
/// </summary>
internal sealed unsafe class FrontendAssets
{
#if SPAR_PACK_AUTO
    public const string Format = "auto";

    /// <summary>
    /// The library-shaped variant: the Brotli path is compiled in, and the build turns it off with a trimming feature
    /// switch when it packed nothing compressed (RuntimeHostConfigurationOption Spar.Frontend.Brotli, Trim="true").
    /// </summary>
    [System.Diagnostics.CodeAnalysis.FeatureSwitchDefinition("Spar.Frontend.Brotli")]
    private static bool BrotliEnabled => !AppContext.TryGetSwitch("Spar.Frontend.Brotli", out var enabled) || enabled;
#elif SPAR_PACK_RAW
    public const string Format = "raw";
#elif SPAR_PACK_BROTLI
    public const string Format = "brotli";
#elif SPAR_PACK_SEPARATE
    public const string Format = "separate";
#elif SPAR_PACK_ZIP
    public const string Format = "zip";
#elif SPAR_PACK_DEFLATE
    public const string Format = "deflate";
#else
    public const string Format = "none";
#endif

    private readonly Dictionary<string, AssetEntry> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ReadOnlyMemory<byte>> _decoded = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();
    private readonly Assembly _assembly;
    private readonly List<Stream> _resources = [];
#if SPAR_PACK_RAW || SPAR_PACK_BROTLI || SPAR_PACK_DEFLATE
    private byte* _data;
#elif SPAR_PACK_SEPARATE
    private readonly Dictionary<int, nint> _entryResources = [];
#endif
#if SPAR_PACK_ZIP
    private ZipArchive? _archive;
#endif

    private FrontendAssets(Assembly assembly)
    {
        _assembly = assembly;
    }

    public IReadOnlyCollection<AssetEntry> Entries => _entries.Values;

    /// <summary>True when the resource stream was an UnmanagedMemoryStream (read in place, no copy).</summary>
    public bool InPlace { get; private set; } = true;

    public static FrontendAssets Open(Assembly assembly)
    {
        var assets = new FrontendAssets(assembly);
#if SPAR_PACK_RAW || SPAR_PACK_BROTLI || SPAR_PACK_DEFLATE
        var pack = assets.Pin("Spar.Frontend", out var length);
        assets._data = pack + assets.ReadIndex(new ReadOnlySpan<byte>(pack, length));
#elif SPAR_PACK_SEPARATE
        var index = assets.Pin("Spar.Frontend.index", out var length);
        assets.ReadIndex(new ReadOnlySpan<byte>(index, length));
        foreach (var entry in assets._entries.Values)
        {
            var name = "Spar.Frontend." + entry.Offset.ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
            assets._entryResources[entry.Offset] = (nint)assets.Pin(name, out _);
        }
#elif SPAR_PACK_ZIP
        var stream = assembly.GetManifestResourceStream("Spar.Frontend") ?? throw new InvalidOperationException("No embedded frontend.");
        assets._resources.Add(stream);
        assets._archive = new ZipArchive(stream, ZipArchiveMode.Read);
        foreach (var entry in assets._archive.Entries)
        {
            assets._entries[entry.FullName] = new(entry.FullName, ContentTypeOf(entry.FullName), 2, 0, (int)entry.CompressedLength, (int)entry.Length);
        }
#endif
        return assets;
    }

    /// <summary>The decoded content of <paramref name="path"/>, decoding it on first use.</summary>
    public bool TryGet(string path, out AssetEntry entry, out ReadOnlyMemory<byte> content)
    {
        if (!_entries.TryGetValue(path, out entry!))
        {
            content = default;
            return false;
        }

        if (entry.Encoding == 0)
        {
            content = Stored(entry); // In place: nothing to decode or cache.
            return true;
        }

        lock (_lock)
        {
            if (_decoded.TryGetValue(path, out content))
            {
                return true;
            }
        }

        content = Decode(entry);
        lock (_lock)
        {
            _decoded.TryAdd(path, content);
        }

        return true;
    }

    public void DecodeAll()
    {
        foreach (var entry in _entries.Values)
        {
            TryGet(entry.Path, out _, out _);
        }
    }

    public int CachedBytes
    {
        get
        {
            lock (_lock)
            {
                return _decoded.Values.Sum(value => value.Length);
            }
        }
    }

    /// <summary>The bytes as embedded (compressed when <see cref="AssetEntry.Encoding"/> is 1).</summary>
    public ReadOnlyMemory<byte> Stored(AssetEntry entry)
    {
#if SPAR_PACK_RAW || SPAR_PACK_BROTLI || SPAR_PACK_DEFLATE
        return new PointerMemory(_data + entry.Offset, entry.StoredLength).Memory;
#elif SPAR_PACK_SEPARATE
        return new PointerMemory((byte*)_entryResources[entry.Offset], entry.StoredLength).Memory;
#else
        throw new NotSupportedException($"The {Format} format has no stored view of {entry.Path}.");
#endif
    }

    /// <summary>Decodes without the cache (for timing).</summary>
    public byte[] Decode(AssetEntry entry)
    {
        var output = new byte[entry.OriginalLength];
#if SPAR_PACK_BROTLI || SPAR_PACK_SEPARATE
        if (entry.Encoding == 0)
        {
            Stored(entry).Span.CopyTo(output);
        }
#if SPAR_PACK_AUTO
        else if (!BrotliEnabled)
        {
            throw new NotSupportedException($"{entry.Path} is compressed, but this build has no Brotli decoder.");
        }
#endif
        else if (!BrotliDecoder.TryDecompress(Stored(entry).Span, output, out var written) || written != output.Length)
        {
            throw new InvalidDataException($"Brotli decoding failed for {entry.Path}.");
        }
#elif SPAR_PACK_ZIP
        lock (_lock)
        {
            using var stream = _archive!.GetEntry(entry.Path)!.Open();
            stream.ReadExactly(output);
        }
#elif SPAR_PACK_DEFLATE
        if (entry.Encoding == 0)
        {
            Stored(entry).Span.CopyTo(output);
        }
        else
        {
            using var source = new UnmanagedMemoryStream(_data + entry.Offset, entry.StoredLength);
            using var deflate = new DeflateStream(source, CompressionMode.Decompress);
            deflate.ReadExactly(output);
        }
#else
        Stored(entry).Span.CopyTo(output);
#endif
        return output;
    }

    private byte* Pin(string name, out int length)
    {
        var stream = _assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"No embedded resource {name}.");
        _resources.Add(stream);
        length = checked((int)stream.Length);
        if (stream is UnmanagedMemoryStream unmanaged)
        {
            return unmanaged.PositionPointer;
        }

        // Not expected for NativeAOT; a pinned copy keeps the same code path.
        InPlace = false;
        var copy = GC.AllocateUninitializedArray<byte>(length, pinned: true);
        stream.ReadExactly(copy);
        fixed (byte* pointer = copy)
        {
            _resources.Add(new MemoryStream(copy));
            return pointer;
        }
    }

    /// <summary>Parses the index and returns the data offset.</summary>
    private int ReadIndex(ReadOnlySpan<byte> pack)
    {
        if (!pack.StartsWith("SPAK"u8) || BinaryPrimitives.ReadUInt16LittleEndian(pack[4..]) != 1)
        {
            throw new InvalidDataException("Not a version 1 Spar pack.");
        }

        var count = BinaryPrimitives.ReadUInt16LittleEndian(pack[6..]);
        var dataOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(pack[8..]);
        var position = 12;
        for (var i = 0; i < count; i++)
        {
            var encoding = pack[position];
            var pathLength = BinaryPrimitives.ReadUInt16LittleEndian(pack[(position + 1)..]);
            var path = Encoding.UTF8.GetString(pack.Slice(position + 3, pathLength));
            position += 3 + pathLength;
            var typeLength = pack[position];
            var type = Encoding.ASCII.GetString(pack.Slice(position + 1, typeLength));
            position += 1 + typeLength;
            var offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(pack[position..]);
            var stored = (int)BinaryPrimitives.ReadUInt32LittleEndian(pack[(position + 4)..]);
            var original = (int)BinaryPrimitives.ReadUInt32LittleEndian(pack[(position + 8)..]);
            position += 12 + 32; // The SHA-256 is not checked at runtime.
            _entries[path] = new(path, type, encoding, offset, stored, original);
        }

        return dataOffset;
    }

#if SPAR_PACK_ZIP
    private static string ContentTypeOf(string path) => Path.GetExtension(path) switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" => "application/json",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        _ => "application/octet-stream",
    };
#endif

    /// <summary>A Memory over bytes that live as long as the process (the image's resource section).</summary>
    private sealed class PointerMemory(byte* pointer, int length) : MemoryManager<byte>
    {
        public override Span<byte> GetSpan() => new(pointer, length);

        public override MemoryHandle Pin(int elementIndex = 0) => new(pointer + elementIndex);

        public override void Unpin()
        {
        }

        protected override void Dispose(bool disposing)
        {
        }
    }
}
