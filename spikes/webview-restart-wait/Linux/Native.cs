using System.Runtime.InteropServices;

namespace RestartWait.Linux;

// Hand-written imports for GLib, GObject, GIO, GTK4 and WebKitGTK 6.0 (LP64 Linux x64), checked against the headers of
// WebKitGTK 2.52 and GTK 4.14 (patterns of spikes/linux-wsl-desktop). gboolean = int, GType = nuint, gint64 = long.
// GObjects are nint; "owned" results are released by the caller.

internal static partial class GLib
{
    private const string Lib = "libglib-2.0.so.0";

    [LibraryImport(Lib)]
    public static partial int g_main_context_iteration(nint context, int mayBlock);

    [LibraryImport(Lib)]
    public static unsafe partial uint g_timeout_add_full(int priority, uint interval, delegate* unmanaged<nint, int> function, nint data, nint notify);

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
    public static partial void g_object_unref(nint instance);

    [LibraryImport(Lib)]
    public static unsafe partial nint g_object_new_with_properties(nuint type, uint count, nint* names, GValue* values);

    [LibraryImport(Lib)]
    public static unsafe partial GValue* g_value_init(GValue* value, nuint type);

    [LibraryImport(Lib)]
    public static unsafe partial void g_value_set_object(GValue* value, nint instance);

    [LibraryImport(Lib)]
    public static unsafe partial void g_value_unset(GValue* value);

    /// <summary>g_object_new with one object-valued construct property.</summary>
    public static unsafe nint NewWithObject(nuint type, string name, nuint objectType, nint value)
    {
        var names = stackalloc nint[1];
        var values = stackalloc GValue[1];
        names[0] = Marshal.StringToCoTaskMemUTF8(name);
        values[0] = default;
        g_value_init(&values[0], objectType);
        g_value_set_object(&values[0], value);
        try
        {
            return g_object_new_with_properties(type, 1, names, values);
        }
        finally
        {
            g_value_unset(&values[0]);
            Marshal.FreeCoTaskMem(names[0]);
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
    private const string Lib = "libgio-2.0.so.0";

    [LibraryImport(Lib)]
    public static partial nint g_memory_input_stream_new_from_bytes(nint bytes);

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
}

internal static partial class Gtk
{
    private const string Lib = "libgtk-4.so.1";

    [LibraryImport(Lib)]
    public static partial int gtk_init_check();

    [LibraryImport(Lib)]
    public static partial nint gtk_window_new();

    [LibraryImport(Lib)]
    public static partial void gtk_window_set_default_size(nint window, int width, int height);

    [LibraryImport(Lib)]
    public static partial void gtk_window_set_child(nint window, nint child);

    [LibraryImport(Lib)]
    public static partial void gtk_window_present(nint window);

    [LibraryImport(Lib)]
    public static partial void gtk_window_destroy(nint window);
}

internal static partial class WebKit
{
    private const string Lib = "libwebkitgtk-6.0.so.4";

    public const int LoadCommitted = 2;
    public const int LoadFinished = 3;

    [LibraryImport(Lib)]
    public static partial nuint webkit_web_view_get_type();

    [LibraryImport(Lib)]
    public static partial nuint webkit_network_session_get_type();

    [LibraryImport(Lib)]
    public static partial nint webkit_network_session_get_default();

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint webkit_network_session_new(string? dataDirectory, string? cacheDirectory);

    [LibraryImport(Lib)]
    public static partial nint webkit_network_session_new_ephemeral();

    [LibraryImport(Lib)]
    public static partial nint webkit_web_context_get_default();

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial void webkit_web_context_register_uri_scheme(
        nint context, string scheme, delegate* unmanaged<nint, nint, void> callback, nint userData, nint userDataDestroy);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_web_view_load_uri(nint webView, string uri);

    [LibraryImport(Lib)]
    public static partial nint webkit_uri_scheme_request_get_path(nint request);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_uri_scheme_request_finish(nint request, nint stream, long length, string contentType);
}
