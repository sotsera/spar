using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Spar.Tests;

/// <summary>
/// What a window manager does when its close button is pressed, without one: a <c>WM_DELETE_WINDOW</c> client message
/// to the toplevel X11 window of a process (found by <c>_NET_WM_PID</c>), which GTK turns into the window's
/// <c>close-request</c>. Used on the test's own Xvfb only.
/// </summary>
[SupportedOSPlatform("linux")]
internal static unsafe partial class X11Close
{
    private const string Library = "libX11.so.6";
    private const int ClientMessage = 33;

    /// <summary>Sends the close to every window of <paramref name="processId"/> on <paramref name="display"/>; returns how many got it.</summary>
    public static int Send(string display, int processId)
    {
        var connection = XOpenDisplay(display);
        if (connection == 0)
        {
            throw new InvalidOperationException($"The X display {display} cannot be opened.");
        }

        try
        {
            var pid = XInternAtom(connection, "_NET_WM_PID", 1);
            var protocols = XInternAtom(connection, "WM_PROTOCOLS", 0);
            var delete = XInternAtom(connection, "WM_DELETE_WINDOW", 0);
            var sent = 0;
            foreach (var window in Windows(connection, XDefaultRootWindow(connection)))
            {
                if (pid == 0 || PidOf(connection, window, pid) != processId)
                {
                    continue;
                }

                var message = new XClientMessageEvent
                {
                    Type = ClientMessage,
                    Window = window,
                    MessageType = protocols,
                    Format = 32,
                    Data0 = (long)delete,
                    Data1 = 0,
                };
                XSendEvent(connection, window, 0, 0, &message);
                sent++;
            }

            XFlush(connection);
            return sent;
        }
        finally
        {
            XCloseDisplay(connection);
        }
    }

    private static List<nuint> Windows(nint connection, nuint root)
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

    private static long PidOf(nint connection, nuint window, nuint pidAtom)
    {
        const nuint Cardinal = 6;
        var status = XGetWindowProperty(
            connection, window, pidAtom, 0, 1, 0, Cardinal, out _, out var format, out var items, out _, out var value);
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
    private static partial int XGetWindowProperty(
        nint display, nuint window, nuint property, nint offset, nint length, int delete, nuint requestedType,
        out nuint actualType, out int actualFormat, out nuint items, out nuint bytesAfter, out void* value);

    [LibraryImport(Library)]
    private static partial int XSendEvent(nint display, nuint window, int propagate, nint eventMask, XClientMessageEvent* message);

    [LibraryImport(Library)]
    private static partial int XFlush(nint display);

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

        [FieldOffset(64)]
        public long Data1;
    }
}
