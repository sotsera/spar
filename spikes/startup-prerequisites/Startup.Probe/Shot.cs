using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Startup.Probe;

// Screen grabs (BitBlt from the screen DC) saved as PNG. Adapted from spikes/windows-sandbox-checks (Capture.cs).
internal static unsafe class Shot
{
    public static void Screen(Native.Rect area, string path)
    {
        if (area.Width <= 0 || area.Height <= 0)
        {
            return;
        }

        var header = new Native.BitmapInfoHeader
        {
            Size = (uint)sizeof(Native.BitmapInfoHeader),
            Width = area.Width,
            Height = -area.Height,
            Planes = 1,
            BitCount = 32,
        };
        var screen = Native.GetDC(0);
        var dc = Native.CreateCompatibleDC(screen);
        var bitmap = Native.CreateDIBSection(dc, header, 0, out var bits, 0, 0);
        var old = Native.SelectObject(dc, bitmap);
        try
        {
            Native.BitBlt(dc, 0, 0, area.Width, area.Height, screen, area.Left, area.Top, 0x00CC0020); // SRCCOPY
            SavePng(new ReadOnlySpan<uint>((void*)bits, area.Width * area.Height), area.Width, area.Height, path);
        }
        finally
        {
            Native.SelectObject(dc, old);
            Native.DeleteObject(bitmap);
            Native.DeleteDC(dc);
            Native.ReleaseDC(0, screen);
        }
    }

    public static Native.Rect FullScreen() => new() { Right = Native.GetSystemMetrics(0), Bottom = Native.GetSystemMetrics(1) };

    private static void SavePng(ReadOnlySpan<uint> data, int width, int height, string path)
    {
        using var file = File.Create(path);
        file.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..], height);
        ihdr[8] = 8;
        ihdr[9] = 2; // RGB
        Chunk(file, "IHDR", ihdr);
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = new byte[1 + (width * 3)];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var c = data[(y * width) + x];
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
        var typeBytes = Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(buffer, Crc32(Crc32(0xFFFFFFFF, typeBytes), data) ^ 0xFFFFFFFF);
        s.Write(buffer);
    }

    private static uint Crc32(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
            }
        }

        return crc;
    }
}
