using System.Runtime.InteropServices;
using System.Text;

namespace Spike.BlackBoxAtSpi.Native;

/// <summary>
/// Xlib on the run's own Xvfb: the top-level windows of a process (by <c>_NET_WM_PID</c>), their title
/// (<c>_NET_WM_NAME</c>) and whether they are viewable, and the window manager's close (<c>WM_DELETE_WINDOW</c>).
/// </summary>
internal static unsafe partial class X11
{
    private const string Library = "libX11.so.6";
    private const int ClientMessage = 33;
    private const int IsViewable = 2;

    public static nint Open(string display)
    {
        XSetErrorHandler(&IgnoreError);
        var connection = XOpenDisplay(display);
        return connection != 0 ? connection : throw new InvalidOperationException($"The X display {display} cannot be opened.");
    }

    public static void CloseDisplay(nint connection) => XCloseDisplay(connection);

    /// <summary>The windows of <paramref name="processId"/>, each with its title and whether it is viewable.</summary>
    public static List<(nuint Window, string Title, bool Viewable)> WindowsOf(nint connection, int processId)
    {
        var pidAtom = XInternAtom(connection, "_NET_WM_PID", 1);
        var nameAtom = XInternAtom(connection, "_NET_WM_NAME", 0);
        var utf8 = XInternAtom(connection, "UTF8_STRING", 0);
        var found = new List<(nuint, string, bool)>();
        if (pidAtom == 0)
        {
            return found;
        }

        foreach (var window in All(connection, XDefaultRootWindow(connection)))
        {
            if (Cardinal(connection, window, pidAtom) != processId)
            {
                continue;
            }

            found.Add((window, Text(connection, window, nameAtom, utf8) ?? "", Viewable(connection, window)));
        }

        return found;
    }

    /// <summary>Sends <c>WM_DELETE_WINDOW</c> to <paramref name="window"/>, as a window manager's close button does.</summary>
    public static void SendDelete(nint connection, nuint window)
    {
        var message = new XClientMessageEvent
        {
            Type = ClientMessage,
            Window = window,
            MessageType = XInternAtom(connection, "WM_PROTOCOLS", 0),
            Format = 32,
            Data0 = (long)XInternAtom(connection, "WM_DELETE_WINDOW", 0),
        };
        XSendEvent(connection, window, 0, 0, &message);
        XFlush(connection);
    }

    /// <summary>The window's origin on the root window (no window manager on the Xvfb: no frame).</summary>
    public static (int X, int Y) Origin(nint connection, nuint window)
    {
        XTranslateCoordinates(connection, window, XDefaultRootWindow(connection), 0, 0, out var x, out var y, out _);
        return (x, y);
    }

    /// <summary>A left click at (<paramref name="x"/>, <paramref name="y"/>) on the root window through XTEST.</summary>
    public static void Click(nint connection, int x, int y)
    {
        XTestFakeMotionEvent(connection, -1, x, y, 0);
        XFlush(connection);
        Thread.Sleep(20);
        XTestFakeButtonEvent(connection, 1, 1, 0);
        XFlush(connection);
        Thread.Sleep(20);
        XTestFakeButtonEvent(connection, 1, 0, 0);
        XFlush(connection);
    }

    private static bool Viewable(nint connection, nuint window)
    {
        // XWindowAttributes is 136 bytes on x86-64; map_state is the int at offset 92.
        var attributes = stackalloc byte[256];
        return XGetWindowAttributes(connection, window, attributes) != 0 && *(int*)(attributes + 92) == IsViewable;
    }

    private static List<nuint> All(nint connection, nuint root)
    {
        var all = new List<nuint>();
        var pending = new Stack<nuint>();
        pending.Push(root);
        while (pending.TryPop(out var window))
        {
            if (XQueryTree(connection, window, out _, out _, out var children, out var count) == 0)
            {
                continue;
            }

            for (var i = 0; i < count; i++)
            {
                all.Add(children[i]);
                pending.Push(children[i]);
            }

            if (children != null)
            {
                XFree(children);
            }
        }

        return all;
    }

    private static long Cardinal(nint connection, nuint window, nuint atom)
    {
        const nuint CardinalType = 6;
        var status = XGetWindowProperty(connection, window, atom, 0, 1, 0, CardinalType, out _, out var format, out var items, out _, out var value);
        try
        {
            return status == 0 && format == 32 && items == 1 && value != null ? *(long*)value : -1;
        }
        finally
        {
            if (value != null)
            {
                XFree(value);
            }
        }
    }

    private static string? Text(nint connection, nuint window, nuint atom, nuint type)
    {
        var status = XGetWindowProperty(connection, window, atom, 0, 1024, 0, type, out _, out var format, out var items, out _, out var value);
        try
        {
            return status == 0 && format == 8 && value != null ? Encoding.UTF8.GetString((byte*)value, (int)items) : null;
        }
        finally
        {
            if (value != null)
            {
                XFree(value);
            }
        }
    }

    [UnmanagedCallersOnly]
    private static int IgnoreError(nint display, nint error) => 0;

    [LibraryImport(Library)]
    private static partial nint XSetErrorHandler(delegate* unmanaged<nint, nint, int> handler);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint XOpenDisplay(string name);

    [LibraryImport(Library)]
    private static partial int XCloseDisplay(nint display);

    [LibraryImport(Library)]
    private static partial nuint XDefaultRootWindow(nint display);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nuint XInternAtom(nint display, string name, int onlyIfExists);

    [LibraryImport(Library)]
    private static partial int XQueryTree(nint display, nuint window, out nuint root, out nuint parent, out nuint* children, out uint count);

    [LibraryImport(Library)]
    private static partial int XGetWindowAttributes(nint display, nuint window, byte* attributes);

    [LibraryImport(Library)]
    private static partial int XGetWindowProperty(
        nint display, nuint window, nuint property, nint offset, nint length, int delete, nuint requestedType,
        out nuint actualType, out int actualFormat, out nuint items, out nuint bytesAfter, out void* value);

    [LibraryImport(Library)]
    private static partial int XSendEvent(nint display, nuint window, int propagate, nint eventMask, XClientMessageEvent* message);

    [LibraryImport(Library)]
    private static partial int XFlush(nint display);

    [LibraryImport(Library)]
    private static partial int XTranslateCoordinates(nint display, nuint source, nuint destination, int x, int y, out int destinationX, out int destinationY, out nuint child);

    [LibraryImport("libXtst.so.6")]
    private static partial int XTestFakeMotionEvent(nint display, int screen, int x, int y, nuint delay);

    [LibraryImport("libXtst.so.6")]
    private static partial int XTestFakeButtonEvent(nint display, uint button, int isPress, nuint delay);

    [LibraryImport(Library)]
    private static partial int XFree(void* data);

    /// <summary>An <c>XClientMessageEvent</c> padded to the 192 bytes of an <c>XEvent</c> (x86-64).</summary>
    [StructLayout(LayoutKind.Explicit, Size = 192)]
    private struct XClientMessageEvent
    {
        [FieldOffset(0)]
        public int Type;

        [FieldOffset(32)]
        public nuint Window;

        [FieldOffset(40)]
        public nuint MessageType;

        [FieldOffset(48)]
        public int Format;

        [FieldOffset(56)]
        public long Data0;
    }
}
