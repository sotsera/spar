using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Spar.Testing.BlackBox.Linux;

/// <summary>
/// The part of GLib, GObject and GIO the black box's accessibility client uses: GDBus connections and synchronous calls,
/// and <c>GVariant</c> values (checked against the 2.80 headers of Ubuntu 24.04). No variadic function is used: values
/// are built with <c>g_variant_new_*</c> and <c>g_variant_new_tuple</c>. <c>gboolean</c> is 32-bit, <c>gsize</c>
/// pointer-sized (LP64). Ownership: a connection is owned (<see cref="g_object_unref"/>); a call's reply is owned
/// (<see cref="g_variant_unref"/>); a value built here is floating, and the call that takes it sinks it.
/// </summary>
[SupportedOSPlatform("linux")]
internal static unsafe partial class Gio
{
    /// <summary><c>G_DBUS_CONNECTION_FLAGS_AUTHENTICATION_CLIENT | G_DBUS_CONNECTION_FLAGS_MESSAGE_BUS_CONNECTION</c>.</summary>
    public const int ClientOfMessageBus = 1 | 8;

    /// <summary><c>G_DBUS_CALL_FLAGS_NO_AUTO_START</c>: a name that is not running is an error at once, never started.</summary>
    public const int NoAutoStart = 1;

    private const string Glib = "libglib-2.0.so.0";
    private const string Gobject = "libgobject-2.0.so.0";
    private const string Library = "libgio-2.0.so.0";

    /// <summary>A connection to <paramref name="address"/>, authenticated and registered on its bus; owned.</summary>
    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint g_dbus_connection_new_for_address_sync(string address, int flags, nint observer, nint cancellable, nint* error);

    [LibraryImport(Library)]
    public static partial int g_dbus_connection_close_sync(nint connection, nint cancellable, nint* error);

    /// <summary>A method call and its reply tuple (owned); <paramref name="parameters"/>, floating, is consumed.</summary>
    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint g_dbus_connection_call_sync(
        nint connection,
        string? busName,
        string objectPath,
        string interfaceName,
        string methodName,
        nint parameters,
        nint replyType,
        int flags,
        int timeoutMilliseconds,
        nint cancellable,
        nint* error);

    /// <summary>The D-Bus error name of a remote error (owned string), or null for a local one.</summary>
    [LibraryImport(Library)]
    public static partial nint g_dbus_error_get_remote_error(nint error);

    [LibraryImport(Gobject)]
    public static partial void g_object_unref(nint instance);

    [LibraryImport(Glib)]
    public static partial void g_error_free(nint error);

    [LibraryImport(Glib)]
    public static partial void g_free(nint memory);

    [LibraryImport(Glib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint g_variant_new_string(string value);

    [LibraryImport(Glib)]
    public static partial nint g_variant_new_int32(int value);

    [LibraryImport(Glib)]
    public static partial nint g_variant_new_uint32(uint value);

    [LibraryImport(Glib)]
    public static partial nint g_variant_new_tuple(nint* children, nuint count);

    [LibraryImport(Glib)]
    public static partial void g_variant_unref(nint value);

    /// <summary>The value's type string, owned by the value.</summary>
    [LibraryImport(Glib)]
    public static partial nint g_variant_get_type_string(nint value);

    [LibraryImport(Glib)]
    public static partial nuint g_variant_n_children(nint value);

    /// <summary>A child of a container (owned).</summary>
    [LibraryImport(Glib)]
    public static partial nint g_variant_get_child_value(nint value, nuint index);

    /// <summary>The string of an <c>s</c>, <c>o</c> or <c>g</c> value, owned by the value.</summary>
    [LibraryImport(Glib)]
    public static partial nint g_variant_get_string(nint value, nuint* length);

    /// <summary>The value inside a <c>v</c> (owned).</summary>
    [LibraryImport(Glib)]
    public static partial nint g_variant_get_variant(nint value);

    [LibraryImport(Glib)]
    public static partial int g_variant_get_boolean(nint value);

    [LibraryImport(Glib)]
    public static partial byte g_variant_get_byte(nint value);

    [LibraryImport(Glib)]
    public static partial short g_variant_get_int16(nint value);

    [LibraryImport(Glib)]
    public static partial ushort g_variant_get_uint16(nint value);

    [LibraryImport(Glib)]
    public static partial int g_variant_get_int32(nint value);

    [LibraryImport(Glib)]
    public static partial uint g_variant_get_uint32(nint value);

    [LibraryImport(Glib)]
    public static partial long g_variant_get_int64(nint value);

    [LibraryImport(Glib)]
    public static partial ulong g_variant_get_uint64(nint value);

    [LibraryImport(Glib)]
    public static partial double g_variant_get_double(nint value);
}
