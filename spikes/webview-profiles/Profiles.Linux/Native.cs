using System.Runtime.InteropServices;

namespace Spar.WebViewProfiles;

// Imports for GLib, GObject, GIO, GTK and WebKitGTK on LP64 Linux x64. Copied from spikes/linux-memory (checked there
// against GLib 2.80, GTK 3.24.41 and 4.14.5, WebKitGTK 2.52.6), plus the network-session and website-data-manager
// calls, checked against the installed WebKitGTK 2.52.6 headers (webkit/WebKitWebsiteDataManager.h,
// WebKitNetworkSession.h, WebKitWebsiteData.h). C types: gboolean = int, guint = uint, GType = nuint, GTimeSpan = long.

internal static partial class GLib
{
    private const string Lib = "libglib-2.0.so.0";

    public const int PriorityDefault = 0;

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
}

internal static partial class GObject
{
    private const string Lib = "libgobject-2.0.so.0";

    public const nuint TypeString = 16 << 2;

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

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial void g_value_set_string(GValue* value, string? text);

    [LibraryImport(Lib)]
    public static unsafe partial void g_value_set_object(GValue* value, nint instance);

    [LibraryImport(Lib)]
    public static unsafe partial void g_value_unset(GValue* value);

    public static unsafe nuint Connect(nint instance, string signal, void* handler, nint data = 0) =>
        g_signal_connect_data(instance, signal, handler, data, 0, 0);

    /// <summary>g_object_new_with_properties with string or object properties.</summary>
    public static unsafe nint New(nuint type, params ReadOnlySpan<Property> properties)
    {
        var names = stackalloc nint[Math.Max(1, properties.Length)];
        var values = stackalloc GValue[Math.Max(1, properties.Length)];
        for (var i = 0; i < properties.Length; i++)
        {
            names[i] = Marshal.StringToCoTaskMemUTF8(properties[i].Name);
            values[i] = default;
            if (properties[i].Text is { } text)
            {
                g_value_init(&values[i], TypeString);
                g_value_set_string(&values[i], text);
            }
            else
            {
                g_value_init(&values[i], properties[i].ObjectType);
                g_value_set_object(&values[i], properties[i].Instance);
            }
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

/// <summary>A construct property: a string, or an object of a given type.</summary>
internal readonly record struct Property(string Name, string? Text, nint Instance = 0, nuint ObjectType = 0);

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
#if SPAR_GTK4
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
#else
    private const string Lib = "libgtk-3.so.0";

    [LibraryImport(Lib)]
    public static partial int gtk_init_check(nint argc, nint argv);

    [LibraryImport(Lib)]
    public static partial nint gtk_window_new(int type);

    [LibraryImport(Lib)]
    public static partial void gtk_container_add(nint container, nint child);

    [LibraryImport(Lib)]
    public static partial void gtk_widget_show_all(nint widget);

    [LibraryImport(Lib)]
    public static partial void gtk_widget_destroy(nint widget);
#endif

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void gtk_window_set_title(nint window, string title);

    [LibraryImport(Lib)]
    public static partial void gtk_window_set_default_size(nint window, int width, int height);
}

internal static partial class WebKit
{
#if SPAR_GTK4
    private const string Lib = "libwebkitgtk-6.0.so.4";
    private const string JscLib = "libjavascriptcoregtk-6.0.so.1";

    // WebKitWebsiteDataTypes (6.0): MEMORY_CACHE 1<<0 ... DOM_CACHE 1<<11, ALL = (1<<12)-1.
    public const int DataLocalStorage = 1 << 4;
    public const int DataIndexedDb = 1 << 5;
    public const int DataCookies = 1 << 6;
    public const int DataDiskCache = 1 << 1;
    public const int DataAll = (1 << 12) - 1;

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint webkit_network_session_new(string dataDirectory, string cacheDirectory);

    [LibraryImport(Lib)]
    public static partial nint webkit_network_session_new_ephemeral();

    /// <summary>Borrowed.</summary>
    [LibraryImport(Lib)]
    public static partial nint webkit_network_session_get_website_data_manager(nint session);

    [LibraryImport(Lib)]
    public static partial nuint webkit_network_session_get_type();

    [LibraryImport(Lib)]
    public static partial nint webkit_web_context_new();

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int webkit_user_content_manager_register_script_message_handler(nint manager, string name, string? world);
#else
    private const string Lib = "libwebkit2gtk-4.1.so.0";
    private const string JscLib = "libjavascriptcoregtk-4.1.so.0";

    // WebKitWebsiteDataTypes (4.1): MEMORY_CACHE 1<<0 ... DOM_CACHE 1<<13, ALL = (1<<14)-1.
    public const int DataLocalStorage = 1 << 4;
    public const int DataIndexedDb = 1 << 6;
    public const int DataCookies = 1 << 8;
    public const int DataDiskCache = 1 << 1;
    public const int DataAll = (1 << 14) - 1;

    [LibraryImport(Lib)]
    public static partial nuint webkit_website_data_manager_get_type();

    [LibraryImport(Lib)]
    public static partial nint webkit_website_data_manager_new_ephemeral();

    [LibraryImport(Lib)]
    public static partial nint webkit_web_context_new_with_website_data_manager(nint manager);

    /// <summary>Borrowed.</summary>
    [LibraryImport(Lib)]
    public static partial nint webkit_web_context_get_website_data_manager(nint context);

    [LibraryImport(Lib)]
    public static partial void webkit_web_context_set_sandbox_enabled(nint context, int enabled);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int webkit_user_content_manager_register_script_message_handler(nint manager, string name);

    /// <summary>Borrowed JSCValue of a WebKitJavascriptResult.</summary>
    [LibraryImport(Lib)]
    public static partial nint webkit_javascript_result_get_js_value(nint result);
#endif

    [LibraryImport(Lib)]
    public static partial nuint webkit_web_view_get_type();

    [LibraryImport(Lib)]
    public static partial nuint webkit_web_context_get_type();

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
    public static unsafe partial void webkit_website_data_manager_clear(nint manager, int types, long timespan, nint cancellable, delegate* unmanaged<nint, nint, nint, void> callback, nint data);

    [LibraryImport(Lib)]
    public static partial int webkit_website_data_manager_clear_finish(nint manager, nint result, out nint error);

    /// <summary>Borrowed, or null for an ephemeral manager.</summary>
    [LibraryImport(Lib)]
    public static partial nint webkit_website_data_manager_get_base_data_directory(nint manager);

    [LibraryImport(Lib)]
    public static partial int webkit_website_data_manager_is_ephemeral(nint manager);

    /// <summary>Owned string (g_free).</summary>
    [LibraryImport(JscLib)]
    public static partial nint jsc_value_to_string(nint value);
}
