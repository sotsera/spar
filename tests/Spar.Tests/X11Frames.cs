using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Spar.Tests;

/// <summary>
/// The colour an app's window shows over time, read back from the test's own X server (Xvfb, no compositor, so a
/// window's contents are what the screen shows): one pixel near the window's bottom-right corner, inside the web view,
/// sampled as fast as the server answers from the moment the window is viewable (plan section 3d's "first frame").
/// </summary>
[SupportedOSPlatform("linux")]
internal static unsafe partial class X11Frames
{
    private const string Library = "libX11.so.6";
    private const int IsViewable = 2;
    private const int ZPixmap = 2;

    /// <summary>How far in from the right and bottom edges the pixel is.</summary>
    private const int Inset = 16;

    /// <summary>
    /// Samples the window of <paramref name="processId"/> on <paramref name="display"/> until <paramref name="stop"/>
    /// is set; each sample is the time since <paramref name="started"/> and the colour as <c>0xRRGGBB</c>.
    /// </summary>
    public static List<(double Milliseconds, uint Colour)> Sample(string display, int processId, Stopwatch started, CancellationToken stop)
    {
        X11Close.IgnoreErrors();
        var connection = X11Close.XOpenDisplay(display);
        if (connection == 0)
        {
            throw new InvalidOperationException($"The X display {display} cannot be opened.");
        }

        var samples = new List<(double, uint)>();
        try
        {
            var attributes = stackalloc byte[256];
            nuint window = 0;
            while (!stop.IsCancellationRequested)
            {
                if (window == 0)
                {
                    window = Largest(connection, processId, attributes);
                    if (window == 0)
                    {
                        Thread.Sleep(1);
                        continue;
                    }
                }

                if (XGetWindowAttributes(connection, window, attributes) == 0 || *(int*)(attributes + 92) != IsViewable)
                {
                    window = 0;
                    continue;
                }

                var width = *(int*)(attributes + 8);
                var height = *(int*)(attributes + 12);
                var image = XGetImage(connection, window, Math.Max(0, width - Inset), Math.Max(0, height - Inset), 1, 1, nuint.MaxValue, ZPixmap);
                if (image == 0)
                {
                    continue;
                }

                var pixel = (uint)XGetPixel(image, 0, 0) & 0xFFFFFF;
                XDestroyImage(image);
                samples.Add((started.Elapsed.TotalMilliseconds, pixel));
            }
        }
        finally
        {
            X11Close.XCloseDisplay(connection);
        }

        return samples;
    }

    /// <summary>Whether the process shows a window on <paramref name="display"/>: viewable, and larger than GTK's helpers.</summary>
    public static bool Shows(string display, int processId)
    {
        X11Close.IgnoreErrors();
        var connection = X11Close.XOpenDisplay(display);
        if (connection == 0)
        {
            throw new InvalidOperationException($"The X display {display} cannot be opened.");
        }

        try
        {
            var attributes = stackalloc byte[256];
            return Largest(connection, processId, attributes) != 0;
        }
        finally
        {
            X11Close.XCloseDisplay(connection);
        }
    }

    /// <summary>The largest viewable window of the process, or 0: GTK also makes small unmapped helper windows.</summary>
    private static nuint Largest(nint connection, int processId, byte* attributes)
    {
        nuint best = 0;
        var bestArea = 0L;
        foreach (var window in X11Close.WindowsOf(connection, processId))
        {
            if (XGetWindowAttributes(connection, window, attributes) == 0 || *(int*)(attributes + 92) != IsViewable)
            {
                continue;
            }

            var area = (long)*(int*)(attributes + 8) * *(int*)(attributes + 12);
            if (area > bestArea)
            {
                (best, bestArea) = (window, area);
            }
        }

        return bestArea > 2_500 ? best : 0;
    }

    /// <summary><c>XWindowAttributes</c> (136 bytes on x86-64): width at 8, height at 12, <c>map_state</c> at 92.</summary>
    [LibraryImport(Library)]
    private static partial int XGetWindowAttributes(nint display, nuint window, byte* attributes);

    [LibraryImport(Library)]
    private static partial nint XGetImage(nint display, nuint drawable, int x, int y, uint width, uint height, nuint planeMask, int format);

    [LibraryImport(Library)]
    private static partial nuint XGetPixel(nint image, int x, int y);

    [LibraryImport(Library)]
    private static partial int XDestroyImage(nint image);
}
