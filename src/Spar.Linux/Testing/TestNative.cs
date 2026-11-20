using System.Runtime.InteropServices;

namespace Spar.Linux.Testing;

/// <summary>
/// The platform calls only a test window uses (plan section 5, level 5), checked against WebKitGTK 6.0's
/// <c>webkit/WebKitWebView.h</c>, <c>WebKitScriptDialog.h</c>, <c>WebKitUserContent.h</c>, GTK 4.14's <c>gdk/gdktexture.h</c>,
/// <c>gdk/gdkmemorytexture.h</c>, <c>gtk/gtkwidget.h</c>, <c>gtk/gtknative.h</c>, GLib's <c>gvariant.h</c>, JavaScriptCore's
/// <c>jsc/JSCValue.h</c>, <c>X11/Xlib.h</c> and <c>X11/extensions/XTest.h</c>. On LP64, <c>Window</c>, <c>KeySym</c>,
/// <c>Time</c>, <c>gsize</c> and <c>unsigned long</c> are 64-bit (<see cref="nuint"/>), <c>gssize</c> is <see cref="nint"/>,
/// <c>KeyCode</c> is a byte, <c>Bool</c> and <c>gboolean</c> are <see cref="int"/>, and enums are <see cref="int"/>.
/// Reached only from the test host, behind the test-host feature switch.
/// </summary>
internal static unsafe partial class TestNative
{
    private const string WebKit = "libwebkitgtk-6.0.so.4";
    private const string Jsc = "libjavascriptcoregtk-6.0.so.1";
    private const string Gtk = "libgtk-4.so.1";
    private const string Glib = "libglib-2.0.so.0";
    private const string Xlib = "libX11.so.6";
    private const string Xtst = "libXtst.so.6";

    /// <summary><c>WEBKIT_SNAPSHOT_REGION_VISIBLE</c>: the viewport.</summary>
    public const int SnapshotVisible = 0;

    /// <summary><c>GDK_MEMORY_B8G8R8A8_PREMULTIPLIED</c>, what <c>gdk_texture_download</c> writes (cairo's ARGB32 on little-endian).</summary>
    public const int MemoryDefault = 0;

    // ---- WebKitGTK -----------------------------------------------------------------------------------------------

    /// <summary>A network session that keeps nothing on disk, owned.</summary>
    [LibraryImport(WebKit)]
    public static partial nint webkit_network_session_new_ephemeral();

    /// <summary>A web context of its own (its own scheme handlers and web processes), owned.</summary>
    [LibraryImport(WebKit)]
    public static partial nint webkit_web_context_new();

    [LibraryImport(WebKit)]
    public static partial nuint webkit_web_context_get_type();

    /// <summary>A user script for the page's own world, owned (its reference is dropped with <c>webkit_user_script_unref</c>).</summary>
    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint webkit_user_script_new(string source, int injectedFrames, int injectionTime, nint* allowList, nint* blockList);

    /// <summary>
    /// Calls <paramref name="body"/> as an async function's body with <paramref name="arguments"/> (a floating
    /// <c>a{sv}</c>, consumed) in <paramref name="worldName"/>, or the page's world when null; a promise is awaited.
    /// </summary>
    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_web_view_call_async_javascript_function(
        nint view, byte* body, nint length, nint arguments, string? worldName, nint sourceUri, nint cancellable,
        delegate* unmanaged<nint, nint, nint, void> callback, nint data);

    /// <summary>The value, owned, or 0 with an owned <c>GError</c>.</summary>
    [LibraryImport(WebKit)]
    public static partial nint webkit_web_view_call_async_javascript_function_finish(nint view, nint result, nint* error);

    [LibraryImport(WebKit)]
    public static partial void webkit_web_view_get_snapshot(
        nint view, int region, int options, nint cancellable, delegate* unmanaged<nint, nint, nint, void> callback, nint data);

    /// <summary>A <c>GdkTexture</c>, owned, or 0 with an owned <c>GError</c>.</summary>
    [LibraryImport(WebKit)]
    public static partial nint webkit_web_view_get_snapshot_finish(nint view, nint result, nint* error);

    /// <summary><c>WebKitScriptDialogType</c>: 0 alert, 1 confirm, 2 prompt, 3 before-unload confirm.</summary>
    [LibraryImport(WebKit)]
    public static partial int webkit_script_dialog_get_dialog_type(nint dialog);

    /// <summary>Borrowed.</summary>
    [LibraryImport(WebKit)]
    public static partial nint webkit_script_dialog_get_message(nint dialog);

    [LibraryImport(WebKit)]
    public static partial void webkit_script_dialog_confirm_set_confirmed(nint dialog, int confirmed);

    /// <summary>Borrowed.</summary>
    [LibraryImport(WebKit)]
    public static partial nint webkit_script_dialog_prompt_get_default_text(nint dialog);

    [LibraryImport(WebKit)]
    public static partial void webkit_script_dialog_prompt_set_text(nint dialog, nint text);

    /// <summary>The main frame's address, borrowed.</summary>
    [LibraryImport(WebKit)]
    public static partial nint webkit_web_view_get_uri(nint view);

    [LibraryImport(WebKit)]
    public static partial void webkit_web_view_reload(nint view);

    [LibraryImport(WebKit, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_web_view_load_uri(nint view, string uri);

    // ---- JavaScriptCore ------------------------------------------------------------------------------------------

    /// <summary>The value as JSON, owned; 0 for a value JSON cannot hold (<c>undefined</c>, a function).</summary>
    [LibraryImport(Jsc)]
    public static partial nint jsc_value_to_json(nint value, uint indent);

    [LibraryImport(Jsc)]
    public static partial int jsc_value_is_undefined(nint value);

    [LibraryImport(Jsc)]
    public static partial int jsc_value_is_string(nint value);

    /// <summary>A string value's text, owned.</summary>
    [LibraryImport(Jsc)]
    public static partial nint jsc_value_to_string(nint value);

    // ---- GLib ----------------------------------------------------------------------------------------------------

    [LibraryImport(Glib)]
    public static partial nint g_variant_dict_new(nint from);

    /// <summary>Consumes a floating <paramref name="value"/>.</summary>
    [LibraryImport(Glib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void g_variant_dict_insert_value(nint dictionary, string key, nint value);

    /// <summary>The dictionary's <c>a{sv}</c>, floating.</summary>
    [LibraryImport(Glib)]
    public static partial nint g_variant_dict_end(nint dictionary);

    [LibraryImport(Glib)]
    public static partial void g_variant_dict_unref(nint dictionary);

    /// <summary>A floating string variant.</summary>
    [LibraryImport(Glib)]
    public static partial nint g_variant_new_string(byte* text);

    /// <summary>Borrowed data of <paramref name="bytes"/>.</summary>
    [LibraryImport(Glib)]
    public static partial byte* g_bytes_get_data(nint bytes, nuint* size);

    /// <summary>Copies <paramref name="size"/> bytes.</summary>
    [LibraryImport(Glib)]
    public static partial nint g_bytes_new(void* data, nuint size);

    [LibraryImport(Glib)]
    public static partial void g_bytes_unref(nint bytes);

    // ---- GTK and GDK ---------------------------------------------------------------------------------------------

    [LibraryImport(Gtk)]
    public static partial int gdk_texture_get_width(nint texture);

    [LibraryImport(Gtk)]
    public static partial int gdk_texture_get_height(nint texture);

    /// <summary>The texture's pixels in <see cref="MemoryDefault"/>, rows of <paramref name="stride"/> bytes.</summary>
    [LibraryImport(Gtk)]
    public static partial void gdk_texture_download(nint texture, byte* data, nuint stride);

    /// <summary>The texture as a PNG, owned <c>GBytes</c>.</summary>
    [LibraryImport(Gtk)]
    public static partial nint gdk_texture_save_to_png_bytes(nint texture);

    /// <summary>A texture over <paramref name="bytes"/> (it takes its own reference), owned.</summary>
    [LibraryImport(Gtk)]
    public static partial nint gdk_memory_texture_new(int width, int height, int format, nint bytes, nuint stride);

    /// <summary>Where <paramref name="point"/> of <paramref name="widget"/> is in <paramref name="target"/>'s coordinates (GTK 4.10).</summary>
    [LibraryImport(Gtk)]
    public static partial int gtk_widget_compute_point(nint widget, nint target, GraphenePoint* point, GraphenePoint* result);

    /// <summary>Where the window's widget coordinates start in its surface (its shadow and decorations).</summary>
    [LibraryImport(Gtk)]
    public static partial void gtk_native_get_surface_transform(nint native, double* x, double* y);

    // ---- Xlib and XTEST ------------------------------------------------------------------------------------------

    [LibraryImport(Xlib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint XOpenDisplay(string? name);

    [LibraryImport(Xlib)]
    public static partial int XCloseDisplay(nint display);

    [LibraryImport(Xlib)]
    public static partial int XFlush(nint display);

    [LibraryImport(Xlib)]
    public static partial int XSync(nint display, int discard);

    [LibraryImport(Xlib)]
    public static partial nuint XDefaultRootWindow(nint display);

    [LibraryImport(Xlib)]
    public static partial byte XKeysymToKeycode(nint display, nuint keysym);

    [LibraryImport(Xlib)]
    public static partial int XDisplayKeycodes(nint display, int* minimum, int* maximum);

    /// <summary>The keysyms of <paramref name="count"/> keycodes from <paramref name="first"/>, freed with <see cref="XFree"/>.</summary>
    [LibraryImport(Xlib)]
    public static partial nuint* XGetKeyboardMapping(nint display, byte first, int count, int* keysymsPerKeycode);

    [LibraryImport(Xlib)]
    public static partial int XChangeKeyboardMapping(nint display, int firstKeycode, int keysymsPerKeycode, nuint* keysyms, int count);

    [LibraryImport(Xlib)]
    public static partial int XFree(void* data);

    /// <summary><paramref name="revertTo"/> 2 is <c>RevertToParent</c>; <paramref name="time"/> 0 is <c>CurrentTime</c>.</summary>
    [LibraryImport(Xlib)]
    public static partial int XSetInputFocus(nint display, nuint window, int revertTo, nuint time);

    [LibraryImport(Xlib)]
    public static partial int XRaiseWindow(nint display, nuint window);

    [LibraryImport(Xlib)]
    public static partial int XMoveWindow(nint display, nuint window, int x, int y);

    [LibraryImport(Xlib)]
    public static partial int XTranslateCoordinates(
        nint display, nuint source, nuint destination, int sourceX, int sourceY, int* destinationX, int* destinationY, nuint* child);

    [LibraryImport(Xtst)]
    public static partial int XTestQueryExtension(nint display, int* eventBase, int* errorBase, int* major, int* minor);

    [LibraryImport(Xtst)]
    public static partial int XTestFakeMotionEvent(nint display, int screen, int x, int y, nuint delay);

    [LibraryImport(Xtst)]
    public static partial int XTestFakeButtonEvent(nint display, uint button, int isPress, nuint delay);

    [LibraryImport(Xtst)]
    public static partial int XTestFakeKeyEvent(nint display, uint keycode, int isPress, nuint delay);
}

/// <summary>A <c>graphene_point_t</c>: two floats.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GraphenePoint
{
    public float X;
    public float Y;
}
