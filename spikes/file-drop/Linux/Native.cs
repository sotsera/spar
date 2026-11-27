using System.Runtime.InteropServices;

namespace FileDrop;

// The probe's own declarations, only what it calls.
internal static unsafe class Native
{
    private const string GLib = "libglib-2.0.so.0";
    private const string GObject = "libgobject-2.0.so.0";
    private const string Gio = "libgio-2.0.so.0";
    private const string Gtk = "libgtk-4.so.1";
    private const string WebKit = "libwebkitgtk-6.0.so.4";
    private const string Jsc = "libjavascriptcoregtk-6.0.so.1";
    private const string X11 = "libX11.so.6";

    [DllImport(GLib)] public static extern nint g_main_loop_new(nint context, int running);
    [DllImport(GLib)] public static extern void g_main_loop_run(nint loop);
    [DllImport(GLib)] public static extern void g_main_loop_quit(nint loop);
    [DllImport(GLib)] public static extern void g_free(nint memory);
    [DllImport(GLib)] public static extern void g_slist_free(nint list);
    [DllImport(GLib)] public static extern uint g_list_length(nint list);
    [DllImport(GLib)] public static extern void g_list_free(nint list);
    [DllImport(GLib)] public static extern nint g_quark_to_string(uint quark);
    [DllImport(GLib)] public static extern uint g_timeout_add(uint interval, delegate* unmanaged<nint, int> function, nint data);

    [DllImport(GObject)] public static extern ulong g_signal_connect_data(nint instance, string signal, nint handler, nint data, nint destroy, int flags);
    [DllImport(GObject)] public static extern nint g_object_ref(nint instance);
    [DllImport(GObject)] public static extern void g_object_unref(nint instance);
    [DllImport(GObject)] public static extern nint g_value_get_boxed(nint value);

    [DllImport(Gio)] public static extern nint g_file_get_path(nint file);
    [DllImport(Gio)] public static extern nint g_cancellable_new();
    [DllImport(Gio)] public static extern void g_cancellable_cancel(nint cancellable);

    [DllImport(Gtk)] public static extern int gtk_init_check();
    [DllImport(Gtk)] public static extern nint gtk_window_new();
    [DllImport(Gtk)] public static extern void gtk_window_set_default_size(nint window, int width, int height);
    [DllImport(Gtk)] public static extern void gtk_window_set_decorated(nint window, int decorated);
    [DllImport(Gtk)] public static extern void gtk_window_set_child(nint window, nint child);
    [DllImport(Gtk)] public static extern void gtk_window_present(nint window);
    [DllImport(Gtk)] public static extern nint gtk_window_list_toplevels();
    [DllImport(Gtk)] public static extern nint gtk_native_get_surface(nint native);
    [DllImport(Gtk)] public static extern ulong gdk_x11_surface_get_xid(nint surface);
    [DllImport(Gtk)] public static extern void gtk_widget_add_controller(nint widget, nint controller);
    [DllImport(Gtk)] public static extern void gtk_event_controller_set_propagation_phase(nint controller, int phase);
    [DllImport(Gtk)] public static extern nint gtk_drop_controller_motion_new();
    [DllImport(Gtk)] public static extern nint gtk_drop_controller_motion_get_drop(nint controller);
    [DllImport(Gtk)] public static extern nint gtk_drop_target_new(nuint type, int actions);
    [DllImport(Gtk)] public static extern nuint gdk_file_list_get_type();
    [DllImport(Gtk)] public static extern nint gdk_file_list_get_files(nint fileList);
    [DllImport(Gtk)] public static extern void gdk_drop_read_value_async(nint drop, nuint type, int priority, nint cancellable, delegate* unmanaged<nint, nint, nint, void> callback, nint data);
    [DllImport(Gtk)] public static extern nint gdk_drop_read_value_finish(nint drop, nint result, nint* error);
    [DllImport(Gtk)] public static extern nint gtk_file_dialog_new();
    [DllImport(Gtk)] public static extern void gtk_file_dialog_open(nint dialog, nint parent, nint cancellable, delegate* unmanaged<nint, nint, nint, void> callback, nint data);
    [DllImport(Gtk)] public static extern nint gtk_file_dialog_open_finish(nint dialog, nint result, nint* error);
    [DllImport(Gtk)] public static extern nint gtk_window_get_title(nint window);

    [DllImport(WebKit)] public static extern nint webkit_web_view_new();
    [DllImport(WebKit)] public static extern nint webkit_user_script_new_for_world(string source, int frames, int time, string world, nint allow, nint block);
    [DllImport(WebKit)] public static extern void webkit_user_content_manager_add_script(nint manager, nint script);
    [DllImport(WebKit)] public static extern int webkit_user_content_manager_register_script_message_handler(nint manager, string name, string world);
    [DllImport(WebKit)] public static extern nint webkit_web_view_get_user_content_manager(nint webView);
    [DllImport(WebKit)] public static extern int webkit_user_content_manager_register_script_message_handler(nint manager, string name, nint world);
    [DllImport(WebKit)] public static extern void webkit_web_view_load_html(nint webView, string html, string baseUri);
    [DllImport(WebKit)] public static extern nint webkit_navigation_policy_decision_get_navigation_action(nint decision);
    [DllImport(WebKit)] public static extern nint webkit_navigation_action_get_request(nint action);
    [DllImport(WebKit)] public static extern nint webkit_uri_request_get_uri(nint request);
    [DllImport(WebKit)] public static extern void webkit_policy_decision_ignore(nint decision);

    [DllImport(Jsc)] public static extern nint jsc_value_to_string(nint value);

    [DllImport(X11)] public static extern int XInitThreads();
    [DllImport(X11)] public static extern nint XOpenDisplay(nint name);
    [DllImport(X11)] public static extern ulong XDefaultRootWindow(nint display);
    [DllImport(X11)] public static extern ulong XCreateSimpleWindow(nint display, ulong parent, int x, int y, uint width, uint height, uint border, ulong borderPixel, ulong background);
    [DllImport(X11)] public static extern ulong XInternAtom(nint display, string name, int onlyIfExists);
    [DllImport(X11)] public static extern int XSetSelectionOwner(nint display, ulong selection, ulong owner, ulong time);
    [DllImport(X11)] public static extern int XSendEvent(nint display, ulong window, int propagate, long mask, long* xevent);
    [DllImport(X11)] public static extern int XFlush(nint display);
    [DllImport(X11)] public static extern int XPending(nint display);
    [DllImport(X11)] public static extern int XNextEvent(nint display, long* xevent);
    [DllImport(X11)] public static extern int XChangeProperty(nint display, ulong window, ulong property, ulong type, int format, int mode, byte* data, int elements);
    [DllImport(X11)] public static extern int XTranslateCoordinates(nint display, ulong source, ulong destination, int x, int y, out int destinationX, out int destinationY, out ulong child);

    public static string? Utf8(nint text) => text == 0 ? null : Marshal.PtrToStringUTF8(text);

    public static string? TakeUtf8(nint text)
    {
        var value = Utf8(text);
        if (text != 0)
        {
            g_free(text);
        }

        return value;
    }

    public static string ErrorText(nint error)
    {
        if (error == 0)
        {
            return "no error";
        }

        var domain = *(uint*)error;
        var code = *(int*)(error + 4);
        var message = Utf8(*(nint*)(error + 8));
        return $"{Utf8(g_quark_to_string(domain))} {code}: {message}";
    }
}
