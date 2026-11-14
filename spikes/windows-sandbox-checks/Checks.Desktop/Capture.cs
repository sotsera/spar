using System.Buffers.Binary;
using System.IO.Compression;
using Checks.Desktop.Interop;
using static Checks.Desktop.Interop.Native;

namespace Checks.Desktop;

// Pixel captures: PrintWindow frames of the spike window (classified by colour) and whole-screen grabs (Sandbox).
internal static unsafe class Capture
{
    public sealed class Pixels(int width, int height, uint[] data)
    {
        public int Width { get; } = width;
        public int Height { get; } = height;
        public uint[] Data { get; } = data; // 0x00RRGGBB, top-down
    }

    public static Pixels? PrintWindowPixels(nint hwnd)
    {
        GetWindowRect(hwnd, out var r);
        return Grab(r.Width, r.Height, hdc => PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT) != 0);
    }

    public static Pixels? Screen(Rect area)
    {
        var screen = GetDC(0);
        try
        {
            return Grab(area.Width, area.Height, hdc => BitBlt(hdc, 0, 0, area.Width, area.Height, screen, area.Left, area.Top, 0x00CC0020) != 0);
        }
        finally
        {
            ReleaseDC(0, screen);
        }
    }

    private static Pixels? Grab(int width, int height, Func<nint, bool> draw)
    {
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var header = new BitmapInfoHeader
        {
            Size = (uint)sizeof(BitmapInfoHeader),
            Width = width,
            Height = -height,
            Planes = 1,
            BitCount = 32,
        };
        var screen = GetDC(0);
        var dc = CreateCompatibleDC(screen);
        ReleaseDC(0, screen);
        var bitmap = CreateDIBSection(dc, header, 0, out var bits, 0, 0);
        var old = SelectObject(dc, bitmap);
        try
        {
            if (!draw(dc))
            {
                return null;
            }

            var data = new uint[width * height];
            new ReadOnlySpan<uint>((void*)bits, data.Length).CopyTo(data);
            for (var i = 0; i < data.Length; i++)
            {
                data[i] &= 0x00FFFFFF;
            }

            return new Pixels(width, height, data);
        }
        finally
        {
            SelectObject(dc, old);
            DeleteObject(bitmap);
            DeleteDC(dc);
        }
    }

    // The most frequent colour in a band (every second pixel), with its share.
    public static (uint Color, double Share) Dominant(Pixels p, Rect band)
    {
        var counts = new Dictionary<uint, int>();
        var total = 0;
        for (var y = Math.Max(0, band.Top); y < Math.Min(p.Height, band.Bottom); y += 2)
        {
            for (var x = Math.Max(0, band.Left); x < Math.Min(p.Width, band.Right); x += 2)
            {
                var c = p.Data[(y * p.Width) + x];
                counts[c] = counts.GetValueOrDefault(c) + 1;
                total++;
            }
        }

        if (total == 0)
        {
            return (0, 0);
        }

        var best = counts.MaxBy(kv => kv.Value);
        return (best.Key, (double)best.Value / total);
    }

    public static void SavePng(Pixels p, string path)
    {
        using var file = File.Create(path);
        file.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, p.Width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..], p.Height);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 2; // RGB
        Chunk(file, "IHDR", ihdr);
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = new byte[1 + (p.Width * 3)];
            for (var y = 0; y < p.Height; y++)
            {
                row[0] = 0;
                for (var x = 0; x < p.Width; x++)
                {
                    var c = p.Data[(y * p.Width) + x];
                    row[1 + (x * 3)] = (byte)(c >> 16);
                    row[2 + (x * 3)] = (byte)(c >> 8);
                    row[3 + (x * 3)] = (byte)c;
                }

                z.Write(row);
            }
        }

        Chunk(file, "IDAT", raw.ToArray());
        Chunk(file, "IEND", []);
    }

    private static void Chunk(Stream s, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, data.Length);
        s.Write(buffer);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        var crc = Crc32(Crc32(0xFFFFFFFF, typeBytes), data) ^ 0xFFFFFFFF;
        BinaryPrimitives.WriteUInt32BigEndian(buffer, crc);
        s.Write(buffer);
    }

    private static readonly uint[] s_crcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }

    private static uint Crc32(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            crc = s_crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    public static string Hex(uint c) => $"#{c:x6}";
}
