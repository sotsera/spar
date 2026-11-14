using System.Runtime.InteropServices;

namespace Wsl.Linux;

// Imports added for the WSLg desktop spike: window and monitor facts, focus, the X11 window id, hardware acceleration,
// web-process termination and dlopen for the start-up library check. Signatures checked against the installed GTK
// 3.24.41, GTK 4.14.5 and WebKitGTK 2.52.6 headers in the WSL distribution.

/// <summary>GdkRectangle: four ints.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GdkRectangle
{
    public int X;
    public int Y;
    public int Width;
    public int Height;
}

internal static partial class GObject
{
    /// <summary>Borrowed.</summary>
    [LibraryImport(Lib)]
    public static partial nint g_type_name(nuint type);

    /// <summary>G_OBJECT_TYPE_NAME: GTypeInstance { GTypeClass *g_class } and GTypeClass { GType g_type }.</summary>
    public static unsafe string TypeName(nint instance)
    {
        if (instance == 0)
        {
            return "(null)";
        }

        var typeClass = *(nint*)instance;
        var type = *(nuint*)typeClass;
        return Marshal.PtrToStringUTF8(g_type_name(type)) ?? "?";
    }
}

internal static partial class Gtk
{
    [LibraryImport(Lib)]
    public static partial int gtk_widget_get_scale_factor(nint widget);

    [LibraryImport(Lib)]
    public static partial int gtk_window_is_active(nint window);

    [LibraryImport(Lib)]
    public static partial int gtk_window_get_decorated(nint window);

    [LibraryImport(Lib)]
    public static partial void gtk_window_maximize(nint window);

    [LibraryImport(Lib)]
    public static partial void gtk_window_unmaximize(nint window);

    /// <summary>Borrowed.</summary>
    [LibraryImport(Lib)]
    public static partial nint gtk_widget_get_display(nint widget);

#if SPAR_GTK4
    [LibraryImport(Lib)]
    public static partial int gdk_monitor_get_scale_factor(nint monitor);

    [LibraryImport(Lib)]
    public static partial void gdk_monitor_get_geometry(nint monitor, out GdkRectangle geometry);

    [LibraryImport(Lib)]
    public static partial int gdk_monitor_get_refresh_rate(nint monitor);

    /// <summary>Borrowed.</summary>
    [LibraryImport(Lib)]
    public static partial nint gtk_native_get_surface(nint native);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int gtk_widget_has_css_class(nint widget, string cssClass);

    [LibraryImport(Lib)]
    public static partial int gtk_widget_get_width(nint widget);

    [LibraryImport(Lib)]
    public static partial int gtk_widget_get_height(nint widget);

    /// <summary>Borrowed.</summary>
    [LibraryImport(Lib)]
    public static partial nint gdk_display_get_monitor_at_surface(nint display, nint surface);

    [LibraryImport(Lib)]
    public static partial double gdk_surface_get_scale(nint surface);

    [LibraryImport(Lib)]
    public static partial double gdk_monitor_get_scale(nint monitor);

    /// <summary>X11 backend only.</summary>
    [LibraryImport(Lib)]
    public static partial nuint gdk_x11_surface_get_xid(nint surface);
#else
    private const string GdkLib = "libgdk-3.so.0";

    [LibraryImport(GdkLib)]
    public static partial int gdk_monitor_get_scale_factor(nint monitor);

    [LibraryImport(GdkLib)]
    public static partial void gdk_monitor_get_geometry(nint monitor, out GdkRectangle geometry);

    [LibraryImport(GdkLib)]
    public static partial int gdk_monitor_get_refresh_rate(nint monitor);

    /// <summary>Borrowed GdkWindow.</summary>
    [LibraryImport(Lib)]
    public static partial nint gtk_widget_get_window(nint widget);

    /// <summary>Borrowed.</summary>
    [LibraryImport(Lib)]
    public static partial nint gtk_widget_get_style_context(nint widget);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int gtk_style_context_has_class(nint context, string cssClass);

    [LibraryImport(Lib)]
    public static partial int gtk_widget_get_allocated_width(nint widget);

    [LibraryImport(Lib)]
    public static partial int gtk_widget_get_allocated_height(nint widget);

    [LibraryImport(Lib)]
    public static partial int gtk_window_has_toplevel_focus(nint window);

    /// <summary>Borrowed.</summary>
    [LibraryImport(GdkLib)]
    public static partial nint gdk_display_get_default();

    /// <summary>Borrowed.</summary>
    [LibraryImport(GdkLib)]
    public static partial nint gdk_display_get_monitor_at_window(nint display, nint window);

    /// <summary>X11 backend only.</summary>
    [LibraryImport(GdkLib)]
    public static partial nuint gdk_x11_window_get_xid(nint window);
#endif
}

internal static partial class WebKit
{
    [LibraryImport(Lib)]
    public static partial void webkit_settings_set_hardware_acceleration_policy(nint settings, int policy);

    [LibraryImport(Lib)]
    public static partial int webkit_settings_get_hardware_acceleration_policy(nint settings);

    /// <summary>The policy values differ: 4.1 has ON_DEMAND (0), ALWAYS (1), NEVER (2); 6.0 has ALWAYS (0), NEVER (1).</summary>
    public static int AccelerationPolicy(string name) => name switch
    {
#if SPAR_GTK4
        "always" => 0,
        "never" => 1,
#else
        "ondemand" => 0,
        "always" => 1,
        "never" => 2,
#endif
        _ => throw new ArgumentException("Unknown acceleration policy " + name, nameof(name)),
    };

    public static string AccelerationPolicyName(int value) => value switch
    {
#if SPAR_GTK4
        0 => "always",
        1 => "never",
#else
        0 => "ondemand",
        1 => "always",
        2 => "never",
#endif
        _ => "unknown-" + value,
    };
}

internal static partial class Gio
{
    /// <summary>Callback: (connection, sender, objectPath, interfaceName, signalName, parameters, userData).</summary>
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static unsafe partial uint g_dbus_connection_signal_subscribe(
        nint connection, string? sender, string? interfaceName, string? member, string? objectPath, string? arg0, int flags,
        delegate* unmanaged<nint, nint, nint, nint, nint, nint, nint, void> callback, nint userData, nint userDataFree);
}

internal static partial class GLib
{
    /// <summary>Borrowed.</summary>
    [LibraryImport(Lib)]
    public static partial nint g_variant_get_string(nint value, nint length);
}

internal static partial class LibC
{
    public const int RtldNow = 2;

    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint dlopen(string fileName, int flags);

    /// <summary>Borrowed; valid until the next dl* call on this thread.</summary>
    [LibraryImport("libc")]
    public static partial nint dlerror();
}

internal static partial class Gtk
{
    [LibraryImport(Lib)]
    public static partial void gtk_widget_grab_focus(nint widget);
}
