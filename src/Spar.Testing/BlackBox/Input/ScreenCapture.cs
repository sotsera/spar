using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Spar.Testing;

/// <summary>
/// A screenshot of the whole screen as a PNG file, for a Sandbox check's evidence: what the guest showed when the check
/// read it. Only in the user session of a guest Spar's runner started (<see cref="SandboxGuest.EnsureUserSession"/>):
/// the developer's screen is never captured.
/// </summary>
internal static unsafe class ScreenCapture
{
    /// <summary>Saves the screen to <paramref name="path"/> (its folder is created).</summary>
    [SupportedOSPlatform("windows10.0.17763")]
    public static void Save(string path)
    {
        SandboxGuest.EnsureUserSession("capture the screen");
        var left = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_XVIRTUALSCREEN);
        var top = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_YVIRTUALSCREEN);
        var width = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXVIRTUALSCREEN);
        var height = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYVIRTUALSCREEN);
        var pixels = new byte[width * height * 4];

        var screen = PInvoke.GetDC(default);
        var memory = PInvoke.CreateCompatibleDC(screen);
        var bitmap = PInvoke.CreateCompatibleBitmap(screen, width, height);
        try
        {
            var previous = PInvoke.SelectObject(memory, bitmap);
            PInvoke.BitBlt(memory, 0, 0, width, height, screen, left, top, ROP_CODE.SRCCOPY);
            PInvoke.SelectObject(memory, previous);

            // Top-down 32-bit BGRA rows.
            var info = new BITMAPINFO();
            info.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
            info.bmiHeader.biWidth = width;
            info.bmiHeader.biHeight = -height;
            info.bmiHeader.biPlanes = 1;
            info.bmiHeader.biBitCount = 32;
            fixed (byte* target = pixels)
            {
                _ = PInvoke.GetDIBits(memory, bitmap, 0, (uint)height, target, &info, DIB_USAGE.DIB_RGB_COLORS);
            }
        }
        finally
        {
            PInvoke.DeleteObject(bitmap);
            PInvoke.DeleteDC(memory);
            _ = PInvoke.ReleaseDC(default, screen);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, Png(width, height, pixels));
    }

    /// <summary>An 8-bit RGB PNG of top-down BGRA rows, each filtered with "none".</summary>
    internal static byte[] Png(int width, int height, ReadOnlySpan<byte> bgra)
    {
        var raw = new byte[height * ((width * 3) + 1)];
        var at = 0;
        for (var y = 0; y < height; y++)
        {
            raw[at++] = 0;
            var row = bgra.Slice(y * width * 4, width * 4);
            for (var x = 0; x < width; x++)
            {
                raw[at++] = row[(x * 4) + 2];
                raw[at++] = row[(x * 4) + 1];
                raw[at++] = row[x * 4];
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        using var png = new MemoryStream();
        png.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = 8; // bit depth
        header[9] = 2; // RGB
        Chunk(png, "IHDR"u8, header);
        Chunk(png, "IDAT"u8, compressed.ToArray());
        Chunk(png, "IEND"u8, []);
        return png.ToArray();
    }

    private static void Chunk(Stream png, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        png.Write(number);
        png.Write(type);
        png.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(number, ~Crc(Crc(uint.MaxValue, type), data));
        png.Write(number);
    }

    /// <summary>PNG's CRC-32 (ISO 3309, reflected 0xEDB88320), bit by bit: a screenshot is written once per check.</summary>
    private static uint Crc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return crc;
    }
}
