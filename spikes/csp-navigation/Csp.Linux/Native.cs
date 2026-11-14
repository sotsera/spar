using System.Runtime.InteropServices;

namespace Csp.Linux;

// Copied from spikes/browser-telemetry and extended (policy decisions, permissions, app launching).
// Source-generated imports for GLib, GObject, GIO, libsoup, GTK and WebKitGTK (LP64 Linux x64).
// C types: gboolean = int, gint = int, guint = uint, gulong = nuint, gsize/GType = nuint, gssize = nint, gint64 = long.
// Pointers to GObjects are nint. "Owned" results must be released by us; "borrowed" ones must not.
// WebKit, libsoup and GIO signatures were compared with the installed headers
// (WebKitGTK 2.52.6 for both APIs, libsoup 3.4.4).

internal static partial class GLib
{
    private const string Lib = "libglib-2.0.so.0";

    public const int PriorityDefault = 0;

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

    [LibraryImport(Lib)]
    public static partial void g_free(nint memory);

    [LibraryImport(Lib)]
    public static partial void g_error_free(nint error);

    [LibraryImport(Lib)]
    public static unsafe partial nint g_bytes_new(void* data, nuint size);

    [LibraryImport(Lib)]
    public static partial void g_bytes_unref(nint bytes);

    /// <summary>Reads GError.message (struct GError { GQuark domain; gint code; gchar *message; }: offset 8 on LP64).</summary>
    public static unsafe string ErrorMessage(nint error) =>
        error == 0 ? "(no error)" : Marshal.PtrToStringUTF8(*(nint*)((byte*)error + 8)) ?? "(null)";

    /// <summary>Takes ownership of a g_malloc'd UTF-8 string.</summary>
    public static string? TakeString(nint text)
    {
        if (text == 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUTF8(text);
        }
        finally
        {
            g_free(text);
        }
    }
}

internal static partial class GObject
{
    private const string Lib = "libgobject-2.0.so.0";

    public const nuint TypeBoolean = 5 << 2;
    public const nuint TypeString = 16 << 2;

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial nuint g_signal_connect_data(nint instance, string signal, void* handler, nint data, nint destroyData, int flags);

    [LibraryImport(Lib)]
    public static partial void g_signal_handler_disconnect(nint instance, nuint handlerId);

    [LibraryImport(Lib)]
    public static partial nint g_object_ref(nint instance);

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
    public static unsafe partial void g_value_set_boolean(GValue* value, int boolean);

    [LibraryImport(Lib)]
    public static unsafe partial void g_value_unset(GValue* value);

    /// <summary>Connects a signal to an unmanaged callback with no user data.</summary>
    public static unsafe nuint Connect(nint instance, string signal, void* handler) =>
        g_signal_connect_data(instance, signal, handler, 0, 0, 0);

    /// <summary>g_object_new_with_properties for string, boolean and object properties (types from the value itself).</summary>
    public static unsafe nint New(nuint type, params ReadOnlySpan<GProperty> properties)
    {
        var names = stackalloc nint[properties.Length];
        var values = stackalloc GValue[properties.Length];
        for (var i = 0; i < properties.Length; i++)
        {
            names[i] = Marshal.StringToCoTaskMemUTF8(properties[i].Name);
            values[i] = default;
            switch (properties[i].Value)
            {
                case string text:
                    g_value_init(&values[i], TypeString);
                    g_value_set_string(&values[i], text);
                    break;
                case bool flag:
                    g_value_init(&values[i], TypeBoolean);
                    g_value_set_boolean(&values[i], flag ? 1 : 0);
                    break;
                case nint instance:
                    g_value_init(&values[i], properties[i].ObjectType);
                    g_value_set_object(&values[i], instance);
                    break;
                default:
                    throw new ArgumentException($"Unsupported property value for {properties[i].Name}.", nameof(properties));
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

/// <summary>One construct property: a string, a bool, or a GObject of <paramref name="ObjectType"/>.</summary>
internal readonly record struct GProperty(string Name, object Value, nuint ObjectType = 0);

/// <summary>struct GValue { GType g_type; union { … } data[2]; }: 24 bytes on LP64.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GValue
{
    public nuint Type;
    public ulong Data0;
    public ulong Data1;
}

internal static partial class Gio
{
    private const string Lib = "libgio-2.0.so.0";

    [LibraryImport(Lib)]
    public static partial nint g_memory_input_stream_new_from_bytes(nint bytes);

    /// <summary>A pollable input stream over a file descriptor (gio-unix, exported by libgio-2.0).</summary>
    [LibraryImport(Lib)]
    public static partial nint g_unix_input_stream_new(int fd, int closeFd);

    [LibraryImport(Lib)]
    public static unsafe partial int g_input_stream_read_all(nint stream, void* buffer, nuint count, out nuint bytesRead, nint cancellable, out nint error);

    [LibraryImport(Lib)]
    public static partial int g_input_stream_close(nint stream, nint cancellable, out nint error);

    /// <summary>An owned stream holding a copy of <paramref name="content"/>.</summary>
    public static unsafe nint MemoryStream(ReadOnlySpan<byte> content)
    {
        fixed (byte* data = content)
        {
            var bytes = GLib.g_bytes_new(data, (nuint)content.Length);
            try
            {
                return g_memory_input_stream_new_from_bytes(bytes);
            }
            finally
            {
                GLib.g_bytes_unref(bytes);
            }
        }
    }

    /// <summary>Reads a whole (small, in-memory) request body synchronously.</summary>
    public static unsafe byte[] ReadAll(nint stream)
    {
        var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            nuint read;
            nint error;
            fixed (byte* data = buffer)
            {
                if (g_input_stream_read_all(stream, data, (nuint)buffer.Length, out read, 0, out error) == 0)
                {
                    var message = GLib.ErrorMessage(error);
                    GLib.g_error_free(error);
                    throw new IOException("Reading the request body failed: " + message);
                }
            }

            output.Write(buffer, 0, (int)read);
            if (read < (nuint)buffer.Length)
            {
                return output.ToArray();
            }
        }
    }
}

internal static partial class Soup
{
    private const string Lib = "libsoup-3.0.so.0";

    public const int MessageHeadersResponse = 1;

    [LibraryImport(Lib)]
    public static partial nint soup_message_headers_new(int type);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void soup_message_headers_append(nint headers, string name, string value);

    /// <summary>Borrowed result.</summary>
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint soup_message_headers_get_one(nint headers, string name);

    public static string? Header(nint headers, string name) =>
        headers == 0 ? null : Marshal.PtrToStringUTF8(soup_message_headers_get_one(headers, name));
}

internal static partial class LibC
{
    public const int OCloexec = 0x80000;

    [LibraryImport("libc", SetLastError = true)]
    public static unsafe partial int pipe2(int* fds, int flags);

    [LibraryImport("libc", SetLastError = true)]
    public static partial int close(int fd);

    [LibraryImport("libc", SetLastError = true)]
    public static partial int fcntl(int fd, int command, int argument);

    public const int FSetPipeSize = 1031;
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
    public static partial void gtk_window_destroy(nint window);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int gdk_texture_save_to_png(nint texture, string fileName);
#else
    private const string Lib = "libgtk-3.so.0";

    [LibraryImport(Lib)]
    public static partial int gtk_init_check(nint argc, nint argv);

    [LibraryImport(Lib)]
    public static partial nint gtk_window_new(int type);

    [LibraryImport(Lib)]
    public static partial void gtk_container_add(nint container, nint widget);

    [LibraryImport(Lib)]
    public static partial void gtk_widget_show_all(nint widget);

    [LibraryImport(Lib)]
    public static partial void gtk_widget_destroy(nint widget);
#endif

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void gtk_window_set_title(nint window, string title);

    [LibraryImport(Lib)]
    public static partial void gtk_window_set_default_size(nint window, int width, int height);

    [LibraryImport(Lib)]
    public static partial void gtk_window_present(nint window);

    [LibraryImport(Lib)]
    public static partial void gtk_window_close(nint window);
}

internal static partial class Cairo
{
    private const string Lib = "libcairo.so.2";

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int cairo_surface_write_to_png(nint surface, string fileName);

    [LibraryImport(Lib)]
    public static partial void cairo_surface_destroy(nint surface);
}

internal static partial class Jsc
{
#if SPAR_GTK4
    private const string Lib = "libjavascriptcoregtk-6.0.so.1";
#else
    private const string Lib = "libjavascriptcoregtk-4.1.so.0";
#endif

    /// <summary>Owned result (g_free).</summary>
    [LibraryImport(Lib)]
    public static partial nint jsc_value_to_string(nint value);

    /// <summary>Owned result (g_free).</summary>
    [LibraryImport(Lib)]
    public static partial nint jsc_value_to_json(nint value, uint indent);
}

internal static partial class WebKit
{
#if SPAR_GTK4
    private const string Lib = "libwebkitgtk-6.0.so.4";
#else
    private const string Lib = "libwebkit2gtk-4.1.so.0";
#endif

    public const int LoadStarted = 0;
    public const int LoadCommitted = 2;
    public const int LoadFinished = 3;
    public const int InjectTopFrame = 1;
    public const int InjectAtDocumentStart = 0;
    public const int SnapshotRegionVisible = 0;
    public const int SnapshotOptionsNone = 0;

    [LibraryImport(Lib)]
    public static partial nuint webkit_web_view_get_type();

    [LibraryImport(Lib)]
    public static partial nint webkit_web_context_new();

    [LibraryImport(Lib)]
    public static partial nuint webkit_web_context_get_type();

#if SPAR_GTK4
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint webkit_network_session_new(string dataDirectory, string cacheDirectory);

    [LibraryImport(Lib)]
    public static partial nuint webkit_network_session_get_type();

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int webkit_user_content_manager_register_script_message_handler(nint manager, string name, string? worldName);
#else
    [LibraryImport(Lib)]
    public static partial nuint webkit_website_data_manager_get_type();

    [LibraryImport(Lib)]
    public static partial nint webkit_web_context_new_with_website_data_manager(nint manager);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int webkit_user_content_manager_register_script_message_handler(nint manager, string name);

    [LibraryImport(Lib)]
    public static partial void webkit_web_context_set_sandbox_enabled(nint context, int enabled);

    /// <summary>Borrowed JSCValue of a WebKitJavascriptResult (4.1 only; 6.0 passes the JSCValue directly).</summary>
    [LibraryImport(Lib)]
    public static partial nint webkit_javascript_result_get_js_value(nint result);
#endif

    [LibraryImport(Lib)]
    public static partial void webkit_web_context_set_automation_allowed(nint context, int allowed);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial void webkit_web_context_register_uri_scheme(
        nint context, string scheme, delegate* unmanaged<nint, nint, void> callback, nint data, nint destroy);

    /// <summary>Borrowed.</summary>
    [LibraryImport(Lib)]
    public static partial nint webkit_web_context_get_security_manager(nint context);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_security_manager_register_uri_scheme_as_secure(nint manager, string scheme);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_security_manager_register_uri_scheme_as_cors_enabled(nint manager, string scheme);

    /// <summary>Borrowed.</summary>
    [LibraryImport(Lib)]
    public static partial nint webkit_web_view_get_user_content_manager(nint webView);

    /// <summary>Borrowed.</summary>
    [LibraryImport(Lib)]
    public static partial nint webkit_web_view_get_settings(nint webView);

    [LibraryImport(Lib)]
    public static partial void webkit_settings_set_enable_developer_extras(nint settings, int enabled);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint webkit_user_script_new(string source, int injectedFrames, int injectionTime, nint allowList, nint blockList);

    [LibraryImport(Lib)]
    public static partial void webkit_user_script_unref(nint script);

    [LibraryImport(Lib)]
    public static partial void webkit_user_content_manager_add_script(nint manager, nint script);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_web_view_load_uri(nint webView, string uri);

    [LibraryImport(Lib)]
    public static partial void webkit_web_view_reload(nint webView);

    /// <summary>Borrowed.</summary>
    [LibraryImport(Lib)]
    public static partial nint webkit_web_view_get_uri(nint webView);

    [LibraryImport(Lib)]
    public static unsafe partial void webkit_web_view_evaluate_javascript(
        nint webView, byte* script, nint length, nint worldName, nint sourceUri, nint cancellable,
        delegate* unmanaged<nint, nint, nint, void> callback, nint data);

    [LibraryImport(Lib)]
    public static unsafe partial void webkit_web_view_call_async_javascript_function(
        nint webView, byte* body, nint length, nint arguments, nint worldName, nint sourceUri, nint cancellable,
        delegate* unmanaged<nint, nint, nint, void> callback, nint data);

    /// <summary>Owned JSCValue (g_object_unref), or 0 with an owned GError.</summary>
    [LibraryImport(Lib)]
    public static partial nint webkit_web_view_call_async_javascript_function_finish(nint webView, nint result, out nint error);

    [LibraryImport(Lib)]
    public static unsafe partial void webkit_web_view_get_snapshot(
        nint webView, int region, int options, nint cancellable, delegate* unmanaged<nint, nint, nint, void> callback, nint data);

    /// <summary>Owned: a cairo_surface_t* (4.1) or a GdkTexture* (6.0).</summary>
    [LibraryImport(Lib)]
    public static partial nint webkit_web_view_get_snapshot_finish(nint webView, nint result, out nint error);

    [LibraryImport(Lib)]
    public static partial void webkit_web_view_terminate_web_process(nint webView);

    // URI scheme requests (callback arguments are borrowed; we ref them while the response is pending).

    [LibraryImport(Lib)]
    public static partial nint webkit_uri_scheme_request_get_path(nint request);

    [LibraryImport(Lib)]
    public static partial nint webkit_uri_scheme_request_get_http_method(nint request);

    /// <summary>Borrowed SoupMessageHeaders.</summary>
    [LibraryImport(Lib)]
    public static partial nint webkit_uri_scheme_request_get_http_headers(nint request);

    /// <summary>Owned GInputStream, or 0 when there is no body.</summary>
    [LibraryImport(Lib)]
    public static partial nint webkit_uri_scheme_request_get_http_body(nint request);

    /// <summary>Owned WebKitURISchemeResponse.</summary>
    [LibraryImport(Lib)]
    public static partial nint webkit_uri_scheme_response_new(nint stream, long length);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_uri_scheme_response_set_status(nint response, uint statusCode, string? reason);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_uri_scheme_response_set_content_type(nint response, string contentType);

    /// <summary>Takes ownership of the headers.</summary>
    [LibraryImport(Lib)]
    public static partial void webkit_uri_scheme_response_set_http_headers(nint response, nint headers);

    [LibraryImport(Lib)]
    public static partial void webkit_uri_scheme_request_finish_with_response(nint request, nint response);
}

internal static partial class WebKitPolicy
{
#if SPAR_GTK4
    private const string Lib = "libwebkitgtk-6.0.so.4";
#else
    private const string Lib = "libwebkit2gtk-4.1.so.0";
#endif

    public const int NavigationAction = 0;
    public const int NewWindowAction = 1;
    public const int Response = 2;

    /// <summary>Borrowed.</summary>
    [LibraryImport(Lib)]
    public static partial nint webkit_navigation_policy_decision_get_navigation_action(nint decision);

    /// <summary>Borrowed WebKitURIRequest.</summary>
    [LibraryImport(Lib)]
    public static partial nint webkit_navigation_action_get_request(nint action);

    [LibraryImport(Lib)]
    public static partial int webkit_navigation_action_is_redirect(nint action);

    [LibraryImport(Lib)]
    public static partial int webkit_navigation_action_is_user_gesture(nint action);

    [LibraryImport(Lib)]
    public static partial uint webkit_navigation_action_get_mouse_button(nint action);

    [LibraryImport(Lib)]
    public static partial int webkit_navigation_action_get_navigation_type(nint action);

    /// <summary>Borrowed.</summary>
    [LibraryImport(Lib)]
    public static partial nint webkit_navigation_action_get_frame_name(nint action);

    /// <summary>Borrowed.</summary>
    [LibraryImport(Lib)]
    public static partial nint webkit_uri_request_get_uri(nint request);

    /// <summary>Borrowed WebKitURIRequest.</summary>
    [LibraryImport(Lib)]
    public static partial nint webkit_response_policy_decision_get_request(nint decision);

    [LibraryImport(Lib)]
    public static partial int webkit_response_policy_decision_is_main_frame_main_resource(nint decision);

    [LibraryImport(Lib)]
    public static partial void webkit_policy_decision_ignore(nint decision);

    [LibraryImport(Lib)]
    public static partial void webkit_policy_decision_use(nint decision);

    [LibraryImport(Lib)]
    public static partial void webkit_permission_request_deny(nint request);

    /// <summary>Borrowed: the full URI including the query.</summary>
    [LibraryImport(Lib)]
    public static partial nint webkit_uri_scheme_request_get_uri(nint request);

    [LibraryImport(Lib)]
    public static partial void webkit_settings_set_enable_media_stream(nint settings, int enabled);

    [LibraryImport(Lib)]
    public static partial int webkit_settings_get_enable_media_stream(nint settings);
}

internal static partial class GType
{
    [LibraryImport("libgobject-2.0.so.0")]
    private static partial nint g_type_name(nuint type);

    /// <summary>G_OBJECT_TYPE_NAME: instance->g_class->g_type.</summary>
    public static unsafe string Name(nint instance) =>
        Marshal.PtrToStringUTF8(g_type_name(**(nuint**)instance)) ?? "?";
}

internal static partial class GioApps
{
    /// <summary>Opens a URI with the desktop's default handler. Returns 0 with an owned GError on failure.</summary>
    [LibraryImport("libgio-2.0.so.0", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int g_app_info_launch_default_for_uri(string uri, nint launchContext, out nint error);
}
