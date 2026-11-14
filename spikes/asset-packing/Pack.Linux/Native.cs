using System.Runtime.InteropServices;

namespace Spar.AssetPacking;

// The subset of the Linux desktop spike's declarations (spikes/linux-desktop/Linux.Desktop/Native.cs) that this host
// needs: GLib, GObject, GIO, libsoup 3, GTK 3 and WebKitGTK 4.1, LP64 Linux x64. Pointers to GObjects are nint.
// Signatures were checked there against the installed headers (WebKitGTK 2.52.6, libsoup 3.4.4).

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

    [LibraryImport(Lib)]
    public static unsafe partial nint g_bytes_new(void* data, nuint size);

    /// <summary>Wraps memory that outlives the bytes object (no copy).</summary>
    [LibraryImport(Lib)]
    public static unsafe partial nint g_bytes_new_static(void* data, nuint size);

    [LibraryImport(Lib)]
    public static partial void g_bytes_unref(nint bytes);
}

internal static partial class GObject
{
    private const string Lib = "libgobject-2.0.so.0";

    public const nuint TypeString = 16 << 2;

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial nuint g_signal_connect_data(nint instance, string signal, void* handler, nint data, nint destroyData, int flags);

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

    public static unsafe nuint Connect(nint instance, string signal, void* handler) =>
        g_signal_connect_data(instance, signal, handler, 0, 0, 0);

    /// <summary>g_object_new_with_properties for string properties and at most one object property.</summary>
    public static unsafe nint New(nuint type, ReadOnlySpan<(string Name, string? Text, nint Object, nuint ObjectType)> properties)
    {
        var names = stackalloc nint[properties.Length];
        var values = stackalloc GValue[properties.Length];
        for (var i = 0; i < properties.Length; i++)
        {
            names[i] = Marshal.StringToCoTaskMemUTF8(properties[i].Name);
            values[i] = default;
            if (properties[i].ObjectType == 0)
            {
                g_value_init(&values[i], TypeString);
                g_value_set_string(&values[i], properties[i].Text);
            }
            else
            {
                g_value_init(&values[i], properties[i].ObjectType);
                g_value_set_object(&values[i], properties[i].Object);
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
    [LibraryImport("libgio-2.0.so.0")]
    public static partial nint g_memory_input_stream_new_from_bytes(nint bytes);
}

internal static partial class Soup
{
    private const string Lib = "libsoup-3.0.so.0";

    public const int MessageHeadersResponse = 1;

    [LibraryImport(Lib)]
    public static partial nint soup_message_headers_new(int type);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void soup_message_headers_append(nint headers, string name, string value);
}

internal static partial class Gtk
{
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
    public static partial void gtk_window_set_default_size(nint window, int width, int height);

    [LibraryImport(Lib)]
    public static partial void gtk_window_close(nint window);
}

internal static partial class WebKit
{
    private const string Lib = "libwebkit2gtk-4.1.so.0";

    public const int LoadStarted = 0;
    public const int LoadFinished = 3;

    [LibraryImport(Lib)]
    public static partial nuint webkit_web_view_get_type();

    [LibraryImport(Lib)]
    public static partial nuint webkit_web_context_get_type();

    [LibraryImport(Lib)]
    public static partial nuint webkit_website_data_manager_get_type();

    [LibraryImport(Lib)]
    public static partial nint webkit_web_context_new_with_website_data_manager(nint manager);

    [LibraryImport(Lib)]
    public static partial void webkit_web_context_set_sandbox_enabled(nint context, int enabled);

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

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_web_view_load_uri(nint webView, string uri);

    [LibraryImport(Lib)]
    public static partial nint webkit_uri_scheme_request_get_path(nint request);

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
