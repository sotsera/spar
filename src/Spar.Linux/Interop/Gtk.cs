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

    /// <summary><c>GDK_TOPLEVEL_STATE_MINIMIZED</c>.</summary>
    public const uint ToplevelMinimized = 1 << 0;

    /// <summary><c>GDK_TOPLEVEL_STATE_SUSPENDED</c> (GTK 4.12): the surface cannot be seen by the user.</summary>
    public const uint ToplevelSuspended = 1 << 16;

    /// <summary>Opens the display named by the environment; false when there is none.</summary>
    [LibraryImport(Library)]
    public static partial int gtk_init_check();

    /// <summary>
    /// Releases the calling thread's current GL context, which GDK keeps referenced in a thread-local slot; nothing when
    /// there is none, GTK initialised or not.
    /// </summary>
    [LibraryImport(Library)]
    public static partial void gdk_gl_context_clear_current();

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

    /// <summary>Takes the provider off the display, dropping the display's reference (a test window's theme).</summary>
    [LibraryImport(Library)]
    public static partial void gtk_style_context_remove_provider_for_display(nint display, nint provider);

    /// <summary>The settings of one display, borrowed: each GDK display has its own (the dark preference among them).</summary>
    [LibraryImport(Library)]
    public static partial nint gtk_settings_get_for_display(nint display);

    /// <summary>The display a widget is on, borrowed.</summary>
    [LibraryImport(Library)]
    public static partial nint gtk_widget_get_display(nint widget);

    /// <summary>Asks the window to close, as the window manager does: GTK emits <c>close-request</c>.</summary>
    [LibraryImport(Library)]
    public static partial void gtk_window_close(nint window);

    /// <summary>Moves a window that is not realized yet to another display (a test window's display for its scheme).</summary>
    [LibraryImport(Library)]
    public static partial void gtk_window_set_display(nint window, nint display);

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

    /// <summary>Asks the window manager to minimize the window; one that cannot (none running) leaves it as it is.</summary>
    [LibraryImport(Library)]
    public static partial void gtk_window_minimize(nint window);

    [LibraryImport(Library)]
    public static partial void gtk_window_unminimize(nint window);

    /// <summary>Asks the window manager to maximize the window; one that cannot (none running) leaves it as it is.</summary>
    [LibraryImport(Library)]
    public static partial void gtk_window_maximize(nint window);

    [LibraryImport(Library)]
    public static partial void gtk_window_unmaximize(nint window);

    /// <summary>A <c>gboolean</c>: whether the window manager reports the window maximized.</summary>
    [LibraryImport(Library)]
    public static partial int gtk_window_is_maximized(nint window);

    /// <summary>The window's title, borrowed UTF-8 (never freed by the caller); 0 for none.</summary>
    [LibraryImport(Library)]
    public static partial nint gtk_window_get_title(nint window);

    /// <summary>The widget's allocated width, in its own units (CSS pixels for the view).</summary>
    [LibraryImport(Library)]
    public static partial int gtk_widget_get_width(nint widget);

    [LibraryImport(Library)]
    public static partial int gtk_widget_get_height(nint widget);

    /// <summary>Keeps the window above <paramref name="parent"/> (0 for none); the window manager may centre it on it.</summary>
    [LibraryImport(Library)]
    public static partial void gtk_window_set_transient_for(nint window, nint parent);

    /// <summary>The window <see cref="gtk_window_set_transient_for"/> set, borrowed; 0 for none.</summary>
    [LibraryImport(Library)]
    public static partial nint gtk_window_get_transient_for(nint window);

    /// <summary>A <c>gboolean</c>: while the window is shown, the other windows of its window group take no input.</summary>
    [LibraryImport(Library)]
    public static partial void gtk_window_set_modal(nint window, int modal);

    [LibraryImport(Library)]
    public static partial int gtk_window_get_modal(nint window);

    /// <summary>
    /// A window group, owned: grabs and modality reach only the windows of one group, so a modal blocks its own group
    /// and no other. Every window takes a reference of its own on its group.
    /// </summary>
    [LibraryImport(Library)]
    public static partial nint gtk_window_group_new();

    [LibraryImport(Library)]
    public static partial void gtk_window_group_add_window(nint group, nint window);

    /// <summary>The window's group, borrowed: the default group of the app when none was set.</summary>
    [LibraryImport(Library)]
    public static partial nint gtk_window_get_group(nint window);

    /// <summary>The smallest size the widget accepts; on a window, its minimum size.</summary>
    [LibraryImport(Library)]
    public static partial void gtk_widget_set_size_request(nint widget, int width, int height);

    /// <summary>Whether the widget is mapped (a window: shown, not hidden), as a <c>gboolean</c>.</summary>
    [LibraryImport(Library)]
    public static partial int gtk_widget_get_mapped(nint widget);

    /// <summary>The window's <c>GdkSurface</c>, borrowed; 0 before it is realised.</summary>
    [LibraryImport(Library)]
    public static partial nint gtk_native_get_surface(nint native);

    /// <summary>A toplevel surface's <c>GdkToplevelState</c> flags (<see cref="ToplevelMinimized"/>, …).</summary>
    [LibraryImport(Library)]
    public static partial uint gdk_toplevel_get_state(nint toplevel);

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

    /// <summary>
    /// A controller that sees every event of its widget as it is (<c>event</c>: the controller, the <c>GdkEvent</c>, the
    /// user data; TRUE stops it); owned until <see cref="gtk_widget_add_controller"/> takes it.
    /// </summary>
    [LibraryImport(Library)]
    public static partial nint gtk_event_controller_legacy_new();

    /// <summary>
    /// A controller for key events (<c>key-pressed</c>: the controller, the keyval, the keycode, the
    /// <c>GdkModifierType</c> state, the user data; TRUE stops it); owned until <see cref="gtk_widget_add_controller"/>.
    /// </summary>
    [LibraryImport(Library)]
    public static partial nint gtk_event_controller_key_new();

    /// <summary><c>GTK_PHASE_CAPTURE</c> (1): the controller sees the event on its way down, before the widget's own.</summary>
    [LibraryImport(Library)]
    public static partial void gtk_event_controller_set_propagation_phase(nint controller, int phase);

    /// <summary>The widget takes the controller (transfer full) and keeps it while it lives.</summary>
    [LibraryImport(Library)]
    public static partial void gtk_widget_add_controller(nint widget, nint controller);

    /// <summary><c>GdkEventType</c>: 2 button press, 3 release, 4 key press, 17 touch begin, 19 touch end.</summary>
    [LibraryImport(Library)]
    public static partial int gdk_event_get_event_type(nint gdkEvent);
}
