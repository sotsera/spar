using System.Buffers.Binary;
using System.IO.Compression;

namespace Spar.WindowTests;

/// <summary>
/// Reads one pixel of a screenshot: the PNGs the DevTools Protocol returns are 8-bit RGB or RGBA, not interlaced, so
/// this decodes only that (the image chunks inflated, each row unfiltered).
/// </summary>
internal static class Png
{
    public static (byte Red, byte Green, byte Blue) Pixel(byte[] png, int x, int y)
    {
        var width = 0;
        var height = 0;
        var channels = 0;
        using var compressed = new MemoryStream();
        for (var offset = 8; offset < png.Length;)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));
            var type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            var data = png.AsSpan(offset + 8, length);
            if (type == "IHDR")
            {
                width = BinaryPrimitives.ReadInt32BigEndian(data);
                height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                Assert.Equal(8, data[8]);
                channels = data[9] switch { 2 => 3, 6 => 4, _ => throw new NotSupportedException($"PNG colour type {data[9]}") };
                Assert.Equal(0, data[12]);
            }
            else if (type == "IDAT")
            {
                compressed.Write(data);
            }

            offset += length + 12;
        }

        compressed.Position = 0;
        using var inflated = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionMode.Decompress))
        {
            zlib.CopyTo(inflated);
        }

        Assert.InRange(x, 0, width - 1);
        Assert.InRange(y, 0, height - 1);
        var raw = inflated.ToArray();
        var stride = width * channels;
        var previous = new byte[stride];
        var row = new byte[stride];
        for (var line = 0; line <= y; line++)
        {
            var start = line * (stride + 1);
            var filter = raw[start];
            for (var i = 0; i < stride; i++)
            {
                var value = raw[start + 1 + i];
                var left = i >= channels ? row[i - channels] : 0;
                var up = previous[i];
                var upLeft = i >= channels ? previous[i - channels] : 0;
                row[i] = (byte)(filter switch
                {
                    0 => value,
                    1 => value + left,
                    2 => value + up,
                    3 => value + ((left + up) >> 1),
                    4 => value + Paeth(left, up, upLeft),
                    _ => throw new NotSupportedException($"PNG filter {filter}"),
                });
            }

            (previous, row) = (row, previous);
        }

        // After the last swap the wanted row is in `previous`.
        var pixel = x * channels;
        return (previous[pixel], previous[pixel + 1], previous[pixel + 2]);
    }

    private static int Paeth(int left, int up, int upLeft)
    {
        var estimate = left + up - upLeft;
        var toLeft = Math.Abs(estimate - left);
        var toUp = Math.Abs(estimate - up);
        var toUpLeft = Math.Abs(estimate - upLeft);
        return toLeft <= toUp && toLeft <= toUpLeft ? left : toUp <= toUpLeft ? up : upLeft;
    }
}
