using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Spar.WindowTests;

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

    /// <summary>The process's largest viewable window on <paramref name="display"/> (its main window), or 0.</summary>
    public static nuint MainWindow(string display, int processId)
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
            return Largest(connection, processId, attributes);
        }
        finally
        {
            X11Close.XCloseDisplay(connection);
        }
    }

    /// <summary>
    /// How many windows the process shows on <paramref name="display"/>, viewable and larger than GTK's helpers: the
    /// app's window, and one more while a dialog of its own is open.
    /// </summary>
    public static int Count(string display, int processId)
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
            var count = 0;
            foreach (var window in X11Close.WindowsOf(connection, processId))
            {
                if (XGetWindowAttributes(connection, window, attributes) != 0
                    && *(int*)(attributes + 92) == IsViewable
                    && (long)*(int*)(attributes + 8) * *(int*)(attributes + 12) > 2_500)
                {
                    count++;
                }
            }

            return count;
        }
        finally
        {
            X11Close.XCloseDisplay(connection);
        }
    }

    /// <summary>The titles (<c>WM_NAME</c>) of the process's viewable windows of a real size, as <see cref="Count"/> counts them.</summary>
    public static List<string> Titles(string display, int processId)
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
            var titles = new List<string>();
            foreach (var window in X11Close.WindowsOf(connection, processId))
            {
                if (XGetWindowAttributes(connection, window, attributes) == 0
                    || *(int*)(attributes + 92) != IsViewable
                    || (long)*(int*)(attributes + 8) * *(int*)(attributes + 12) <= 2_500)
                {
                    continue;
                }

                nint name = 0;
                titles.Add(XFetchName(connection, window, &name) != 0 && name != 0 ? Marshal.PtrToStringUTF8(name) ?? "" : "");
                if (name != 0)
                {
                    XFree(name);
                }
            }

            return titles;
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

    /// <summary>The window's <c>WM_NAME</c>, owned: <see cref="XFree"/>.</summary>
    [LibraryImport(Library)]
    private static partial int XFetchName(nint display, nuint window, nint* name);

    [LibraryImport(Library)]
    private static partial int XFree(nint data);
}

/// <summary>
/// Real input on the tests' private Xvfb through XTEST: a click of any button in the middle of a window, and key
/// combinations by their keysym names (<c>Control_L+Shift_L+i</c>). Never on a display that is not the run's own.
/// </summary>
[SupportedOSPlatform("linux")]
internal static unsafe partial class XTest
{
    private const string Library = "libX11.so.6";

    /// <summary>
    /// Presses and releases <paramref name="button"/> (1 left, 3 right, 8 and 9 the side buttons) in the middle of
    /// <paramref name="window"/>, moved by <paramref name="dx"/> and <paramref name="dy"/> pixels.
    /// </summary>
    public static void ClickMiddle(string display, nuint window, uint button = 1, int dx = 0, int dy = 0)
    {
        Assert.NotEqual(0u, window);
        var connection = XOpenDisplay(display);
        Assert.NotEqual(0, connection);
        try
        {
            var attributes = stackalloc byte[256];
            Assert.NotEqual(0, XGetWindowAttributes(connection, window, attributes));
            int x, y;
            nuint child;
            XTranslateCoordinates(connection, window, XDefaultRootWindow(connection), 0, 0, &x, &y, &child);
            var width = *(int*)(attributes + 8);
            var height = *(int*)(attributes + 12);
            XTestFakeMotionEvent(connection, -1, x + (width / 2) + dx, y + (height / 2) + dy, 0);
            XTestFakeButtonEvent(connection, button, 1, 30);
            XTestFakeButtonEvent(connection, button, 0, 30);
            XFlush(connection);
            XSync(connection, 0);
        }
        finally
        {
            XCloseDisplay(connection);
        }
    }

    /// <summary>Presses each key of <paramref name="combination"/> in order (keysym names joined by <c>+</c>), then releases them in reverse.</summary>
    public static void Press(string display, string combination)
    {
        var connection = XOpenDisplay(display);
        Assert.NotEqual(0, connection);
        try
        {
            var codes = combination.Split('+').Select(name =>
            {
                var code = XKeysymToKeycode(connection, XStringToKeysym(name));
                Assert.True(code != 0, $"No key for '{name}' on {display}.");
                return (uint)code;
            }).ToList();
            foreach (var code in codes)
            {
                XTestFakeKeyEvent(connection, code, 1, 20);
            }

            for (var index = codes.Count - 1; index >= 0; index--)
            {
                XTestFakeKeyEvent(connection, codes[index], 0, 20);
            }

            XFlush(connection);
            XSync(connection, 0);
        }
        finally
        {
            XCloseDisplay(connection);
        }
    }

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint XOpenDisplay(string name);

    [LibraryImport(Library)]
    private static partial int XCloseDisplay(nint display);

    [LibraryImport(Library)]
    private static partial nuint XDefaultRootWindow(nint display);

    [LibraryImport(Library)]
    private static partial int XGetWindowAttributes(nint display, nuint window, byte* attributes);

    [LibraryImport(Library)]
    private static partial int XTranslateCoordinates(nint display, nuint source, nuint destination, int x, int y, int* toX, int* toY, nuint* child);

    [LibraryImport(Library)]
    private static partial int XFlush(nint display);

    [LibraryImport(Library)]
    private static partial int XSync(nint display, int discard);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nuint XStringToKeysym(string name);

    [LibraryImport(Library)]
    private static partial byte XKeysymToKeycode(nint display, nuint keysym);

    [LibraryImport("libXtst.so.6")]
    private static partial int XTestFakeMotionEvent(nint display, int screen, int x, int y, nuint delay);

    [LibraryImport("libXtst.so.6")]
    private static partial int XTestFakeButtonEvent(nint display, uint button, int isPress, nuint delay);

    [LibraryImport("libXtst.so.6")]
    private static partial int XTestFakeKeyEvent(nint display, uint keycode, int isPress, nuint delay);
}
