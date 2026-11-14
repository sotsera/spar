using System.Runtime.InteropServices;

namespace Spar.PageHeartbeat;

// Imports for GLib, GObject, GIO, GTK 4 and WebKitGTK 6.0 on LP64 Linux x64. Window, view, scheme and user-script parts
// are copied from spikes/resource-usage and spikes/linux-integration (checked there against GLib 2.80, GTK 4.14.5 and
// WebKitGTK 2.52.6). Added here, checked against the 2.52.6 headers in the spar-linux-desktop image
// (webkit/WebKitWebView.h): webkit_web_view_reload, webkit_web_view_terminate_web_process,
// webkit_web_view_get_is_web_process_responsive, the "web-process-terminated" signal (view, reason, data) and
// "notify::is-web-process-responsive" (object, pspec, data); gtk_window_minimize/unminimize (gtk/gtkwindow.h).
// C types: gboolean = int, guint = uint, GType = nuint.

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

    [LibraryImport(Lib)]
    public static partial void gtk_window_minimize(nint window);

    [LibraryImport(Lib)]
    public static partial void gtk_window_unminimize(nint window);

    [LibraryImport(Lib)]
    public static partial void gtk_widget_set_visible(nint widget, int visible);

    [LibraryImport(Lib)]
    public static partial int gtk_widget_get_mapped(nint widget);

    [LibraryImport(Lib)]
    public static partial nint gtk_native_get_surface(nint native);

    /// <summary>X11 backend only (exported by libgtk-4).</summary>
    [LibraryImport(Lib)]
    public static partial nuint gdk_x11_surface_get_xid(nint surface);

    /// <summary>GdkToplevelState bits (gdk/gdkenums.h): MINIMIZED 1, MAXIMIZED 2, ... FOCUSED 64.</summary>
    [LibraryImport(Lib)]
    public static partial int gdk_toplevel_get_state(nint toplevel);
}

internal static partial class WebKit
{
    private const string Lib = "libwebkitgtk-6.0.so.4";
    private const string JscLib = "libjavascriptcoregtk-6.0.so.1";

    public const int AccelerationNever = 1; // 6.0: ALWAYS 0, NEVER 1
    public const int InjectTopFrame = 1;    // WEBKIT_USER_CONTENT_INJECT_TOP_FRAME
    public const int AtDocumentStart = 0;   // WEBKIT_USER_SCRIPT_INJECT_AT_DOCUMENT_START

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint webkit_network_session_new(string dataDirectory, string cacheDirectory);

    [LibraryImport(Lib)]
    public static partial nuint webkit_network_session_get_type();

    [LibraryImport(Lib)]
    public static partial nint webkit_web_context_new();

    [LibraryImport(Lib)]
    public static partial nuint webkit_web_context_get_type();

    [LibraryImport(Lib)]
    public static partial nuint webkit_web_view_get_type();

    [LibraryImport(Lib)]
    public static partial nint webkit_web_view_get_settings(nint view);

    [LibraryImport(Lib)]
    public static partial void webkit_settings_set_hardware_acceleration_policy(nint settings, int policy);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int webkit_user_content_manager_register_script_message_handler(nint manager, string name, string? world);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint webkit_user_script_new(string source, int injectedFrames, int injectionTime, nint allowList, nint blockList);

    [LibraryImport(Lib)]
    public static partial void webkit_user_script_unref(nint script);

    [LibraryImport(Lib)]
    public static partial void webkit_user_content_manager_add_script(nint manager, nint script);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial void webkit_web_context_register_uri_scheme(nint context, string scheme, delegate* unmanaged<nint, nint, void> callback, nint data, nint destroy);

    [LibraryImport(Lib)]
    public static partial nint webkit_web_context_get_security_manager(nint context);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_security_manager_register_uri_scheme_as_secure(nint manager, string scheme);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_web_view_load_uri(nint view, string uri);

    [LibraryImport(Lib)]
    public static partial void webkit_web_view_reload(nint view);

    [LibraryImport(Lib)]
    public static partial void webkit_web_view_stop_loading(nint view);

    [LibraryImport(Lib)]
    public static partial void webkit_web_view_terminate_web_process(nint view);

    [LibraryImport(Lib)]
    public static partial int webkit_web_view_get_is_web_process_responsive(nint view);

    [LibraryImport(Lib)]
    public static partial nint webkit_web_view_get_user_content_manager(nint view);

    [LibraryImport(Lib)]
    public static unsafe partial void webkit_web_view_evaluate_javascript(
        nint webView, byte* script, nint length, nint worldName, nint sourceUri, nint cancellable,
        delegate* unmanaged<nint, nint, nint, void> callback, nint data);

    /// <summary>Borrowed path.</summary>
    [LibraryImport(Lib)]
    public static partial nint webkit_uri_scheme_request_get_path(nint request);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_uri_scheme_request_finish(nint request, nint stream, long length, string contentType);

    /// <summary>Owned string (g_free).</summary>
    [LibraryImport(JscLib)]
    public static partial nint jsc_value_to_string(nint value);
}

internal static partial class Libc
{
    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int setenv(string name, string value, int overwrite);

    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint getenv(string name);

    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int unsetenv(string name);

    [LibraryImport("libc", SetLastError = true)]
    public static partial int kill(int pid, int signal);
}
