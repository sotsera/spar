using System.Runtime.InteropServices;

namespace Spar.Linux.Interop;

/// <summary>
/// GTK 4, as the host uses it (checked against the 4.14 headers of Ubuntu 24.04). Every call runs on the main-loop
/// thread that called <see cref="gtk_init_check"/>. A window from <see cref="gtk_window_new"/> belongs to GTK's list of
/// toplevels; <see cref="gtk_window_destroy"/> ends it.
/// </summary>
internal static unsafe partial class Gtk
{
    private const string Library = "libgtk-4.so.1";

    /// <summary><c>GTK_STYLE_PROVIDER_PRIORITY_APPLICATION</c>: above the theme, below the user's own CSS.</summary>
    public const uint StyleProviderPriorityApplication = 600;

    /// <summary>Opens the display named by the environment; false when there is none.</summary>
    [LibraryImport(Library)]
    public static partial int gtk_init_check();

    /// <summary>The loaded GTK's version: callable before <see cref="gtk_init_check"/>.</summary>
    [LibraryImport(Library)]
    public static partial uint gtk_get_major_version();

    [LibraryImport(Library)]
    public static partial uint gtk_get_minor_version();

    [LibraryImport(Library)]
    public static partial uint gtk_get_micro_version();

    /// <summary>The default display's settings, borrowed; after <see cref="gtk_init_check"/>.</summary>
    [LibraryImport(Library)]
    public static partial nint gtk_settings_get_default();

    /// <summary>Borrowed.</summary>
    [LibraryImport(Library)]
    public static partial nint gdk_display_get_default();

    /// <summary>Owned.</summary>
    [LibraryImport(Library)]
    public static partial nint gtk_css_provider_new();

    /// <summary>Replaces the provider's rules with <paramref name="data"/> (UTF-8, <paramref name="length"/> bytes).</summary>
    [LibraryImport(Library)]
    public static partial void gtk_css_provider_load_from_data(nint provider, byte* data, nint length);

    /// <summary>The display keeps its own reference on the provider.</summary>
    [LibraryImport(Library)]
    public static partial void gtk_style_context_add_provider_for_display(nint display, nint provider, uint priority);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void gtk_widget_add_css_class(nint widget, string cssClass);

    /// <summary>
    /// The startup notification or activation token the window is presented with, so the window manager lets a
    /// relaunch's window take the focus; set before <see cref="gtk_window_present"/>.
    /// </summary>
    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void gtk_window_set_startup_id(nint window, string startupId);

    [LibraryImport(Library)]
    public static partial nint gtk_window_new();

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void gtk_window_set_title(nint window, string title);

    [LibraryImport(Library)]
    public static partial void gtk_window_set_default_size(nint window, int width, int height);

    /// <summary>Sinks the child's floating reference: the window owns it from now on.</summary>
    [LibraryImport(Library)]
    public static partial void gtk_window_set_child(nint window, nint child);

    [LibraryImport(Library)]
    public static partial void gtk_window_present(nint window);

    [LibraryImport(Library)]
    public static partial void gtk_window_destroy(nint window);

    /// <summary>The smallest size the widget accepts; on a window, its minimum size.</summary>
    [LibraryImport(Library)]
    public static partial void gtk_widget_set_size_request(nint widget, int width, int height);

    [LibraryImport(Library)]
    public static partial nuint gtk_alert_dialog_get_type();

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void gtk_alert_dialog_set_message(nint dialog, string message);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void gtk_alert_dialog_set_detail(nint dialog, string detail);

    /// <summary>The buttons' labels, a null-terminated array the dialog copies.</summary>
    [LibraryImport(Library)]
    public static partial void gtk_alert_dialog_set_buttons(nint dialog, nint* labels);

    /// <summary>The button that Escape and closing the dialog choose.</summary>
    [LibraryImport(Library)]
    public static partial void gtk_alert_dialog_set_cancel_button(nint dialog, int button);

    [LibraryImport(Library)]
    public static partial void gtk_alert_dialog_set_default_button(nint dialog, int button);

    /// <summary>
    /// Shows the dialog over <paramref name="parent"/> without blocking: <paramref name="callback"/> runs on the loop
    /// when it is answered or the parent goes away. The dialog keeps itself alive until then.
    /// </summary>
    [LibraryImport(Library)]
    public static partial void gtk_alert_dialog_choose(
        nint dialog, nint parent, nint cancellable, delegate* unmanaged<nint, nint, nint, void> callback, nint data);

    /// <summary>The button chosen, or -1 with an owned <c>GError</c> when the dialog was dismissed.</summary>
    [LibraryImport(Library)]
    public static partial int gtk_alert_dialog_choose_finish(nint dialog, nint result, nint* error);
}
