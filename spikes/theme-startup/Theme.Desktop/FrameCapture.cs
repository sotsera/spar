using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using Theme.Desktop.Interop;

namespace Theme.Desktop;

/// <summary>Colour statistics of one region of one captured frame (shares are of sampled pixels, 0..1).</summary>
internal readonly record struct RegionStats(uint Dominant, double DominantShare, double White, double Light, double Dark, double Black, double App, double Splash)
{
    public string DominantHex => "#" + Dominant.ToString("x6", CultureInfo.InvariantCulture);
}

/// <summary>
/// Captures the window as DWM composes it, as fast as PrintWindow(PW_RENDERFULLCONTENT) allows, on its own thread.
/// Works for a window placed offscreen. Every frame is reduced to colour statistics for the client area and the title
/// bar; a PNG is written whenever the frame's signature changes. Timestamps share the host's clock.
/// </summary>
internal sealed unsafe class FrameCapture
{
    // Theme colours and markers (the page's header and the splash mark), see the frontend.
    public const uint LightBackground = 0xf3f3f3;
    public const uint DarkBackground = 0x1f1f1f;

    private readonly nint _window;
    private readonly Stopwatch _clock;
    private readonly string _directory;
    private readonly TimeSpan _duration;
    private readonly List<string> _frames = [];
    private readonly Thread _thread;
    private volatile bool _stop;
    private int _pngs;

    public FrameCapture(nint window, Stopwatch clock, string directory, TimeSpan duration)
    {
        _window = window;
        _clock = clock;
        _directory = directory;
        _duration = duration;
        _thread = new Thread(Run) { Name = "Frame capture", IsBackground = true, Priority = ThreadPriority.AboveNormal };
    }

    public int Count { get; private set; }

    public void Start() => _thread.Start();

    private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Stops capturing. Never block the window thread on this: PrintWindow waits for that thread, so a Join there
    /// deadlocks. Await it while the thread keeps pumping.
    /// </summary>
    public Task StopAsync()
    {
        _stop = true;
        return _finished.Task;
    }

    /// <summary>JSON array of frame records.</summary>
    public string FramesJson()
    {
        lock (_frames)
        {
            return "[\n" + string.Join(",\n", _frames) + "\n]";
        }
    }

    private void Run()
    {
        User32.GetWindowRect(_window, out var bounds);
        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        var origin = new Point();
        User32.ClientToScreen(_window, ref origin);
        User32.GetClientRect(_window, out var client);
        var clientLeft = origin.X - bounds.Left;
        var clientTop = origin.Y - bounds.Top;
        var clientRect = new Rect { Left = clientLeft, Top = clientTop, Right = clientLeft + client.Right, Bottom = clientTop + client.Bottom };

        // The caption band: between the top resize border and the client area, away from the caption buttons.
        var titleRect = new Rect { Left = clientLeft + 8, Top = Math.Max(1, clientTop - 24), Right = clientLeft + client.Right - 160, Bottom = clientTop - 4 };

        var screen = User32.GetDC(0);
        var memory = Gdi32.CreateCompatibleDC(screen);
        var header = new BitmapInfoHeader { Size = (uint)sizeof(BitmapInfoHeader), Width = width, Height = -height, Planes = 1, BitCount = 32 };
        void* bits;
        var bitmap = Gdi32.CreateDIBSection(memory, &header, 0, &bits, 0, 0);
        var previous = Gdi32.SelectObject(memory, bitmap);
        var pixels = (uint*)bits;
        var bins = new int[32768];
        string? lastSignature = null;
        var started = _clock.Elapsed;

        try
        {
            while (!_stop && _clock.Elapsed - started < _duration)
            {
                // Visible and uncloaked both before and after the print, or the frame does not count as seen.
                var cloakedBefore = 0;
                Dwm.DwmGetWindowAttribute(_window, Dwm.Cloaked, &cloakedBefore, sizeof(int));
                var visibleBefore = User32.IsWindowVisible(_window);
                var at = _clock.Elapsed.TotalMilliseconds;
                new Span<uint>(pixels, width * height).Clear();
                var printed = User32.PrintWindow(_window, memory, User32.PwRenderFullContent);
                var took = _clock.Elapsed.TotalMilliseconds - at;
                var visible = visibleBefore && User32.IsWindowVisible(_window);
                var cloaked = cloakedBefore;
                Dwm.DwmGetWindowAttribute(_window, Dwm.Cloaked, &cloaked, sizeof(int));
                cloaked |= cloakedBefore;

                var clientStats = AnalyseRegion(pixels, width, clientRect, bins);
                var titleStats = AnalyseRegion(pixels, width, titleRect, bins);
                var signature = $"{visible && cloaked == 0}|" + Signature(clientStats, titleStats);
                string? png = null;
                if (signature != lastSignature && _pngs < 120)
                {
                    png = $"frame-{Count:D4}-{at:F0}ms.png";
                    Png.Write(Path.Combine(_directory, png), pixels, width, height, 2, width);
                    _pngs++;
                }

                lastSignature = signature;
                var record = new StringBuilder(256);
                record.Append(CultureInfo.InvariantCulture, $"{{\"i\":{Count},\"at\":{at:F1},\"took\":{took:F1},\"ok\":{(printed ? 1 : 0)},\"visible\":{(visible ? 1 : 0)},\"cloaked\":{(cloaked != 0 ? 1 : 0)},");
                record.Append("\"client\":").Append(Json(clientStats)).Append(",\"title\":").Append(Json(titleStats));
                if (png is not null)
                {
                    record.Append(",\"png\":\"").Append(png).Append('"');
                }

                record.Append('}');
                lock (_frames)
                {
                    _frames.Add(record.ToString());
                }

                Count++;
            }
        }
        finally
        {
            Gdi32.SelectObject(memory, previous);
            Gdi32.DeleteObject(bitmap);
            Gdi32.DeleteDC(memory);
            User32.ReleaseDC(0, screen);
            _finished.TrySetResult();
        }
    }

    /// <summary>A coarse key that changes when what a person would notice changes.</summary>
    public static string Signature(RegionStats client, RegionStats title) => Bucket(client) + "|" + Bucket(title);

    private static string Bucket(RegionStats s) =>
        $"{s.Dominant >> 3 & 0x1f1f1f:x6}{(s.App > 0.02 ? "A" : "")}{(s.Splash > 0.003 ? "S" : "")}";

    public static string Json(RegionStats s) => string.Create(
        CultureInfo.InvariantCulture,
        $"{{\"dom\":\"{s.DominantHex}\",\"domShare\":{s.DominantShare:F3},\"white\":{s.White:F3},\"light\":{s.Light:F3},\"dark\":{s.Dark:F3},\"black\":{s.Black:F3},\"app\":{s.App:F3},\"splash\":{s.Splash:F3}}}");

    public static RegionStats AnalyseRegion(uint* pixels, int stride, Rect rect, int[] bins)
    {
        Array.Clear(bins);
        long white = 0, light = 0, dark = 0, black = 0, app = 0, splash = 0, total = 0;
        for (var y = rect.Top; y < rect.Bottom; y += 2)
        {
            var row = pixels + (y * stride);
            for (var x = rect.Left; x < rect.Right; x += 2)
            {
                var p = row[x] & 0xffffff; // BGRA in memory, so 0x00RRGGBB as a little-endian uint.
                int r = (int)(p >> 16), g = (int)(p >> 8) & 0xff, b = (int)p & 0xff;
                bins[(r >> 3 << 10) | (g >> 3 << 5) | (b >> 3)]++;
                total++;
                if (r >= 0xfa && g >= 0xfa && b >= 0xfa) white++;
                else if (Near(r, g, b, LightBackground, 6)) light++;
                else if (Near(r, g, b, DarkBackground, 6)) dark++;
                else if (r <= 8 && g <= 8 && b <= 8) black++;
                else if (b - r > 80 && b - g > 40) app++;
                else if (r - b > 50 && g - b > 15 && r >= g) splash++;
            }
        }

        var top = 0;
        for (var i = 1; i < bins.Length; i++)
        {
            if (bins[i] > bins[top]) top = i;
        }

        // Representative colour: the bin's centre.
        var dominant = (uint)((((top >> 10) & 31) << 3 | 4) << 16 | (((top >> 5) & 31) << 3 | 4) << 8 | ((top & 31) << 3 | 4));
        double share(long n) => total == 0 ? 0 : (double)n / total;
        return new RegionStats(dominant, share(bins[top]), share(white), share(light), share(dark), share(black), share(app), share(splash));
    }

    private static bool Near(int r, int g, int b, uint colour, int tolerance) =>
        Math.Abs(r - (int)(colour >> 16)) <= tolerance && Math.Abs(g - (int)((colour >> 8) & 0xff)) <= tolerance && Math.Abs(b - (int)(colour & 0xff)) <= tolerance;
}

/// <summary>Minimal PNG writer (8-bit RGB, no filtering), downscaled by an integer factor.</summary>
internal static unsafe class Png
{
    private static readonly uint[] s_crcTable = BuildCrcTable();

    public static void Write(string path, uint* pixels, int stride, int height, int scale, int width)
    {
        var w = width / scale;
        var h = height / scale;
        using var raw = new MemoryStream();
        using (var zlib = new ZLibStream(raw, CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = new byte[1 + (w * 3)];
            for (var y = 0; y < h; y++)
            {
                var source = pixels + (y * scale * stride);
                for (var x = 0; x < w; x++)
                {
                    var p = source[x * scale];
                    row[1 + (x * 3)] = (byte)(p >> 16);
                    row[2 + (x * 3)] = (byte)(p >> 8);
                    row[3 + (x * 3)] = (byte)p;
                }

                zlib.Write(row);
            }
        }

        using var file = File.Create(path);
        file.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Span<byte> ihdr = stackalloc byte[13];
        WriteBigEndian(ihdr, (uint)w);
        WriteBigEndian(ihdr[4..], (uint)h);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 2; // RGB
        Chunk(file, "IHDR"u8, ihdr);
        Chunk(file, "IDAT"u8, raw.ToArray());
        Chunk(file, "IEND"u8, []);
    }

    private static void Chunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        WriteBigEndian(number, (uint)data.Length);
        output.Write(number);
        output.Write(type);
        output.Write(data);
        var crc = 0xffffffffu;
        foreach (var b in type) crc = s_crcTable[(crc ^ b) & 0xff] ^ (crc >> 8);
        foreach (var b in data) crc = s_crcTable[(crc ^ b) & 0xff] ^ (crc >> 8);
        WriteBigEndian(number, crc ^ 0xffffffffu);
        output.Write(number);
    }

    private static void WriteBigEndian(Span<byte> target, uint value)
    {
        target[0] = (byte)(value >> 24);
        target[1] = (byte)(value >> 16);
        target[2] = (byte)(value >> 8);
        target[3] = (byte)value;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (var n = 0u; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xedb88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }

        return table;
    }
}
