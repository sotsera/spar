using System.Runtime.InteropServices;

namespace Startup.Linux;

// After the checks pass: a window with a WebKitWebView loads a small page, to see whether a passed check means a
// working view. Ends on load-finished, web-process-terminated or a 20 s timeout.
internal static unsafe class TryView
{
    private static nint s_loop;
    private static string s_outcome = "timeout";
    private static Log? s_log;

    public static StartupProblem? Run(Options o, Log log)
    {
        s_log = log;
        var window = Gtk.gtk_window_new();
        Gtk.gtk_window_set_title(window, o.AppName);
        Gtk.gtk_window_set_default_size(window, 640, 400);
        var view = WebKit.webkit_web_view_new();
        GObject.g_signal_connect_data(view, "load-changed", (nint)(delegate* unmanaged<nint, int, nint, void>)&OnLoadChanged, 0, 0, 0);
        GObject.g_signal_connect_data(view, "web-process-terminated", (nint)(delegate* unmanaged<nint, int, nint, void>)&OnTerminated, 0, 0, 0);
        Gtk.gtk_window_set_child(window, view);
        WebKit.webkit_web_view_load_html(view, "<!doctype html><title>ok</title><p>Spar startup check</p>", "about:blank");
        Gtk.gtk_window_present(window);
        s_loop = GLib.g_main_loop_new(0, 0);
        GLib.g_timeout_add(20_000, &OnTimeout, 0);
        var started = log.Now;
        GLib.g_main_loop_run(s_loop);
        log.Mark("try-view", ("outcome", s_outcome), ("ms", log.Now - started));
        Gtk.gtk_window_destroy(window);
        return s_outcome == "finished" ? null : new StartupProblem(ProblemKind.Broken, "WebKitGTK 6.0",
            $"WebKitGTK could not display a page ({s_outcome}).", "Reinstall WebKitGTK 6.0 and bubblewrap, or report this to the app's publisher.",
            $"trial view: {s_outcome}");
    }

    [UnmanagedCallersOnly]
    private static void OnLoadChanged(nint view, int loadEvent, nint data)
    {
        s_log?.Mark("load-changed", ("event", loadEvent));
        if (loadEvent == 3) // WEBKIT_LOAD_FINISHED
        {
            s_outcome = "finished";
            GLib.g_main_loop_quit(s_loop);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnTerminated(nint view, int reason, nint data)
    {
        s_outcome = $"web-process-terminated reason={reason}";
        GLib.g_main_loop_quit(s_loop);
    }

    [UnmanagedCallersOnly]
    private static int OnTimeout(nint data)
    {
        GLib.g_main_loop_quit(s_loop);
        return 0;
    }
}
