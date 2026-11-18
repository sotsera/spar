using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;

namespace Spar.WindowAutomation.Linux.Automation;

/// <summary>
/// How a window test's input reaches the page (decision "Trusted input in Linux desktop tests": one interface, so a
/// Wayland route can be added later). Coordinates are viewport CSS pixels.
/// </summary>
internal interface IInput
{
    string Name { get; }

    bool Trusted { get; }

    Task MoveAsync(View view, double x, double y);

    Task ClickAsync(View view, double x, double y);

    Task PressAsync(View view, string key);

    Task TypeAsync(View view, string text);
}

/// <summary>
/// Trusted input through XTEST: the view's place on the X screen (the window's XID and root origin, the surface
/// transform, the view's offset in the window) plus the CSS point. Keys go to the X input focus, which this backend
/// moves to the window first when it is elsewhere.
/// </summary>
internal sealed class XtestInput(XInput x, string focusMode) : IInput
{
    public string Name => "xtest";

    public bool Trusted => true;

    /// <summary>Key strokes that found the X focus elsewhere and moved it to the test's window.</summary>
    public int FocusMoves { get; private set; }

    public async Task MoveAsync(View view, double cssX, double cssY)
    {
        var (rx, ry) = await ScreenPointAsync(view, cssX, cssY);
        x.Move(rx, ry);
    }

    public async Task ClickAsync(View view, double cssX, double cssY)
    {
        var (rx, ry) = await ScreenPointAsync(view, cssX, cssY);
        x.Click(rx, ry);
    }

    public async Task PressAsync(View view, string key)
    {
        await FocusAsync(view);
        if (key.Length <= 2 && x.Prepare(key).Remapped)
        {
            await MappingSeenAsync();
        }

        x.Press(key);
    }

    public async Task TypeAsync(View view, string text)
    {
        await FocusAsync(view);
        while (text.Length > 0)
        {
            var (length, remapped) = x.Prepare(text);
            if (remapped)
            {
                await MappingSeenAsync();
            }

            x.Type(text[..length]);
            text = text[length..];
        }
    }

    // After a keymap change, GTK must read the MappingNotify (an X event, dispatched before idle sources) before the
    // next key; two turns of the UI thread's idle queue follow it.
    private static async Task MappingSeenAsync()
    {
        await Ui.Invoke(() => { });
        await Ui.Invoke(() => { });
    }

    public async Task<(int X, int Y)> ScreenPointAsync(View view, double cssX, double cssY)
    {
        var geometry = await view.GeometryAsync();
        var (ox, oy) = x.RootOrigin(geometry.Xid);
        return ((int)Math.Round(ox + geometry.X + cssX), (int)Math.Round(oy + geometry.Y + cssY));
    }

    private async Task FocusAsync(View view)
    {
        if (focusMode == "never")
        {
            return;
        }

        var geometry = await view.GeometryAsync();
        if (focusMode == "always" || x.Focus() != geometry.Xid)
        {
            x.SetFocus(geometry.Xid);
            FocusMoves++;
        }
    }
}

/// <summary>Untrusted input: events dispatched by page script (isTrusted false, no user activation).</summary>
internal sealed class ScriptInput(Func<View, string, Task<JsonElement>> engine) : IInput
{
    public string Name => "script";

    public bool Trusted => false;

    public Task MoveAsync(View view, double x, double y) => Task.CompletedTask;

    public Task ClickAsync(View view, double x, double y) =>
        engine(view, $$"""{"op":"synthClick","x":{{x.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"y":{{y.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}""");

    public Task PressAsync(View view, string key) => engine(view, $$"""{"op":"synthKey","key":{{Json.String(key)}}}""");

    public async Task TypeAsync(View view, string text)
    {
        foreach (var c in text)
        {
            await PressAsync(view, c.ToString());
        }
    }
}

internal static class Json
{
    /// <summary>A JSON string literal (also a valid JavaScript string literal).</summary>
    public static string String(string value) => "\"" + JsonEncodedText.Encode(value).ToString() + "\"";

    public static string Bool(bool value) => value ? "true" : "false";
}

/// <summary>A minimal PNG encoder for a crop of a premultiplied BGRA (cairo ARGB32, little-endian) buffer.</summary>
internal static class Png
{
    public static byte[] Encode(byte[] bgra, int width, int height, int x, int y, int w, int h)
    {
        x = Math.Clamp(x, 0, width);
        y = Math.Clamp(y, 0, height);
        w = Math.Clamp(w, 0, width - x);
        h = Math.Clamp(h, 0, height - y);
        var raw = new byte[h * (w * 4 + 1)];
        for (var row = 0; row < h; row++)
        {
            var o = row * (w * 4 + 1);
            for (var col = 0; col < w; col++)
            {
                var i = ((y + row) * width + x + col) * 4;
                int b = bgra[i], g = bgra[i + 1], r = bgra[i + 2], a = bgra[i + 3];
                if (a is > 0 and < 255)
                {
                    r = r * 255 / a;
                    g = g * 255 / a;
                    b = b * 255 / a;
                }

                var p = o + 1 + col * 4;
                raw[p] = (byte)r;
                raw[p + 1] = (byte)g;
                raw[p + 2] = (byte)b;
                raw[p + 3] = (byte)a;
            }
        }

        using var output = new MemoryStream();
        output.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0d, 0x0a, 0x1a, 0x0a]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, w);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), h);
        header[8] = 8;
        header[9] = 6;
        Chunk(output, "IHDR", header);
        using (var compressed = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            {
                zlib.Write(raw);
            }

            Chunk(output, "IDAT", compressed.ToArray());
        }

        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void Chunk(Stream output, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        var crc = Crc(Crc(0xffffffffu, typeBytes), data) ^ 0xffffffffu;
        Span<byte> sum = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(sum, crc);
        output.Write(sum);
    }

    private static readonly uint[] s_table = [.. Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++)
        {
            c = (c & 1) != 0 ? 0xedb88320u ^ (c >> 1) : c >> 1;
        }

        return c;
    })];

    private static uint Crc(uint crc, byte[] data)
    {
        foreach (var b in data)
        {
            crc = s_table[(crc ^ b) & 0xff] ^ (crc >> 8);
        }

        return crc;
    }
}
