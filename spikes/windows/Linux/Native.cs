using System.Runtime.InteropServices;

namespace WindowsSpike;

[StructLayout(LayoutKind.Sequential)]
internal struct GValue
{
    public nuint Type;
    public long Data0;
    public long Data1;
}

// The probe's own declarations, only what it calls.
internal static unsafe class Native
{
    private const string GLib = "libglib-2.0.so.0";
    private const string GObject = "libgobject-2.0.so.0";
    private const string Gtk = "libgtk-4.so.1";
    private const string WebKit = "libwebkitgtk-6.0.so.4";
    private const string Jsc = "libjavascriptcoregtk-6.0.so.1";
    private const string X11 = "libX11.so.6";
    private const string XTest = "libXtst.so.6";

    [DllImport(GLib)] public static extern nint g_main_loop_new(nint context, int running);
    [DllImport(GLib)] public static extern void g_main_loop_run(nint loop);
    [DllImport(GLib)] public static extern void g_main_loop_quit(nint loop);
    [DllImport(GLib)] public static extern uint g_idle_add(delegate* unmanaged<nint, int> function, nint data);
    [DllImport(GLib)] public static extern void g_free(nint memory);
    [DllImport(GLib)] public static extern void g_error_free(nint error);

    [DllImport(GObject)] public static extern ulong g_signal_connect_data(nint instance, string signal, nint handler, nint data, nint destroy, int flags);
    [DllImport(GObject)] public static extern nint g_object_new_with_properties(nuint type, uint count, nint* names, GValue* values);
    [DllImport(GObject)] public static extern GValue* g_value_init(GValue* value, nuint type);
    [DllImport(GObject)] public static extern void g_value_set_object(GValue* value, nint instance);
    [DllImport(GObject)] public static extern void g_value_unset(GValue* value);
    [DllImport(GObject)] public static extern void g_object_unref(nint instance);

    [DllImport(Gtk)] public static extern int gtk_init_check();
    [DllImport(Gtk)] public static extern nint gtk_window_new();
    [DllImport(Gtk)] public static extern void gtk_window_set_default_size(nint window, int width, int height);
    [DllImport(Gtk)] public static extern void gtk_window_set_title(nint window, string title);
    [DllImport(Gtk)] public static extern void gtk_window_set_child(nint window, nint child);
    [DllImport(Gtk)] public static extern void gtk_window_present(nint window);
    [DllImport(Gtk)] public static extern void gtk_window_set_transient_for(nint window, nint parent);
    [DllImport(Gtk)] public static extern void gtk_window_set_modal(nint window, int modal);
    [DllImport(Gtk)] public static extern void gtk_window_destroy(nint window);
    [DllImport(Gtk)] public static extern nint gtk_native_get_surface(nint native);
    [DllImport(Gtk)] public static extern ulong gdk_x11_surface_get_xid(nint surface);

    [DllImport(WebKit)] public static extern nint webkit_network_session_new(string dataDirectory, string cacheDirectory);
    [DllImport(WebKit)] public static extern nuint webkit_network_session_get_type();
    [DllImport(WebKit)] public static extern nuint webkit_web_view_get_type();
    [DllImport(WebKit)] public static extern nint webkit_user_content_manager_new();
    [DllImport(WebKit)] public static extern nuint webkit_user_content_manager_get_type();
    [DllImport(WebKit)] public static extern int webkit_user_content_manager_register_script_message_handler(nint manager, string name, nint world);
    [DllImport(WebKit)] public static extern void webkit_web_view_load_html(nint view, string html, string baseUri);
    [DllImport(WebKit)] public static extern void webkit_web_view_reload(nint view);
    [DllImport(WebKit)] public static extern void webkit_web_view_set_zoom_level(nint view, double level);
    [DllImport(WebKit)] public static extern double webkit_web_view_get_zoom_level(nint view);
    [DllImport(WebKit)] public static extern void webkit_web_view_terminate_web_process(nint view);
    [DllImport(WebKit)] public static extern void webkit_web_view_evaluate_javascript(nint view, string script, nint length, nint world, nint sourceUri, nint cancellable, delegate* unmanaged<nint, nint, nint, void> callback, nint data);
    [DllImport(WebKit)] public static extern nint webkit_web_view_evaluate_javascript_finish(nint view, nint result, nint* error);
    [DllImport(WebKit)] public static extern nint webkit_web_view_get_find_controller(nint view);
    [DllImport(WebKit)] public static extern void webkit_find_controller_search(nint controller, string text, uint options, uint maxMatches);
    [DllImport(WebKit)] public static extern void webkit_find_controller_count_matches(nint controller, string text, uint options, uint maxMatches);
    [DllImport(WebKit)] public static extern void webkit_find_controller_search_next(nint controller);
    [DllImport(WebKit)] public static extern void webkit_find_controller_search_finish(nint controller);

    [DllImport(Jsc)] public static extern nint jsc_value_to_string(nint value);

    [DllImport(X11)] public static extern int XInitThreads();
    [DllImport(X11)] public static extern nint XOpenDisplay(nint name);
    [DllImport(X11)] public static extern int XFlush(nint display);
    [DllImport(X11)] public static extern byte XKeysymToKeycode(nint display, ulong keysym);
    [DllImport(X11)] public static extern ulong XDefaultRootWindow(nint display);
    [DllImport(X11)] public static extern int XTranslateCoordinates(nint display, ulong source, ulong destination, int x, int y, out int destinationX, out int destinationY, out ulong child);
    [DllImport(XTest)] public static extern int XTestFakeMotionEvent(nint display, int screen, int x, int y, ulong delay);
    [DllImport(XTest)] public static extern int XTestFakeButtonEvent(nint display, uint button, int press, ulong delay);
    [DllImport(XTest)] public static extern int XTestFakeKeyEvent(nint display, uint keycode, int press, ulong delay);

    public static string? TakeString(nint text)
    {
        if (text == 0)
        {
            return null;
        }

        var value = Marshal.PtrToStringUTF8(text);
        g_free(text);
        return value;
    }
}
