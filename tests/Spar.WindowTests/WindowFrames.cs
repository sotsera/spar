using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Spar.WindowTests;

/// <summary>
/// What a test window shows over time on Windows, read back without the screen: <c>PrintWindow</c> with
/// <c>PW_RENDERFULLCONTENT</c> renders the window's composed content, WebView2's included, into memory, which works for
/// the test windows placed offscreen and never activated (nothing is read from, or shown on, the user's monitors). The
/// middle of the client area and a point near its top-left corner are sampled about every 16 ms, as DWM composes.
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class WindowFrames
{
    private const uint PrintClientOnly = 1;
    private const uint PrintRenderFullContent = 2;
    private const int Inset = 16;

    /// <summary>Magenta, which neither the probe nor an engine paints.</summary>
    private const uint Unpainted = 0x00FF00FF;

    /// <summary>The visible top-level window of this process with <paramref name="title"/>, or 0.</summary>
    public static nint Find(string title)
    {
        var processId = (uint)Environment.ProcessId;
        var found = nint.Zero;
        var chars = stackalloc char[256];
        for (var window = FindWindowExW(0, 0, null, null); window != 0; window = FindWindowExW(0, window, null, null))
        {
            if (GetWindowThreadProcessId(window, out var owner) == 0 || owner != processId || !IsWindowVisible(window))
            {
                continue;
            }

            var length = GetWindowTextW(window, chars, 256);
            if (new string(chars, 0, length) == title)
            {
                found = window;
            }
        }

        return found;
    }

    /// <summary>
    /// Samples <paramref name="window"/> until <paramref name="stop"/> is set: the time since <paramref name="started"/>
    /// and the two colours as <c>0xRRGGBB</c>.
    /// </summary>
    public static List<(double Milliseconds, uint Middle, uint Corner)> SampleMiddleAndCorner(nint window, Stopwatch started, CancellationToken stop)
    {
        if (!GetClientRect(window, out var client))
        {
            throw new InvalidOperationException("The window's client area cannot be read.");
        }

        var width = client.Right - client.Left;
        var height = client.Bottom - client.Top;
        var screen = GetDC(0);
        var memory = CreateCompatibleDC(screen);
        var header = new BitmapInfoHeader
        {
            Size = (uint)sizeof(BitmapInfoHeader),
            Width = width,
            Height = -height,
            Planes = 1,
            BitCount = 32,
        };
        void* bits;
        var bitmap = CreateDIBSection(memory, &header, 0, &bits, 0, 0);
        var previous = SelectObject(memory, bitmap);
        var samples = new List<(double, uint, uint)>(512);
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var due = started.Elapsed.TotalMilliseconds + 16;
                var pixels = (uint*)bits;
                var middleAt = (height / 2 * width) + (width / 2);
                var cornerAt = (Inset * width) + Inset;

                // A sentinel no page paints: a print that drew nothing (a window not composed yet) leaves it, and is
                // not a frame; without it, the bitmap's zeros read as black.
                pixels[middleAt] = Unpainted;
                pixels[cornerAt] = Unpainted;
                if (PrintWindow(window, memory, PrintClientOnly | PrintRenderFullContent)
                    && pixels[middleAt] != Unpainted && pixels[cornerAt] != Unpainted)
                {
                    samples.Add((started.Elapsed.TotalMilliseconds, pixels[middleAt] & 0xFFFFFF, pixels[cornerAt] & 0xFFFFFF));
                }

                var wait = due - started.Elapsed.TotalMilliseconds;
                if (wait > 1)
                {
                    Thread.Sleep((int)wait);
                }
            }
        }
        finally
        {
            SelectObject(memory, previous);
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(0, screen);
        }

        return samples;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary><c>BITMAPINFOHEADER</c>, 40 bytes; a negative height is a top-down bitmap, <c>BI_RGB</c> is 0.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowExW(nint parent, nint after, string? className, string? title);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint window);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowTextW(nint window, char* text, int length);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(nint window, out Rect rect);

    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint window);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint window, nint dc);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PrintWindow(nint window, nint dc, uint flags);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint dc);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateDIBSection(nint dc, BitmapInfoHeader* info, uint usage, void** bits, nint section, uint offset);

    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint dc, nint item);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint item);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint dc);
}
