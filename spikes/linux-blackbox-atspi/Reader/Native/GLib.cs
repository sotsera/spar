using System.Runtime.InteropServices;

namespace Spike.BlackBoxAtSpi.Native;

/// <summary>
/// GLib, GObject and GIO's GDBus, as the reader uses them (Ubuntu 24.04's 2.80 headers). <c>gboolean</c>, <c>gint</c>
/// and <c>guint</c> are 32-bit, <c>gsize</c> pointer-sized. GVariant constructors return floating references, which a
/// call consumes; a call's reply is owned (<see cref="g_variant_unref"/>).
/// </summary>
internal static unsafe partial class GLib
{
    public const int BusSession = 2;

    /// <summary><c>G_DBUS_CONNECTION_FLAGS_AUTHENTICATION_CLIENT | G_DBUS_CONNECTION_FLAGS_MESSAGE_BUS_CONNECTION</c>.</summary>
    public const int ClientOfMessageBus = 1 | 8;

    private const string Glib = "libglib-2.0.so.0";
    private const string Gobject = "libgobject-2.0.so.0";
    private const string Gio = "libgio-2.0.so.0";

    [LibraryImport(Glib)]
    public static partial void g_free(nint memory);

    [LibraryImport(Glib)]
    public static partial void g_error_free(nint error);

    [LibraryImport(Gobject)]
    public static partial void g_object_unref(nint instance);

    [LibraryImport(Glib)]
    public static partial nint g_main_context_new();

    [LibraryImport(Glib)]
    public static partial void g_main_context_push_thread_default(nint context);

    [LibraryImport(Glib)]
    public static partial nint g_main_loop_new(nint context, int isRunning);

    [LibraryImport(Glib)]
    public static partial void g_main_loop_run(nint loop);

    [LibraryImport(Glib)]
    public static partial void g_main_loop_quit(nint loop);

    /// <summary>Owned: <see cref="g_object_unref"/>.</summary>
    [LibraryImport(Gio)]
    public static partial nint g_bus_get_sync(int busType, nint cancellable, nint* error);

    [LibraryImport(Gio, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint g_dbus_connection_new_for_address_sync(string address, int flags, nint observer, nint cancellable, nint* error);

    /// <summary>Borrowed.</summary>
    [LibraryImport(Gio)]
    public static partial nint g_dbus_connection_get_unique_name(nint connection);

    /// <summary>Takes a floating <paramref name="parameters"/>; the reply is owned.</summary>
    [LibraryImport(Gio, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint g_dbus_connection_call_sync(
        nint connection, string? busName, string objectPath, string interfaceName, string methodName, nint parameters,
        nint replyType, int flags, int timeoutMilliseconds, nint cancellable, nint* error);

    [LibraryImport(Gio, StringMarshalling = StringMarshalling.Utf8)]
    public static partial uint g_dbus_connection_signal_subscribe(
        nint connection, string? sender, string? interfaceName, string? member, string? objectPath, string? arg0, int flags,
        delegate* unmanaged<nint, nint, nint, nint, nint, nint, nint, void> callback, nint userData, nint userDataFree);

    [LibraryImport(Glib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint g_variant_new_string(string value);

    [LibraryImport(Glib)]
    public static partial nint g_variant_new_int32(int value);

    [LibraryImport(Glib)]
    public static partial nint g_variant_new_uint32(uint value);

    [LibraryImport(Glib)]
    public static partial nint g_variant_new_tuple(nint* children, nuint count);

    [LibraryImport(Glib)]
    public static partial nint g_variant_new_strv(nint* strings, nint length);

    [LibraryImport(Glib)]
    public static partial void g_variant_unref(nint value);

    /// <summary>Borrowed, NUL-terminated.</summary>
    [LibraryImport(Glib)]
    public static partial nint g_variant_get_type_string(nint value);

    [LibraryImport(Glib)]
    public static partial nuint g_variant_n_children(nint value);

    /// <summary>Owned.</summary>
    [LibraryImport(Glib)]
    public static partial nint g_variant_get_child_value(nint value, nuint index);

    /// <summary>Owned.</summary>
    [LibraryImport(Glib)]
    public static partial nint g_variant_get_variant(nint value);

    /// <summary>Borrowed; valid while <paramref name="value"/> lives.</summary>
    [LibraryImport(Glib)]
    public static partial nint g_variant_get_string(nint value, nuint* length);

    [LibraryImport(Glib)]
    public static partial uint g_variant_get_uint32(nint value);

    [LibraryImport(Glib)]
    public static partial int g_variant_get_int32(nint value);

    [LibraryImport(Glib)]
    public static partial long g_variant_get_int64(nint value);

    [LibraryImport(Glib)]
    public static partial ulong g_variant_get_uint64(nint value);

    [LibraryImport(Glib)]
    public static partial int g_variant_get_boolean(nint value);

    [LibraryImport(Glib)]
    public static partial byte g_variant_get_byte(nint value);

    [LibraryImport(Glib)]
    public static partial double g_variant_get_double(nint value);

    /// <summary>A <c>GError</c>'s message, then the error freed; null when none was set.</summary>
    public static string? TakeError(nint error)
    {
        if (error == 0)
        {
            return null;
        }

        // struct GError { GQuark domain; gint code; gchar *message; }
        var message = Marshal.PtrToStringUTF8(*(nint*)(error + 8));
        g_error_free(error);
        return message;
    }
}
