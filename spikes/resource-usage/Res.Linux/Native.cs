using System.Runtime.InteropServices;

namespace Spar.ResourceUsage;

// Imports for GLib, GObject, GIO, GTK 4 and WebKitGTK 6.0 on LP64 Linux x64. The window, view and scheme parts are
// copied from spikes/webview-profiles (checked there against GLib 2.80, GTK 4.14.5 and WebKitGTK 2.52.6); the
// website-data fetch, WebKitWebsiteData and lstat imports were added here and checked against the installed headers
// (webkit/WebKitWebsiteDataManager.h, webkit/WebKitWebsiteData.h, glib/glist.h, bits/struct_stat.h).
// C types: gboolean = int, guint = uint, guint64 = ulong, GType = nuint.

internal static partial class GLib
{
    private const string Lib = "libglib-2.0.so.0";

    [LibraryImport(Lib)]
    public static unsafe partial uint g_idle_add_full(int priority, delegate* unmanaged<nint, int> function, nint data, nint notify);

    [LibraryImport(Lib)]
    public static partial nint g_main_loop_new(nint context, int isRunning);

    [LibraryImport(Lib)]
    public static partial void g_main_loop_run(nint loop);

    [LibraryImport(Lib)]
    public static partial void g_main_loop_quit(nint loop);

    [LibraryImport(Lib)]
    public static partial int g_main_context_iteration(nint context, int mayBlock);

    /// <summary>Copies the data.</summary>
    [LibraryImport(Lib)]
    public static unsafe partial nint g_bytes_new(void* data, nuint size);

    [LibraryImport(Lib)]
    public static partial void g_bytes_unref(nint bytes);

    [LibraryImport(Lib)]
    public static partial void g_free(nint memory);

    [LibraryImport(Lib)]
    public static partial void g_error_free(nint error);

    [LibraryImport(Lib)]
    public static partial void g_list_free(nint list);
}

/// <summary>GList (glib/glist.h): data, next, prev.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GList
{
    public nint Data;
    public nint Next;
    public nint Prev;
}

internal static partial class GObject
{
    private const string Lib = "libgobject-2.0.so.0";

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial nuint g_signal_connect_data(nint instance, string signal, void* handler, nint data, nint destroyData, int flags);

    [LibraryImport(Lib)]
    public static partial nint g_object_ref_sink(nint instance);

    [LibraryImport(Lib)]
    public static partial void g_object_unref(nint instance);

    [LibraryImport(Lib)]
    public static unsafe partial nint g_object_new_with_properties(nuint type, uint count, nint* names, GValue* values);

    [LibraryImport(Lib)]
    public static unsafe partial GValue* g_value_init(GValue* value, nuint type);

    [LibraryImport(Lib)]
    public static unsafe partial void g_value_set_object(GValue* value, nint instance);

    [LibraryImport(Lib)]
    public static unsafe partial void g_value_unset(GValue* value);

    public static unsafe nuint Connect(nint instance, string signal, void* handler, nint data = 0) =>
        g_signal_connect_data(instance, signal, handler, data, 0, 0);

    /// <summary>g_object_new_with_properties with object properties.</summary>
    public static unsafe nint New(nuint type, params ReadOnlySpan<(string Name, nint Instance, nuint Type)> properties)
    {
        var names = stackalloc nint[Math.Max(1, properties.Length)];
        var values = stackalloc GValue[Math.Max(1, properties.Length)];
        for (var i = 0; i < properties.Length; i++)
        {
            names[i] = Marshal.StringToCoTaskMemUTF8(properties[i].Name);
            values[i] = default;
            g_value_init(&values[i], properties[i].Type);
            g_value_set_object(&values[i], properties[i].Instance);
        }

        try
        {
            return g_object_new_with_properties(type, (uint)properties.Length, names, values);
        }
        finally
        {
            for (var i = 0; i < properties.Length; i++)
            {
                g_value_unset(&values[i]);
                Marshal.FreeCoTaskMem(names[i]);
            }
        }
    }
}

/// <summary>GValue: a GType and a two-word union (24 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GValue
{
    public nuint Type;
    public long Data0;
    public long Data1;
}

internal static partial class Gio
{
    private const string Lib = "libgio-2.0.so.0";

    [LibraryImport(Lib)]
    public static partial nint g_memory_input_stream_new_from_bytes(nint bytes);
}

internal static partial class Gtk
{
    private const string Lib = "libgtk-4.so.1";

    [LibraryImport(Lib)]
    public static partial int gtk_init_check();

    [LibraryImport(Lib)]
    public static partial nint gtk_window_new();

    [LibraryImport(Lib)]
    public static partial void gtk_window_set_child(nint window, nint child);

    [LibraryImport(Lib)]
    public static partial void gtk_window_present(nint window);

    [LibraryImport(Lib)]
    public static partial void gtk_window_destroy(nint window);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void gtk_window_set_title(nint window, string title);

    [LibraryImport(Lib)]
    public static partial void gtk_window_set_default_size(nint window, int width, int height);
}

internal static partial class WebKit
{
    private const string Lib = "libwebkitgtk-6.0.so.4";
    private const string JscLib = "libjavascriptcoregtk-6.0.so.1";

    // WebKitWebsiteDataTypes (6.0): MEMORY_CACHE 1<<0 ... DOM_CACHE 1<<11, ALL = (1<<12)-1.
    public const int DataAll = (1 << 12) - 1;

    public static readonly string[] DataTypeNames =
    [
        "memory-cache", "disk-cache", "offline-app-cache", "session-storage", "local-storage", "indexeddb", "cookies",
        "device-id-salt", "hsts", "itp", "service-workers", "dom-cache",
    ];

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint webkit_network_session_new(string dataDirectory, string cacheDirectory);

    /// <summary>Borrowed.</summary>
    [LibraryImport(Lib)]
    public static partial nint webkit_network_session_get_website_data_manager(nint session);

    [LibraryImport(Lib)]
    public static partial nuint webkit_network_session_get_type();

    [LibraryImport(Lib)]
    public static partial nint webkit_web_context_new();

    [LibraryImport(Lib)]
    public static partial nuint webkit_web_context_get_type();

    [LibraryImport(Lib)]
    public static partial nuint webkit_web_view_get_type();

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int webkit_user_content_manager_register_script_message_handler(nint manager, string name, string? world);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial void webkit_web_context_register_uri_scheme(nint context, string scheme, delegate* unmanaged<nint, nint, void> callback, nint data, nint destroy);

    [LibraryImport(Lib)]
    public static partial nint webkit_web_context_get_security_manager(nint context);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_security_manager_register_uri_scheme_as_secure(nint manager, string scheme);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_web_view_load_uri(nint view, string uri);

    [LibraryImport(Lib)]
    public static partial nint webkit_web_view_get_user_content_manager(nint view);

    /// <summary>Borrowed path.</summary>
    [LibraryImport(Lib)]
    public static partial nint webkit_uri_scheme_request_get_path(nint request);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_uri_scheme_request_finish(nint request, nint stream, long length, string contentType);

    [LibraryImport(Lib)]
    public static unsafe partial void webkit_website_data_manager_fetch(nint manager, int types, nint cancellable, delegate* unmanaged<nint, nint, nint, void> callback, nint data);

    /// <summary>Owned GList of owned WebKitWebsiteData (unref each, then g_list_free).</summary>
    [LibraryImport(Lib)]
    public static partial nint webkit_website_data_manager_fetch_finish(nint manager, nint result, out nint error);

    /// <summary>Borrowed.</summary>
    [LibraryImport(Lib)]
    public static partial nint webkit_website_data_get_name(nint data);

    [LibraryImport(Lib)]
    public static partial int webkit_website_data_get_types(nint data);

    [LibraryImport(Lib)]
    public static partial ulong webkit_website_data_get_size(nint data, int types);

    [LibraryImport(Lib)]
    public static partial void webkit_website_data_unref(nint data);

    /// <summary>Owned string (g_free).</summary>
    [LibraryImport(JscLib)]
    public static partial nint jsc_value_to_string(nint value);
}

internal static unsafe partial class Libc
{
    /// <summary>lstat (glibc 2.33+ exports it). struct stat on x86_64 is 144 bytes: st_size at 48, st_blocks at 64.</summary>
    [LibraryImport("libc", EntryPoint = "lstat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    public static partial int lstat(string path, byte* buffer);
}
