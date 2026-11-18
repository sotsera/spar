using System.Runtime.InteropServices;

namespace Spar.WindowAutomation.Linux.Native;

// Hand-written imports for GLib, GObject, GIO, libsoup 3, GTK 4, WebKitGTK 6.0, JavaScriptCore 6.0, Xlib and XTEST.
// Signatures follow the installed headers (Ubuntu 24.04: WebKitGTK 2.52.6, GTK 4.14.5). Strings are UTF-8.

/// <summary>GError: { GQuark domain; gint code; gchar *message; }.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GError
{
    public uint Domain;
    public int Code;
    public nint Message;
}

/// <summary>GValue: { GType g_type; union data[2]; } = 24 bytes on LP64.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GValue
{
    public nuint Type;
    public long Data0;
    public long Data1;
}

[StructLayout(LayoutKind.Sequential)]
internal struct GraphenePoint
{
    public float X;
    public float Y;
}

internal static partial class GLib
{
    private const string Lib = "libglib-2.0.so.0";
    private const string GObjectLib = "libgobject-2.0.so.0";
    private const string GioLib = "libgio-2.0.so.0";

    [LibraryImport(Lib)]
    public static partial nint g_main_context_default();

    [LibraryImport(Lib)]
    public static partial nint g_main_loop_new(nint context, int isRunning);

    [LibraryImport(Lib)]
    public static partial void g_main_loop_run(nint loop);

    [LibraryImport(Lib)]
    public static partial void g_main_loop_quit(nint loop);

    [LibraryImport(Lib)]
    public static partial int g_main_context_iteration(nint context, int mayBlock);

    [LibraryImport(Lib)]
    public static unsafe partial uint g_idle_add_full(int priority, delegate* unmanaged<nint, int> function, nint data, nint notify);

    [LibraryImport(Lib)]
    public static partial void g_free(nint memory);

    [LibraryImport(Lib)]
    public static partial void g_error_free(nint error);

    [LibraryImport(Lib)]
    public static unsafe partial nint g_bytes_new(void* data, nuint size);

    [LibraryImport(Lib)]
    public static partial nint g_bytes_get_data(nint bytes, out nuint size);

    [LibraryImport(Lib)]
    public static partial void g_bytes_unref(nint bytes);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint g_quark_from_string(string text);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint g_error_new_literal(nint domain, int code, string message);

    [LibraryImport(GObjectLib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nuint g_signal_connect_data(nint instance, string signal, nint handler, nint data, nint destroy, int flags);

    [LibraryImport(GObjectLib)]
    public static partial void g_signal_handler_disconnect(nint instance, nuint id);

    [LibraryImport(GObjectLib)]
    public static partial nint g_object_ref(nint instance);

    [LibraryImport(GObjectLib)]
    public static partial void g_object_unref(nint instance);

    [LibraryImport(GObjectLib)]
    public static unsafe partial nint g_object_new_with_properties(nuint type, uint count, nint* names, GValue* values);

    [LibraryImport(GObjectLib)]
    public static unsafe partial GValue* g_value_init(GValue* value, nuint type);

    [LibraryImport(GObjectLib)]
    public static unsafe partial void g_value_set_object(GValue* value, nint instance);

    [LibraryImport(GObjectLib)]
    public static unsafe partial void g_value_set_boolean(GValue* value, int boolean);

    [LibraryImport(GObjectLib)]
    public static unsafe partial void g_value_unset(GValue* value);

    [LibraryImport(GObjectLib)]
    public static partial nuint g_object_get_type();

    [LibraryImport(GioLib)]
    public static partial nint g_memory_input_stream_new_from_bytes(nint bytes);

    public static string? Utf8(nint text) => text == 0 ? null : Marshal.PtrToStringUTF8(text);

    /// <summary>Reads a GError's message and frees it.</summary>
    public static unsafe string TakeError(nint error)
    {
        if (error == 0)
        {
            return "unknown error";
        }

        var message = Utf8(((GError*)error)->Message) ?? "?";
        g_error_free(error);
        return message;
    }

    /// <summary>g_object_new_with_properties for object and boolean properties (G_TYPE_BOOLEAN is fundamental 5 &lt;&lt; 2).</summary>
    public static unsafe nint New(nuint type, params (string Name, nint Object, bool? Boolean)[] properties)
    {
        var names = stackalloc nint[properties.Length];
        var values = stackalloc GValue[properties.Length];
        try
        {
            for (var i = 0; i < properties.Length; i++)
            {
                names[i] = Marshal.StringToCoTaskMemUTF8(properties[i].Name);
                values[i] = default;
                if (properties[i].Boolean is { } flag)
                {
                    g_value_init(&values[i], 5 << 2);
                    g_value_set_boolean(&values[i], flag ? 1 : 0);
                }
                else
                {
                    g_value_init(&values[i], g_object_get_type());
                    g_value_set_object(&values[i], properties[i].Object);
                }
            }

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

internal static partial class Soup
{
    private const string Lib = "libsoup-3.0.so.0";

    [LibraryImport(Lib)]
    public static partial nint soup_message_headers_new(int type);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void soup_message_headers_append(nint headers, string name, string value);
}

internal static partial class Gtk
{
    private const string Lib = "libgtk-4.so.1";

    [LibraryImport(Lib)]
    public static partial int gtk_init_check();

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

    [LibraryImport(Lib)]
    public static partial nint gtk_native_get_surface(nint native);

    [LibraryImport(Lib)]
    public static partial void gtk_native_get_surface_transform(nint native, out double x, out double y);

    [LibraryImport(Lib)]
    public static unsafe partial int gtk_widget_compute_point(nint widget, nint target, GraphenePoint* point, GraphenePoint* result);

    [LibraryImport(Lib)]
    public static partial int gtk_widget_get_width(nint widget);

    [LibraryImport(Lib)]
    public static partial int gtk_widget_get_height(nint widget);

    [LibraryImport(Lib)]
    public static partial int gtk_widget_grab_focus(nint widget);

    [LibraryImport(Lib)]
    public static partial nuint gdk_x11_surface_get_xid(nint surface);

    [LibraryImport(Lib)]
    public static partial int gdk_texture_get_width(nint texture);

    [LibraryImport(Lib)]
    public static partial int gdk_texture_get_height(nint texture);

    [LibraryImport(Lib)]
    public static unsafe partial void gdk_texture_download(nint texture, byte* data, nuint stride);

    [LibraryImport(Lib)]
    public static partial nint gdk_texture_save_to_png_bytes(nint texture);
}

internal static partial class WebKit
{
    private const string Lib = "libwebkitgtk-6.0.so.4";
    private const string Jsc = "libjavascriptcoregtk-6.0.so.1";

    public const int LoadFinished = 3;
    public const int HardwareAccelerationNever = 1;

    [LibraryImport(Lib)]
    public static partial nint webkit_web_context_get_default();

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial void webkit_web_context_register_uri_scheme(nint context, string scheme, delegate* unmanaged<nint, nint, void> callback, nint data, nint destroy);

    [LibraryImport(Lib)]
    public static partial nint webkit_web_context_get_security_manager(nint context);

    [LibraryImport(Lib)]
    public static partial void webkit_web_context_set_automation_allowed(nint context, int allowed);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_security_manager_register_uri_scheme_as_secure(nint manager, string scheme);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_security_manager_register_uri_scheme_as_cors_enabled(nint manager, string scheme);

    [LibraryImport(Lib)]
    public static partial nint webkit_network_session_new_ephemeral();

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint webkit_network_session_new(string dataDirectory, string cacheDirectory);

    [LibraryImport(Lib)]
    public static partial nint webkit_web_context_get_network_session_for_automation(nint context);

    [LibraryImport(Lib)]
    public static partial nint webkit_application_info_new();

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_application_info_set_name(nint info, string name);

    [LibraryImport(Lib)]
    public static partial void webkit_application_info_set_version(nint info, ulong major, ulong minor, ulong micro);

    [LibraryImport(Lib)]
    public static partial void webkit_application_info_unref(nint info);

    [LibraryImport(Lib)]
    public static partial void webkit_automation_session_set_application_info(nint session, nint info);

    [LibraryImport(Lib)]
    public static partial nint webkit_user_content_manager_new();

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint webkit_user_script_new_for_world(string source, int injectedFrames, int injectionTime, string? worldName, nint allowList, nint blockList);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint webkit_user_script_new(string source, int injectedFrames, int injectionTime, nint allowList, nint blockList);

    [LibraryImport(Lib)]
    public static partial void webkit_user_content_manager_add_script(nint manager, nint script);

    [LibraryImport(Lib)]
    public static partial void webkit_user_script_unref(nint script);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int webkit_user_content_manager_register_script_message_handler(nint manager, string name, string? worldName);

    [LibraryImport(Lib)]
    public static partial nuint webkit_web_view_get_type();

    [LibraryImport(Lib)]
    public static partial nint webkit_web_view_get_settings(nint view);

    [LibraryImport(Lib)]
    public static partial void webkit_settings_set_hardware_acceleration_policy(nint settings, int policy);

    [LibraryImport(Lib)]
    public static partial void webkit_settings_set_enable_developer_extras(nint settings, int enabled);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_web_view_load_uri(nint view, string uri);

    [LibraryImport(Lib)]
    public static partial nint webkit_web_view_get_uri(nint view);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial void webkit_web_view_call_async_javascript_function(nint view, string body, nint length, nint arguments, string? worldName, string? sourceUri, nint cancellable, delegate* unmanaged<nint, nint, nint, void> callback, nint data);

    [LibraryImport(Lib)]
    public static partial nint webkit_web_view_call_async_javascript_function_finish(nint view, nint result, out nint error);

    [LibraryImport(Lib)]
    public static unsafe partial void webkit_web_view_get_snapshot(nint view, int region, int options, nint cancellable, delegate* unmanaged<nint, nint, nint, void> callback, nint data);

    [LibraryImport(Lib)]
    public static partial nint webkit_web_view_get_snapshot_finish(nint view, nint result, out nint error);

    [LibraryImport(Lib)]
    public static partial nint webkit_uri_scheme_request_get_path(nint request);

    [LibraryImport(Lib)]
    public static partial nint webkit_uri_scheme_request_get_web_view(nint request);

    [LibraryImport(Lib)]
    public static partial void webkit_uri_scheme_request_finish_with_response(nint request, nint response);

    [LibraryImport(Lib)]
    public static partial nint webkit_uri_scheme_response_new(nint stream, long length);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_uri_scheme_response_set_status(nint response, uint status, string? reason);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_uri_scheme_response_set_content_type(nint response, string contentType);

    [LibraryImport(Lib)]
    public static partial void webkit_uri_scheme_response_set_http_headers(nint response, nint headers);

    [LibraryImport(Lib)]
    public static partial int webkit_script_dialog_get_dialog_type(nint dialog);

    [LibraryImport(Lib)]
    public static partial nint webkit_script_dialog_get_message(nint dialog);

    [LibraryImport(Lib)]
    public static partial void webkit_file_chooser_request_select_files(nint request, nint[] files);

    [LibraryImport(Lib)]
    public static partial void webkit_file_chooser_request_cancel(nint request);

    [LibraryImport(Jsc)]
    public static partial nint jsc_value_to_json(nint value, uint indent);

    [LibraryImport(Jsc)]
    public static partial nint jsc_value_to_string(nint value);
}

/// <summary>Xlib and XTEST, on the harness's own display connection (never GTK's).</summary>
internal static partial class X11
{
    private const string Lib = "libX11.so.6";
    private const string Tst = "libXtst.so.6";

    [LibraryImport(Lib)]
    public static partial nint XOpenDisplay(nint name);

    [LibraryImport(Lib)]
    public static partial int XCloseDisplay(nint display);

    [LibraryImport(Lib)]
    public static partial int XSync(nint display, int discard);

    [LibraryImport(Lib)]
    public static partial nuint XDefaultRootWindow(nint display);

    [LibraryImport(Lib)]
    public static partial int XTranslateCoordinates(nint display, nuint source, nuint destination, int x, int y, out int destinationX, out int destinationY, out nuint child);

    [LibraryImport(Lib)]
    public static partial byte XKeysymToKeycode(nint display, nuint keysym);

    [LibraryImport(Lib)]
    public static partial int XDisplayKeycodes(nint display, out int minimum, out int maximum);

    [LibraryImport(Lib)]
    public static partial nint XGetKeyboardMapping(nint display, byte first, int count, out int keysymsPerKeycode);

    [LibraryImport(Lib)]
    public static unsafe partial int XChangeKeyboardMapping(nint display, int first, int keysymsPerKeycode, nuint* keysyms, int count);

    [LibraryImport(Lib)]
    public static partial int XFree(nint data);

    [LibraryImport(Lib)]
    public static partial int XQueryTree(nint display, nuint window, out nuint root, out nuint parent, out nint children, out uint count);

    [LibraryImport(Lib)]
    public static unsafe partial int XGetWindowAttributes(nint display, nuint window, byte* attributes);

    [LibraryImport(Lib)]
    public static partial int XFetchName(nint display, nuint window, out nint name);

    [LibraryImport(Lib)]
    public static partial int XSetInputFocus(nint display, nuint window, int revertTo, nuint time);

    [LibraryImport(Lib)]
    public static partial int XGetInputFocus(nint display, out nuint window, out int revertTo);

    [LibraryImport(Tst)]
    public static partial int XTestQueryExtension(nint display, out int eventBase, out int errorBase, out int major, out int minor);

    [LibraryImport(Tst)]
    public static partial int XTestFakeMotionEvent(nint display, int screen, int x, int y, nuint delay);

    [LibraryImport(Tst)]
    public static partial int XTestFakeButtonEvent(nint display, uint button, int isPress, nuint delay);

    [LibraryImport(Tst)]
    public static partial int XTestFakeKeyEvent(nint display, uint keycode, int isPress, nuint delay);
}

internal static partial class Libc
{
    [LibraryImport("libc.so.6", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int setenv(string name, string value, int overwrite);

    [LibraryImport("libc.so.6", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int unsetenv(string name);
}
