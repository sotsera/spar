using System.Runtime.InteropServices;
using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("linux")]

namespace Startup.Linux;

// Imports for libc, GLib, GObject, GIO, GTK 4 and WebKitGTK 6.0 (LP64 Linux x64). gboolean = int, guint = uint,
// GType = nuint, pointers = nint. Signatures follow the GTK 4.14 / GLib 2.80 / WebKitGTK 2.52 headers of Ubuntu 24.04.
// No variadic function is imported (gtk_alert_dialog_new and g_variant_new are variadic; their non-variadic
// counterparts are used). Each group is called only after its library loaded in Checks.
internal static unsafe partial class LibC
{
    public const int RtldNow = 2;

    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint dlopen(string file, int mode);

    [LibraryImport("libc")]
    public static partial nint dlerror();

    [LibraryImport("libc")]
    public static partial uint getuid();

    [LibraryImport("libc")]
    public static partial int getpid();
}

internal static unsafe partial class GLib
{
    public const string Lib = "libglib-2.0.so.0";

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void g_set_prgname(string name);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void g_set_application_name(string name);

    [LibraryImport(Lib)]
    public static partial nint g_main_loop_new(nint context, int running);

    [LibraryImport(Lib)]
    public static partial void g_main_loop_run(nint loop);

    [LibraryImport(Lib)]
    public static partial void g_main_loop_quit(nint loop);

    [LibraryImport(Lib)]
    public static partial uint g_timeout_add(uint interval, delegate* unmanaged<nint, int> function, nint data);

    [LibraryImport(Lib)]
    public static partial void g_error_free(nint error);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint g_variant_new_string(string value);

    [LibraryImport(Lib)]
    public static partial nint g_variant_new_uint32(uint value);

    [LibraryImport(Lib)]
    public static partial nint g_variant_new_int32(int value);

    [LibraryImport(Lib)]
    public static partial nint g_variant_new_byte(byte value);

    [LibraryImport(Lib)]
    public static partial nint g_variant_new_strv(nint strv, nint length);

    [LibraryImport(Lib)]
    public static partial nint g_variant_new_variant(nint value);

    [LibraryImport(Lib)]
    public static partial nint g_variant_new_dict_entry(nint key, nint value);

    [LibraryImport(Lib)]
    public static partial nint g_variant_new_array(nint childType, nint* children, nuint count);

    [LibraryImport(Lib)]
    public static partial nint g_variant_new_tuple(nint* children, nuint count);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint g_variant_type_new(string type);

    [LibraryImport(Lib)]
    public static partial void g_variant_type_free(nint type);

    [LibraryImport(Lib)]
    public static partial nint g_variant_get_child_value(nint value, nuint index);

    [LibraryImport(Lib)]
    public static partial uint g_variant_get_uint32(nint value);

    [LibraryImport(Lib)]
    public static partial void g_variant_unref(nint value);

    // GError { GQuark domain; gint code; gchar *message; }: message at offset 8.
    public static string Message(nint error) => error == 0 ? "" : Marshal.PtrToStringUTF8(*(nint*)((byte*)error + 8)) ?? "";
}

internal static unsafe partial class GObject
{
    public const string Lib = "libgobject-2.0.so.0";
    public const nuint TypeBoolean = 5 << 2, TypeInt = 6 << 2, TypeString = 16 << 2;

    // GValue: GType plus a two-element 8-byte union, 24 bytes; zeroed before g_value_init.
    [StructLayout(LayoutKind.Sequential, Size = 24)]
    public struct GValue
    {
        public nuint Type;
        public long Data0;
        public long Data1;
    }

    [LibraryImport(Lib)]
    public static partial nint g_value_init(GValue* value, nuint type);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void g_value_set_string(GValue* value, string text);

    [LibraryImport(Lib)]
    public static partial void g_value_set_boxed(GValue* value, nint boxed);

    [LibraryImport(Lib)]
    public static partial void g_value_set_int(GValue* value, int number);

    [LibraryImport(Lib)]
    public static partial void g_value_set_boolean(GValue* value, int flag);

    [LibraryImport(Lib)]
    public static partial void g_value_unset(GValue* value);

    [LibraryImport(Lib)]
    public static partial nuint g_strv_get_type();

    [LibraryImport(Lib)]
    public static partial nint g_object_new_with_properties(nuint type, uint count, byte** names, GValue* values);

    [LibraryImport(Lib)]
    public static partial void g_object_unref(nint obj);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nuint g_signal_connect_data(nint instance, string signal, nint handler, nint data, nint destroy, int flags);
}

internal static unsafe partial class Gio
{
    public const string Lib = "libgio-2.0.so.0";

    [LibraryImport(Lib)]
    public static partial nint g_cancellable_new();

    [LibraryImport(Lib)]
    public static partial void g_cancellable_cancel(nint cancellable);

    [LibraryImport(Lib)]
    public static partial nint g_bus_get_sync(int busType, nint cancellable, nint* error);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint g_dbus_connection_call_sync(nint connection, string busName, string objectPath, string interfaceName,
        string method, nint parameters, nint replyType, int flags, int timeoutMs, nint cancellable, nint* error);
}

internal static unsafe partial class Gtk
{
    public const string Lib = "libgtk-4.so.1";

    [LibraryImport(Lib)]
    public static partial int gtk_init_check();

    [LibraryImport(Lib)]
    public static partial uint gtk_get_major_version();

    [LibraryImport(Lib)]
    public static partial uint gtk_get_minor_version();

    [LibraryImport(Lib)]
    public static partial uint gtk_get_micro_version();

    [LibraryImport(Lib)]
    public static partial nuint gtk_alert_dialog_get_type();

    [LibraryImport(Lib)]
    public static partial void gtk_alert_dialog_choose(nint dialog, nint parent, nint cancellable,
        delegate* unmanaged<nint, nint, nint, void> callback, nint data);

    [LibraryImport(Lib)]
    public static partial int gtk_alert_dialog_choose_finish(nint dialog, nint result, nint* error);

    [LibraryImport(Lib)]
    public static partial nint gtk_window_new();

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void gtk_window_set_title(nint window, string title);

    [LibraryImport(Lib)]
    public static partial void gtk_window_set_default_size(nint window, int width, int height);

    [LibraryImport(Lib)]
    public static partial void gtk_window_set_child(nint window, nint child);

    [LibraryImport(Lib)]
    public static partial void gtk_window_present(nint window);

    [LibraryImport(Lib)]
    public static partial void gtk_window_destroy(nint window);
}

internal static unsafe partial class WebKit
{
    public const string Lib = "libwebkitgtk-6.0.so.4";

    [LibraryImport(Lib)]
    public static partial uint webkit_get_major_version();

    [LibraryImport(Lib)]
    public static partial uint webkit_get_minor_version();

    [LibraryImport(Lib)]
    public static partial uint webkit_get_micro_version();

    [LibraryImport(Lib)]
    public static partial nint webkit_web_view_new();

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_web_view_load_html(nint view, string content, string? baseUri);
}
