using System.Runtime.InteropServices;

namespace Integ.Linux;

// Source-generated imports for GLib, GObject, GIO, GTK/GDK, WebKitGTK and libX11 (LP64 Linux x64).
// C types: gboolean = int, gint = int, guint = uint, guint32 = uint, gulong = nuint, gsize/GType = nuint, gdouble = double.
// GObject pointers are nint. "Owned" results are released by us; "borrowed" ones are not.
// Signatures were compared with the installed headers (GLib 2.80, GTK 3.24.41 and 4.14.5, WebKitGTK 2.52.6).

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
    public static partial void g_free(nint memory);

    [LibraryImport(Lib)]
    public static partial void g_strfreev(nint strings);

    [LibraryImport(Lib)]
    public static partial void g_error_free(nint error);

    [LibraryImport(Lib)]
    public static unsafe partial nint g_bytes_new(void* data, nuint size);

    [LibraryImport(Lib)]
    public static partial void g_bytes_unref(nint bytes);

    // GVariant. A GVariantType* is its type string (G_VARIANT_TYPE is a checked cast), so types are passed as strings.

    /// <summary>Owned, non-floating, or 0 with an owned GError.</summary>
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint g_variant_parse(string? type, string text, nint limit, nint endPointer, out nint error);

    /// <summary>Owned string (g_free).</summary>
    [LibraryImport(Lib)]
    public static partial nint g_variant_print(nint value, int typeAnnotate);

    [LibraryImport(Lib)]
    public static partial void g_variant_unref(nint value);

    [LibraryImport(Lib)]
    public static partial nint g_variant_ref_sink(nint value);

    /// <summary>Owned child.</summary>
    [LibraryImport(Lib)]
    public static partial nint g_variant_get_child_value(nint value, nuint index);

    [LibraryImport(Lib)]
    public static partial nuint g_variant_n_children(nint value);

    [LibraryImport(Lib)]
    public static partial int g_variant_get_int32(nint value);

    [LibraryImport(Lib)]
    public static partial uint g_variant_get_uint32(nint value);

    /// <summary>Borrowed string.</summary>
    [LibraryImport(Lib)]
    public static partial nint g_variant_get_string(nint value, nint length);

    /// <summary>Owned inner value.</summary>
    [LibraryImport(Lib)]
    public static partial nint g_variant_get_variant(nint value);

    /// <summary>Borrowed type string.</summary>
    [LibraryImport(Lib)]
    public static partial nint g_variant_get_type_string(nint value);

    /// <summary>Reads GError.message (struct GError { GQuark domain; gint code; gchar *message; }: offset 8 on LP64).</summary>
    public static unsafe string ErrorMessage(nint error) =>
        error == 0 ? "(no error)" : Marshal.PtrToStringUTF8(*(nint*)((byte*)error + 8)) ?? "(null)";

    /// <summary>Reads GError.code (offset 4).</summary>
    public static unsafe int ErrorCode(nint error) => error == 0 ? 0 : *(int*)((byte*)error + 4);

    /// <summary>Returns the message and frees the error.</summary>
    public static string TakeError(nint error)
    {
        var message = ErrorMessage(error);
        if (error != 0)
        {
            g_error_free(error);
        }

        return message;
    }

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

    /// <summary>Parses GVariant text; throws on a syntax error (a spike bug, never input).</summary>
    public static nint Parse(string? type, string text)
    {
        var value = g_variant_parse(type, text, 0, 0, out var error);
        return value != 0 ? value : throw new InvalidOperationException($"g_variant_parse({type}) failed: {TakeError(error)} in {text}");
    }

    public static string Print(nint value) => TakeString(g_variant_print(value, 0)) ?? "";

    public static string ChildString(nint tuple, int index)
    {
        var child = g_variant_get_child_value(tuple, (nuint)index);
        try
        {
            return Marshal.PtrToStringUTF8(g_variant_get_string(child, 0)) ?? "";
        }
        finally
        {
            g_variant_unref(child);
        }
    }

    public static int ChildInt32(nint tuple, int index)
    {
        var child = g_variant_get_child_value(tuple, (nuint)index);
        try
        {
            return g_variant_get_int32(child);
        }
        finally
        {
            g_variant_unref(child);
        }
    }

    public static uint ChildUInt32(nint tuple, int index)
    {
        var child = g_variant_get_child_value(tuple, (nuint)index);
        try
        {
            return g_variant_get_uint32(child);
        }
        finally
        {
            g_variant_unref(child);
        }
    }

    /// <summary>GVariant text for a string literal.</summary>
    public static string Quote(string text) =>
        "'" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal) + "'";
}

internal static partial class GObject
{
    private const string Lib = "libgobject-2.0.so.0";

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
    public static partial nint g_object_ref_sink(nint instance);

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

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial void g_object_set_property(nint instance, string name, GValue* value);

    /// <summary>Borrowed.</summary>
    [LibraryImport(Lib)]
    public static partial nint g_type_name_from_instance(nint instance);

    public static string TypeName(nint instance) => instance == 0 ? "(null)" : Marshal.PtrToStringUTF8(g_type_name_from_instance(instance)) ?? "?";

    public static unsafe nuint Connect(nint instance, string signal, void* handler, nint data = 0) =>
        g_signal_connect_data(instance, signal, handler, data, 0, 0);

    /// <summary>g_object_new_with_properties for string and object properties.</summary>
    public static unsafe nint New(nuint type, params ReadOnlySpan<GProperty> properties)
    {
        var names = stackalloc nint[properties.Length];
        var values = stackalloc GValue[properties.Length];
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

/// <summary>One construct property: a string, or a GObject of <paramref name="ObjectType"/>.</summary>
internal readonly record struct GProperty(string Name, string? Text, nint Instance = 0, nuint ObjectType = 0);

/// <summary>struct GValue { GType g_type; union { … } data[2]; }: 24 bytes on LP64.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GValue
{
    public nuint Type;
    public ulong Data0;
    public ulong Data1;
}

/// <summary>struct GDBusInterfaceVTable { method_call; get_property; set_property; gpointer padding[8]; }.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct GDBusInterfaceVTable
{
    public delegate* unmanaged<nint, nint, nint, nint, nint, nint, nint, nint, void> MethodCall;
    public delegate* unmanaged<nint, nint, nint, nint, nint, nint*, nint, nint> GetProperty;
    public nint SetProperty;
    public fixed long Padding[8];
}

internal static partial class Gio
{
    private const string Lib = "libgio-2.0.so.0";

    public const int BusTypeSession = 2;

    [LibraryImport(Lib)]
    public static partial nint g_memory_input_stream_new_from_bytes(nint bytes);

    [LibraryImport(Lib)]
    public static partial nint g_bus_get_sync(int busType, nint cancellable, out nint error);

    /// <summary>Borrowed.</summary>
    [LibraryImport(Lib)]
    public static partial nint g_dbus_connection_get_unique_name(nint connection);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint g_dbus_node_info_new_for_xml(string xml, out nint error);

    /// <summary>Borrowed.</summary>
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint g_dbus_node_info_lookup_interface(nint info, string name);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial uint g_dbus_connection_register_object(
        nint connection, string path, nint interfaceInfo, GDBusInterfaceVTable* vtable, nint data, nint freeData, out nint error);

    [LibraryImport(Lib)]
    public static partial int g_dbus_connection_unregister_object(nint connection, uint id);

    /// <summary>Consumes a floating <paramref name="parameters"/>.</summary>
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int g_dbus_connection_emit_signal(
        nint connection, string? destination, string path, string iface, string signal, nint parameters, out nint error);

    /// <summary>Owned reply, or 0 with an owned GError. Consumes a floating <paramref name="parameters"/>.</summary>
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint g_dbus_connection_call_sync(
        nint connection, string? busName, string path, string iface, string method, nint parameters, string? replyType,
        int flags, int timeoutMs, nint cancellable, out nint error);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial void g_dbus_connection_call(
        nint connection, string? busName, string path, string iface, string method, nint parameters, string? replyType,
        int flags, int timeoutMs, nint cancellable, delegate* unmanaged<nint, nint, nint, void> callback, nint data);

    [LibraryImport(Lib)]
    public static partial nint g_dbus_connection_call_finish(nint connection, nint result, out nint error);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial uint g_dbus_connection_signal_subscribe(
        nint connection, string? sender, string? iface, string? member, string? path, string? arg0, int flags,
        delegate* unmanaged<nint, nint, nint, nint, nint, nint, nint, void> callback, nint data, nint freeData);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial uint g_bus_watch_name_on_connection(
        nint connection, string name, int flags, delegate* unmanaged<nint, nint, nint, nint, void> appeared,
        delegate* unmanaged<nint, nint, nint, void> vanished, nint data, nint freeData);

    [LibraryImport(Lib)]
    public static partial void g_bus_unwatch_name(uint id);

    [LibraryImport(Lib)]
    public static partial void g_dbus_method_invocation_return_value(nint invocation, nint parameters);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void g_dbus_method_invocation_return_dbus_error(nint invocation, string name, string message);

    [LibraryImport(Lib)]
    public static partial int g_dbus_connection_flush_sync(nint connection, nint cancellable, out nint error);

    [LibraryImport(Lib)]
    public static partial nint g_cancellable_new();

    [LibraryImport(Lib)]
    public static partial void g_cancellable_cancel(nint cancellable);

    /// <summary>Owned string (g_free).</summary>
    [LibraryImport(Lib)]
    public static partial nint g_file_get_path(nint file);

    // GApplication (single instance).

    public const int ApplicationHandlesCommandLine = 1 << 3;

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint g_application_new(string id, int flags);

    [LibraryImport(Lib)]
    public static partial int g_application_run(nint application, int argc, nint argv);

    [LibraryImport(Lib)]
    public static partial void g_application_hold(nint application);

    [LibraryImport(Lib)]
    public static partial void g_application_release(nint application);

    [LibraryImport(Lib)]
    public static partial void g_application_quit(nint application);

    [LibraryImport(Lib)]
    public static partial int g_application_get_is_remote(nint application);

    /// <summary>Owned string array (g_strfreev).</summary>
    [LibraryImport(Lib)]
    public static partial nint g_application_command_line_get_arguments(nint commandLine, out int argc);

    [LibraryImport(Lib)]
    public static partial int g_application_command_line_get_is_remote(nint commandLine);

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

internal static partial class Soup
{
    private const string Lib = "libsoup-3.0.so.0";

    [LibraryImport(Lib)]
    public static partial nint soup_message_headers_new(int type);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void soup_message_headers_append(nint headers, string name, string value);
}

/// <summary>GdkRectangle.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GdkRectangle
{
    public int X;
    public int Y;
    public int Width;
    public int Height;
}

/// <summary>GdkRGBA (four doubles).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GdkRgba
{
    public double Red;
    public double Green;
    public double Blue;
    public double Alpha;
}

/// <summary>graphene_point_t (two floats).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GraphenePoint
{
    public float X;
    public float Y;
}

internal static partial class Gtk
{
#if SPAR_GTK4
    private const string Lib = "libgtk-4.so.1";

    public const int EventButtonPress = 2;
    public const int EventButtonRelease = 3;
    public const int EventMotion = 1;

    public const int StateMinimized = 1 << 0;
    public const int StateMaximized = 1 << 1;
    public const int StateFullscreen = 1 << 3;
    public const int StateFocused = 1 << 6;

    [LibraryImport(Lib)]
    public static partial int gtk_init_check();

    [LibraryImport(Lib)]
    public static partial nint gtk_window_new();

    [LibraryImport(Lib)]
    public static partial void gtk_window_set_child(nint window, nint child);

    [LibraryImport(Lib)]
    public static partial void gtk_window_destroy(nint window);

    [LibraryImport(Lib)]
    public static partial void gtk_widget_set_visible(nint widget, int visible);

    [LibraryImport(Lib)]
    public static partial void gtk_window_minimize(nint window);

    [LibraryImport(Lib)]
    public static partial void gtk_window_unminimize(nint window);

    [LibraryImport(Lib)]
    public static partial void gtk_window_get_default_size(nint window, out int width, out int height);

    /// <summary>Borrowed GdkSurface (a GdkToplevel for a window).</summary>
    [LibraryImport(Lib)]
    public static partial nint gtk_native_get_surface(nint native);

    [LibraryImport(Lib)]
    public static partial void gtk_native_get_surface_transform(nint native, out double x, out double y);

    [LibraryImport(Lib)]
    public static unsafe partial int gtk_widget_compute_point(nint widget, nint target, GraphenePoint* point, GraphenePoint* outPoint);

    [LibraryImport(Lib)]
    public static partial int gtk_widget_get_width(nint widget);

    [LibraryImport(Lib)]
    public static partial int gtk_widget_get_height(nint widget);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int gtk_widget_has_css_class(nint widget, string cssClass);

    [LibraryImport(Lib)]
    public static partial void gdk_toplevel_begin_move(nint toplevel, nint device, int button, double x, double y, uint timestamp);

    [LibraryImport(Lib)]
    public static partial void gdk_toplevel_begin_resize(nint toplevel, int edge, nint device, int button, double x, double y, uint timestamp);

    [LibraryImport(Lib)]
    public static partial int gdk_toplevel_show_window_menu(nint toplevel, nint gdkEvent);

    [LibraryImport(Lib)]
    public static partial int gdk_toplevel_get_state(nint toplevel);

    [LibraryImport(Lib)]
    public static partial nint gtk_event_controller_legacy_new();

    [LibraryImport(Lib)]
    public static partial void gtk_event_controller_set_propagation_phase(nint controller, int phase);

    [LibraryImport(Lib)]
    public static partial void gtk_widget_add_controller(nint widget, nint controller);

    [LibraryImport(Lib)]
    public static partial int gdk_event_get_event_type(nint gdkEvent);

    [LibraryImport(Lib)]
    public static partial uint gdk_event_get_time(nint gdkEvent);

    [LibraryImport(Lib)]
    public static partial uint gdk_button_event_get_button(nint gdkEvent);

    [LibraryImport(Lib)]
    public static partial int gdk_event_get_position(nint gdkEvent, out double x, out double y);

    /// <summary>Borrowed.</summary>
    [LibraryImport(Lib)]
    public static partial nint gdk_event_get_device(nint gdkEvent);

    [LibraryImport(Lib)]
    public static partial nint gdk_event_ref(nint gdkEvent);

    [LibraryImport(Lib)]
    public static partial void gdk_event_unref(nint gdkEvent);

    [LibraryImport(Lib)]
    public static partial nint gdk_display_get_clipboard(nint display);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void gdk_clipboard_set_text(nint clipboard, string text);

    [LibraryImport(Lib)]
    public static unsafe partial void gdk_clipboard_read_text_async(nint clipboard, nint cancellable, delegate* unmanaged<nint, nint, nint, void> callback, nint data);

    [LibraryImport(Lib)]
    public static partial nint gdk_clipboard_read_text_finish(nint clipboard, nint result, out nint error);

    [LibraryImport(Lib)]
    public static partial nint gtk_file_dialog_new();

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void gtk_file_dialog_set_title(nint dialog, string title);

    [LibraryImport(Lib)]
    public static unsafe partial void gtk_file_dialog_open(nint dialog, nint parent, nint cancellable, delegate* unmanaged<nint, nint, nint, void> callback, nint data);

    [LibraryImport(Lib)]
    public static partial nint gtk_file_dialog_open_finish(nint dialog, nint result, out nint error);

    [LibraryImport(Lib)]
    public static partial nuint gdk_x11_surface_get_xid(nint surface);

    [LibraryImport(Lib)]
    public static partial nint gtk_box_new(int orientation, int spacing);

    [LibraryImport(Lib)]
    public static partial nint gtk_css_provider_new();

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void gtk_css_provider_load_from_string(nint provider, string css);

    [LibraryImport(Lib)]
    public static partial void gtk_style_context_add_provider_for_display(nint display, nint provider, uint priority);
#else
    private const string Lib = "libgtk-3.so.0";

    public const int EventButtonPress = 4;
    public const int EventDoubleButtonPress = 5;
    public const int EventButtonRelease = 7;

    public const int StateIconified = 1 << 1;
    public const int StateMaximized = 1 << 2;
    public const int StateFullscreen = 1 << 4;
    public const int StateFocused = 1 << 7;
    public const int StateWithdrawn = 1 << 0;

    [LibraryImport(Lib)]
    public static partial int gtk_init_check(nint argc, nint argv);

    [LibraryImport(Lib)]
    public static partial nint gtk_window_new(int type);

    [LibraryImport(Lib)]
    public static partial void gtk_container_add(nint container, nint widget);

    [LibraryImport(Lib)]
    public static partial void gtk_widget_show(nint widget);

    [LibraryImport(Lib)]
    public static partial void gtk_widget_hide(nint widget);

    [LibraryImport(Lib)]
    public static partial void gtk_widget_destroy(nint widget);

    [LibraryImport(Lib)]
    public static partial void gtk_window_iconify(nint window);

    [LibraryImport(Lib)]
    public static partial void gtk_window_deiconify(nint window);

    [LibraryImport(Lib)]
    public static partial void gtk_window_get_size(nint window, out int width, out int height);

    [LibraryImport(Lib)]
    public static partial void gtk_window_get_position(nint window, out int x, out int y);

    [LibraryImport(Lib)]
    public static partial void gtk_window_move(nint window, int x, int y);

    [LibraryImport(Lib)]
    public static partial void gtk_window_resize(nint window, int width, int height);

    [LibraryImport(Lib)]
    public static partial void gtk_window_begin_move_drag(nint window, int button, int rootX, int rootY, uint timestamp);

    [LibraryImport(Lib)]
    public static partial void gtk_window_begin_resize_drag(nint window, int edge, int button, int rootX, int rootY, uint timestamp);

    /// <summary>Borrowed GdkWindow.</summary>
    [LibraryImport(Lib)]
    public static partial nint gtk_widget_get_window(nint widget);

    [LibraryImport(Lib)]
    public static partial int gdk_window_get_state(nint window);

    [LibraryImport(Lib)]
    public static partial int gdk_window_show_window_menu(nint window, nint gdkEvent);

    [LibraryImport(Lib)]
    public static partial nint gdk_event_copy(nint gdkEvent);

    [LibraryImport(Lib)]
    public static partial void gdk_event_free(nint gdkEvent);

    [LibraryImport(Lib)]
    public static partial int gdk_event_get_event_type(nint gdkEvent);

    [LibraryImport(Lib)]
    public static partial uint gdk_event_get_time(nint gdkEvent);

    [LibraryImport(Lib)]
    public static partial int gdk_event_get_button(nint gdkEvent, out uint button);

    [LibraryImport(Lib)]
    public static partial int gdk_event_get_root_coords(nint gdkEvent, out double x, out double y);

    [LibraryImport(Lib)]
    public static partial int gdk_event_get_coords(nint gdkEvent, out double x, out double y);

    /// <summary>Borrowed.</summary>
    [LibraryImport(Lib)]
    public static partial nint gdk_event_get_device(nint gdkEvent);

    [LibraryImport(Lib)]
    public static partial int gdk_event_get_click_count(nint gdkEvent, out uint count);

    [LibraryImport(Lib)]
    public static partial int gtk_widget_translate_coordinates(nint source, nint destination, int x, int y, out int destX, out int destY);

    [LibraryImport(Lib)]
    public static partial int gtk_widget_get_allocated_width(nint widget);

    [LibraryImport(Lib)]
    public static partial int gtk_widget_get_allocated_height(nint widget);

    [LibraryImport(Lib)]
    public static partial nint gtk_widget_get_style_context(nint widget);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int gtk_style_context_has_class(nint context, string cssClass);

    [LibraryImport(Lib)]
    public static partial nint gdk_screen_get_default();

    [LibraryImport(Lib)]
    public static partial int gdk_screen_is_composited(nint screen);

    [LibraryImport(Lib)]
    public static partial nint gtk_clipboard_get(nint selection);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void gtk_clipboard_set_text(nint clipboard, string text, int length);

    [LibraryImport(Lib)]
    public static unsafe partial void gtk_clipboard_request_text(nint clipboard, delegate* unmanaged<nint, nint, nint, void> callback, nint data);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint gtk_file_chooser_native_new(string title, nint parent, int action, string? accept, string? cancel);

    [LibraryImport(Lib)]
    public static partial void gtk_native_dialog_show(nint dialog);

    [LibraryImport(Lib)]
    public static partial void gtk_native_dialog_hide(nint dialog);

    [LibraryImport(Lib)]
    public static partial void gtk_native_dialog_destroy(nint dialog);

    [LibraryImport(Lib)]
    public static partial nuint gdk_x11_window_get_xid(nint window);

    [LibraryImport(Lib)]
    public static partial nint gtk_box_new(int orientation, int spacing);

    [LibraryImport(Lib)]
    public static partial nint gtk_css_provider_new();

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int gtk_css_provider_load_from_data(nint provider, string css, nint length, out nint error);

    [LibraryImport(Lib)]
    public static partial void gtk_style_context_add_provider_for_screen(nint screen, nint provider, uint priority);
#endif

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void gtk_window_set_title(nint window, string title);

    [LibraryImport(Lib)]
    public static partial void gtk_window_set_default_size(nint window, int width, int height);

    [LibraryImport(Lib)]
    public static partial void gtk_window_present(nint window);

    [LibraryImport(Lib)]
    public static partial void gtk_window_close(nint window);

    [LibraryImport(Lib)]
    public static partial void gtk_window_set_decorated(nint window, int decorated);

    [LibraryImport(Lib)]
    public static partial void gtk_window_set_titlebar(nint window, nint titlebar);

    [LibraryImport(Lib)]
    public static partial void gtk_window_maximize(nint window);

    [LibraryImport(Lib)]
    public static partial void gtk_window_unmaximize(nint window);

    [LibraryImport(Lib)]
    public static partial int gtk_window_is_maximized(nint window);

    [LibraryImport(Lib)]
    public static partial int gtk_window_is_active(nint window);

    [LibraryImport(Lib)]
    public static partial int gtk_widget_get_visible(nint widget);

    [LibraryImport(Lib)]
    public static partial int gtk_widget_get_mapped(nint widget);

    [LibraryImport(Lib)]
    public static partial nint gtk_widget_get_display(nint widget);

    [LibraryImport(Lib)]
    public static partial nint gdk_display_get_default();

    [LibraryImport(Lib)]
    public static partial nint gdk_x11_display_get_xdisplay(nint display);

    [LibraryImport(Lib)]
    public static partial int gtk_widget_grab_focus(nint widget);

    [LibraryImport(Lib)]
    public static partial void gtk_widget_unrealize(nint widget);

    [LibraryImport(Lib)]
    public static partial nint gtk_print_settings_new();

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void gtk_print_settings_set(nint settings, string key, string value);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void gtk_print_settings_set_printer(nint settings, string printer);
}

/// <summary>libX11, used only on the X11 backend for the window's root position and the GTK4 move workaround.</summary>
internal static partial class X11
{
    private const string Lib = "libX11.so.6";

    [LibraryImport(Lib)]
    public static partial nuint XDefaultRootWindow(nint display);

    [LibraryImport(Lib)]
    public static partial int XTranslateCoordinates(nint display, nuint source, nuint destination, int x, int y, out int destX, out int destY, out nuint child);

    [LibraryImport(Lib)]
    public static partial int XMoveWindow(nint display, nuint window, int x, int y);

    [LibraryImport(Lib)]
    public static partial int XFlush(nint display);
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
}

internal static partial class WebKit
{
#if SPAR_GTK4
    private const string Lib = "libwebkitgtk-6.0.so.4";
#else
    private const string Lib = "libwebkit2gtk-4.1.so.0";
#endif

    public const int InjectTopFrame = 1;
    public const int InjectAtDocumentStart = 0;
#if SPAR_GTK4
    public const int AccelerationNever = 1; // 6.0: ALWAYS 0, NEVER 1
#else
    public const int AccelerationNever = 2; // 4.1: ON_DEMAND 0, ALWAYS 1, NEVER 2
#endif

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

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial void webkit_web_context_register_uri_scheme(
        nint context, string scheme, delegate* unmanaged<nint, nint, void> callback, nint data, nint destroy);

    [LibraryImport(Lib)]
    public static partial nint webkit_web_context_get_security_manager(nint context);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_security_manager_register_uri_scheme_as_secure(nint manager, string scheme);

    [LibraryImport(Lib)]
    public static partial nint webkit_web_view_get_user_content_manager(nint webView);

    [LibraryImport(Lib)]
    public static partial nint webkit_web_view_get_settings(nint webView);

    [LibraryImport(Lib)]
    public static partial void webkit_settings_set_hardware_acceleration_policy(nint settings, int policy);

    [LibraryImport(Lib)]
    public static partial void webkit_settings_set_javascript_can_access_clipboard(nint settings, int enabled);

    [LibraryImport(Lib)]
    public static partial void webkit_web_view_set_background_color(nint webView, in GdkRgba color);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint webkit_user_script_new(string source, int injectedFrames, int injectionTime, nint allowList, nint blockList);

    [LibraryImport(Lib)]
    public static partial void webkit_user_script_unref(nint script);

    [LibraryImport(Lib)]
    public static partial void webkit_user_content_manager_add_script(nint manager, nint script);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_web_view_load_uri(nint webView, string uri);

    [LibraryImport(Lib)]
    public static unsafe partial void webkit_web_view_evaluate_javascript(
        nint webView, byte* script, nint length, nint worldName, nint sourceUri, nint cancellable,
        delegate* unmanaged<nint, nint, nint, void> callback, nint data);

    [LibraryImport(Lib)]
    public static partial void webkit_permission_request_allow(nint request);

    [LibraryImport(Lib)]
    public static partial void webkit_permission_request_deny(nint request);

    [LibraryImport(Lib)]
    public static partial nint webkit_print_operation_new(nint webView);

    [LibraryImport(Lib)]
    public static partial void webkit_print_operation_set_print_settings(nint operation, nint settings);

    [LibraryImport(Lib)]
    public static partial void webkit_print_operation_print(nint operation);

    [LibraryImport(Lib)]
    public static partial nint webkit_uri_scheme_request_get_path(nint request);

    [LibraryImport(Lib)]
    public static partial nint webkit_uri_scheme_response_new(nint stream, long length);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_uri_scheme_response_set_status(nint response, uint statusCode, string? reason);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void webkit_uri_scheme_response_set_content_type(nint response, string contentType);

    [LibraryImport(Lib)]
    public static partial void webkit_uri_scheme_response_set_http_headers(nint response, nint headers);

    [LibraryImport(Lib)]
    public static partial void webkit_uri_scheme_request_finish_with_response(nint request, nint response);
}
