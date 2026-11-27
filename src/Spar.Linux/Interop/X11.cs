using System.Runtime.InteropServices;

namespace Spar.Linux.Interop;

/// <summary>
/// GDK's X11 backend and the two Xlib calls the theme needs (checked against GTK 4.14's <c>gdk/x11</c> headers and
/// <c>X11/Xlib.h</c>; <c>Window</c> and pixels are <c>unsigned long</c>): on X11 without a compositor a newly mapped
/// window shows whatever was on the screen under it until GTK first paints, because GTK gives its windows no
/// background; a background pixel makes the X server fill it with the theme's colour instead. Used only when the
/// window's surface is an X11 one; a GTK built without X11 lacks these symbols, which the caller catches.
/// </summary>
internal static unsafe partial class X11
{
    private const string Gtk = "libgtk-4.so.1";
    private const string Gobject = "libgobject-2.0.so.0";
    private const string Xlib = "libX11.so.6";

    /// <summary>Creates the widget's native resources (for a window, its surface) without showing it.</summary>
    [LibraryImport(Gtk)]
    public static partial void gtk_widget_realize(nint widget);

    /// <summary>The window's surface, borrowed; 0 before it is realized.</summary>
    [LibraryImport(Gtk)]
    public static partial nint gtk_native_get_surface(nint native);

    /// <summary>Borrowed.</summary>
    [LibraryImport(Gtk)]
    public static partial nint gdk_surface_get_display(nint surface);

    [LibraryImport(Gtk)]
    public static partial nuint gdk_x11_surface_get_type();

    [LibraryImport(Gtk)]
    public static partial nuint gdk_x11_surface_get_xid(nint surface);

    /// <summary>The Xlib display behind a GDK X11 display, borrowed.</summary>
    [LibraryImport(Gtk)]
    public static partial nint gdk_x11_display_get_xdisplay(nint display);

    [LibraryImport(Gobject)]
    public static partial int g_type_check_instance_is_a(nint instance, nuint type);

    [LibraryImport(Xlib)]
    public static partial int XSetWindowBackground(nint display, nuint window, nuint pixel);

    /// <summary><c>XWindowAttributes</c>, 136 bytes on LP64: <c>depth</c> is the int at offset 20.</summary>
    [LibraryImport(Xlib)]
    public static partial int XGetWindowAttributes(nint display, nuint window, byte* attributes);

    /// <summary>Moves the window's top-left corner to (<paramref name="x"/>, <paramref name="y"/>) of its parent (the root, for a toplevel).</summary>
    [LibraryImport(Xlib)]
    public static partial int XMoveWindow(nint display, nuint window, int x, int y);

    /// <summary>Where (<paramref name="x"/>, <paramref name="y"/>) of <paramref name="source"/> is in <paramref name="destination"/>; 0 when they are on different screens.</summary>
    [LibraryImport(Xlib)]
    public static partial int XTranslateCoordinates(nint display, nuint source, nuint destination, int x, int y, out int destinationX, out int destinationY, out nuint child);

    [LibraryImport(Xlib)]
    public static partial nuint XDefaultRootWindow(nint display);

    [LibraryImport(Xlib)]
    public static partial int XFlush(nint display);
}
