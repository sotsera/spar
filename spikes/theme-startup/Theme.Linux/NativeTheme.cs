using System.Runtime.InteropServices;

namespace Theme.Linux;

// Imports added for the theme spike (signatures checked against the installed GTK 3.24.41, GTK 4.14.5,
// WebKitGTK 2.52.6 and GLib/GIO headers in the container).

/// <summary>GdkRGBA: four doubles in GTK3, four floats in GTK4.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GdkRgba
{
#if SPAR_GTK4
    public float Red;
    public float Green;
    public float Blue;
    public float Alpha;
#else
    public double Red;
    public double Green;
    public double Blue;
    public double Alpha;
#endif

    public static GdkRgba FromRgb(uint rgb) => new()
    {
        Red = (rgb >> 16 & 0xff) / 255f,
        Green = (rgb >> 8 & 0xff) / 255f,
        Blue = (rgb & 0xff) / 255f,
        Alpha = 1,
    };
}

internal static partial class Gtk
{
    public const uint StyleProviderPriorityApplication = 600;

    /// <summary>Borrowed.</summary>
    [LibraryImport(Lib)]
    public static partial nint gtk_settings_get_default();

    [LibraryImport(Lib)]
    public static partial nint gtk_css_provider_new();

    [LibraryImport(Lib)]
    public static partial nint gtk_header_bar_new();

    [LibraryImport(Lib)]
    public static partial void gtk_widget_queue_draw(nint widget);

    [LibraryImport(Lib)]
    public static partial void gtk_window_set_titlebar(nint window, nint titlebar);

#if SPAR_GTK4
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void gtk_css_provider_load_from_string(nint provider, string css);

    [LibraryImport(Lib)]
    public static partial void gtk_style_context_add_provider_for_display(nint display, nint provider, uint priority);

    /// <summary>Borrowed.</summary>
    [LibraryImport(Lib)]
    public static partial nint gdk_display_get_default();

    [LibraryImport(Lib)]
    public static partial void gtk_widget_set_visible(nint widget, int visible);
#else
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int gtk_css_provider_load_from_data(nint provider, string css, nint length, out nint error);

    [LibraryImport(Lib)]
    public static partial void gtk_style_context_add_provider_for_screen(nint screen, nint provider, uint priority);

    /// <summary>Borrowed.</summary>
    [LibraryImport("libgdk-3.so.0")]
    public static partial nint gdk_screen_get_default();

    [LibraryImport(Lib)]
    public static partial void gtk_widget_show(nint widget);

    [LibraryImport(Lib)]
    public static partial void gtk_window_move(nint window, int x, int y);

    [LibraryImport(Lib)]
    public static partial void gtk_header_bar_set_show_close_button(nint bar, int show);
#endif
}

internal static partial class GObject
{
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial void g_object_set_property(nint instance, string name, GValue* value);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial void g_object_get_property(nint instance, string name, GValue* value);

    [LibraryImport(Lib)]
    public static unsafe partial int g_value_get_boolean(GValue* value);

    /// <summary>Borrowed.</summary>
    [LibraryImport(Lib)]
    public static unsafe partial nint g_value_get_string(GValue* value);

    public static unsafe void SetBoolean(nint instance, string name, bool value)
    {
        var gvalue = default(GValue);
        g_value_init(&gvalue, TypeBoolean);
        g_value_set_boolean(&gvalue, value ? 1 : 0);
        g_object_set_property(instance, name, &gvalue);
        g_value_unset(&gvalue);
    }

    public static unsafe void SetString(nint instance, string name, string value)
    {
        var gvalue = default(GValue);
        g_value_init(&gvalue, TypeString);
        g_value_set_string(&gvalue, value);
        g_object_set_property(instance, name, &gvalue);
        g_value_unset(&gvalue);
    }

    public static unsafe bool GetBoolean(nint instance, string name)
    {
        var gvalue = default(GValue);
        g_value_init(&gvalue, TypeBoolean);
        g_object_get_property(instance, name, &gvalue);
        var result = g_value_get_boolean(&gvalue) != 0;
        g_value_unset(&gvalue);
        return result;
    }

    public static unsafe string? GetString(nint instance, string name)
    {
        var gvalue = default(GValue);
        g_value_init(&gvalue, TypeString);
        g_object_get_property(instance, name, &gvalue);
        var result = Marshal.PtrToStringUTF8(g_value_get_string(&gvalue));
        g_value_unset(&gvalue);
        return result;
    }
}

internal static partial class WebKit
{
    [LibraryImport(Lib)]
    public static partial void webkit_web_view_set_background_color(nint webView, in GdkRgba color);
}

internal static partial class GLib
{
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint g_variant_new_string(string text);

    [LibraryImport(Lib)]
    public static unsafe partial nint g_variant_new_tuple(nint* children, nuint count);

    [LibraryImport(Lib)]
    public static partial nint g_variant_get_child_value(nint value, nuint index);

    [LibraryImport(Lib)]
    public static partial nint g_variant_get_variant(nint value);

    [LibraryImport(Lib)]
    public static partial uint g_variant_get_uint32(nint value);

    /// <summary>Borrowed.</summary>
    [LibraryImport(Lib)]
    public static partial nint g_variant_get_type_string(nint value);

    /// <summary>Owned (g_free).</summary>
    [LibraryImport(Lib)]
    public static partial nint g_variant_print(nint value, int typeAnnotate);

    [LibraryImport(Lib)]
    public static partial void g_variant_unref(nint value);
}

internal static partial class Gio
{
    public const int BusTypeSession = 2;

    [LibraryImport(Lib)]
    public static partial nint g_bus_get_sync(int busType, nint cancellable, out nint error);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint g_dbus_connection_call_sync(
        nint connection, string busName, string objectPath, string interfaceName, string methodName,
        nint parameters, nint replyType, int flags, int timeoutMs, nint cancellable, out nint error);
}

internal static partial class LibC
{
    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint getenv(string name);
}
