using System.Runtime.InteropServices;

namespace Mem.Linux;

// Imports for GLib, GObject, GIO, GTK/GDK and WebKitGTK on LP64 Linux x64, checked against the installed headers
// (GLib 2.80, GTK 3.24.41 and 4.14.5, WebKitGTK 2.52.6). C types: gboolean and gint = int, guint and guint32 = uint,
// gsize and GType = nuint, gint64 = long. GObject pointers are nint.

internal static partial class GLib
{
    private const string Lib = "libglib-2.0.so.0";

    [LibraryImport(Lib)]
    public static unsafe partial uint g_idle_add_full(int priority, delegate* unmanaged<nint, int> function, nint data, nint notify);

    [LibraryImport(Lib)]
    public static unsafe partial uint g_timeout_add_full(int priority, uint interval, delegate* unmanaged<nint, int> function, nint data, nint notify);

    [LibraryImport(Lib)]
    public static partial nint g_main_loop_new(nint context, int isRunning);

    [LibraryImport(Lib)]
    public static partial void g_main_loop_run(nint loop);

    [LibraryImport(Lib)]
    public static partial void g_main_loop_quit(nint loop);

    [LibraryImport(Lib)]
    public static partial void g_main_loop_unref(nint loop);

    [LibraryImport(Lib)]
    public static partial int g_main_context_iteration(nint context, int mayBlock);

    /// <summary>Copies the data.</summary>
    [LibraryImport(Lib)]
    public static unsafe partial nint g_bytes_new(void* data, nuint size);

    [LibraryImport(Lib)]
    public static partial void g_bytes_unref(nint bytes);

    [LibraryImport(Lib)]
    public static partial void g_free(nint memory);
}

internal static partial class GObject
{
    private const string Lib = "libgobject-2.0.so.0";

    public const nuint TypeString = 16 << 2;

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial nuint g_signal_connect_data(nint instance, string signal, void* handler, nint data, nint destroyData, int flags);

    [LibraryImport(Lib)]
    public static partial nint g_object_ref(nint instance);

    [LibraryImport(Lib)]
    public static partial nint g_object_ref_sink(nint instance);

    [LibraryImport(Lib)]
    public static partial void g_object_unref(nint instance);

    [LibraryImport(Lib)]
    public static partial void g_object_run_dispose(nint instance);

    [LibraryImport(Lib)]
    public static unsafe partial void g_object_weak_ref(nint instance, delegate* unmanaged<nint, nint, void> notify, nint data);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial void g_object_set_data_full(nint instance, string key, nint data, delegate* unmanaged<nint, void> destroy);

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

    /// <summary>Borrowed.</summary>
    [LibraryImport(Lib)]
    public static partial nint g_type_name_from_instance(nint instance);

    public static string TypeName(nint instance) => instance == 0 ? "(null)" : Marshal.PtrToStringUTF8(g_type_name_from_instance(instance)) ?? "?";

    /// <summary>GObject.ref_count (struct GObject { GTypeInstance g_type_instance; guint ref_count; ... }: offset 8). Diagnostics only.</summary>
    public static unsafe uint RefCount(nint instance) => instance == 0 ? 0 : *(uint*)((byte*)instance + 8);

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

    [LibraryImport(Lib)]
    public static partial nint gtk_event_controller_legacy_new();

    [LibraryImport(Lib)]
    public static partial void gtk_event_controller_set_propagation_phase(nint controller, int phase);

    [LibraryImport(Lib)]
    public static partial void gtk_widget_add_controller(nint widget, nint controller);

    /// <summary>Owned GListModel of the widget's event controllers.</summary>
    [LibraryImport(Lib)]
    public static partial nint gtk_widget_observe_controllers(nint widget);

    [LibraryImport(Lib)]
    public static partial void gtk_event_controller_reset(nint controller);

    /// <summary>Borrowed name, or null.</summary>
    [LibraryImport(Lib)]
    public static partial nint gtk_event_controller_get_name(nint controller);

    [LibraryImport("libgio-2.0.so.0")]
    public static partial uint g_list_model_get_n_items(nint list);

    /// <summary>Owned item.</summary>
    [LibraryImport("libgio-2.0.so.0")]
    public static partial nint g_list_model_get_item(nint list, uint position);

    [LibraryImport(Lib)]
    public static partial void gtk_widget_set_visible(nint widget, int visible);

    [LibraryImport(Lib)]
    public static partial nint gtk_native_get_surface(nint native);

    [LibraryImport(Lib)]
    public static partial nint gtk_native_get_renderer(nint native);

    [LibraryImport(Lib)]
    public static partial nint gdk_display_get_default();

    [LibraryImport(Lib)]
    public static partial int gdk_event_get_event_type(nint gdkEvent);

    [LibraryImport(Lib)]
    public static partial nint gdk_event_get_device(nint gdkEvent);

    [LibraryImport(Lib)]
    public static partial uint gdk_event_get_time(nint gdkEvent);

    [LibraryImport(Lib)]
    public static partial int gdk_event_get_position(nint gdkEvent, out double x, out double y);

    [LibraryImport(Lib)]
    public static partial uint gdk_button_event_get_button(nint gdkEvent);

    [LibraryImport(Lib)]
    public static partial void gdk_toplevel_begin_move(nint toplevel, nint device, int button, double x, double y, uint timestamp);

    [LibraryImport(Lib)]
    public static partial void gdk_toplevel_begin_resize(nint toplevel, int edge, nint device, int button, double x, double y, uint timestamp);
#else
    private const string Lib = "libgtk-3.so.0";
    private const string GdkLib = "libgdk-3.so.0";

    [LibraryImport(Lib)]
    public static partial int gtk_init_check(nint argc, nint argv);

    [LibraryImport(Lib)]
    public static partial nint gtk_window_new(int type);

    [LibraryImport(Lib)]
    public static partial void gtk_container_add(nint container, nint child);

    [LibraryImport(Lib)]
    public static partial void gtk_container_remove(nint container, nint child);

    [LibraryImport(Lib)]
    public static partial void gtk_widget_show_all(nint widget);

    [LibraryImport(Lib)]
    public static partial void gtk_widget_destroy(nint widget);

    [LibraryImport(Lib)]
    public static partial void gtk_widget_hide(nint widget);

    [LibraryImport(Lib)]
    public static partial void gtk_window_begin_move_drag(nint window, int button, int rootX, int rootY, uint timestamp);

    [LibraryImport(Lib)]
    public static partial void gtk_window_begin_resize_drag(nint window, int edge, int button, int rootX, int rootY, uint timestamp);

    [LibraryImport(GdkLib)]
    public static partial nint gdk_display_get_default();

    [LibraryImport(GdkLib)]
    public static partial int gdk_event_get_event_type(nint gdkEvent);

    [LibraryImport(GdkLib)]
    public static partial uint gdk_event_get_time(nint gdkEvent);

    [LibraryImport(GdkLib)]
    public static partial int gdk_event_get_button(nint gdkEvent, out uint button);

    [LibraryImport(GdkLib)]
    public static partial int gdk_event_get_root_coords(nint gdkEvent, out double x, out double y);
#endif

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void gtk_window_set_title(nint window, string title);

    [LibraryImport(Lib)]
    public static partial void gtk_window_set_default_size(nint window, int width, int height);

    [LibraryImport(Lib)]
    public static partial void gtk_window_close(nint window);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint gtk_label_new(string text);

    [LibraryImport(Lib)]
    public static partial void gtk_widget_set_sensitive(nint widget, int sensitive);

    [LibraryImport(Lib)]
    public static partial void gtk_widget_unrealize(nint widget);

    [LibraryImport(Lib)]
    public static partial void gtk_widget_grab_focus(nint widget);
}

internal static partial class WebKit
{
#if SPAR_GTK4
    private const string Lib = "libwebkitgtk-6.0.so.4";
    private const string JscLib = "libjavascriptcoregtk-6.0.so.1";
    public const int AccelerationAlways = 0;
    public const int AccelerationNever = 1;

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint webkit_network_session_new(string dataDirectory, string cacheDirectory);

    [LibraryImport(Lib)]
    public static partial nuint webkit_network_session_get_type();

    [LibraryImport(Lib)]
    public static partial nint webkit_web_context_new();

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int webkit_user_content_manager_register_script_message_handler(nint manager, string name, string? world);
#else
    private const string Lib = "libwebkit2gtk-4.1.so.0";
    private const string JscLib = "libjavascriptcoregtk-4.1.so.0";
    public const int AccelerationAlways = 1;
    public const int AccelerationNever = 2;

    [LibraryImport(Lib)]
    public static partial nuint webkit_website_data_manager_get_type();

    [LibraryImport(Lib)]
    public static partial nint webkit_web_context_new_with_website_data_manager(nint manager);

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

    [LibraryImport(Lib)]
    public static partial nint webkit_web_view_get_settings(nint view);

    [LibraryImport(Lib)]
    public static partial void webkit_settings_set_hardware_acceleration_policy(nint settings, int policy);

    [LibraryImport(Lib)]
    public static partial int webkit_settings_get_hardware_acceleration_policy(nint settings);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_web_view_load_uri(nint view, string uri);

    [LibraryImport(Lib)]
    public static partial void webkit_web_view_terminate_web_process(nint view);

    [LibraryImport(Lib)]
    public static partial void webkit_web_view_try_close(nint view);

    [LibraryImport(Lib)]
    public static partial nint webkit_web_view_get_user_content_manager(nint view);

    /// <summary>Borrowed path.</summary>
    [LibraryImport(Lib)]
    public static partial nint webkit_uri_scheme_request_get_path(nint request);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_uri_scheme_request_finish(nint request, nint stream, long length, string contentType);

    /// <summary>Owned string (g_free).</summary>
    [LibraryImport(JscLib)]
    public static partial nint jsc_value_to_string(nint value);
}
