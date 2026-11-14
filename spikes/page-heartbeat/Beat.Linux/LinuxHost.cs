using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Spar.PageHeartbeat;

/// <summary>
/// The Linux app under test: GTK 4 and WebKitGTK 6.0 on one thread with a GLib main loop and a GLib synchronization
/// context. One presented window and view (acceleration off and GSK_RENDERER=cairo, the plan's software-GPU default),
/// pages from spar://app (the www folder), the injected script as a document-start user script, page messages through
/// the "spar" script message handler, host messages through webkit_web_view_evaluate_javascript. It also records
/// WebKit's own responsiveness property and web-process termination.
/// </summary>
internal sealed class LinuxHost : Session
{
    private const string Origin = "spar://app";
    private static LinuxHost? s_host;

    private readonly string _www;
    private readonly string _data;
    private GLibContext? _ui;
    private nint _loop;
    private nint _context;
    private nint _window;
    private nint _view;
    private Process? _cover;
    private int _responsiveFlips;

    private LinuxHost(Options options)
        : base(options)
    {
        _www = Path.GetFullPath(options.Get("www") ?? Path.Combine(AppContext.BaseDirectory, "www")) + "/";
        _data = Path.GetFullPath(options.Get("data") ?? Path.Combine(Path.GetTempPath(), "spar-page-heartbeat"));
    }

    public static int Run(Options options)
    {
        Log.Open(options.Get("log"));

        // Software-GPU default from the plan; set through libc (Environment.SetEnvironmentVariable does not reach
        // native getenv), before GTK starts, and only if the user has not set it.
        if (Libc.getenv("GSK_RENDERER") == 0)
        {
            Libc.setenv("GSK_RENDERER", "cairo", 0);
        }

        // No inspector server, ever (plan 3c). An empty value is not enough: WebKit then tries to listen on "".
        Libc.unsetenv("WEBKIT_INSPECTOR_SERVER");
        Libc.unsetenv("WEBKIT_INSPECTOR_HTTP_SERVER");
        var host = new LinuxHost(options);
        var thread = new Thread(host.RunOnUiThread) { Name = "Spar UI" };
        thread.Start();
        thread.Join();
        Log.Line("HOST-EXIT", "code=0");
        return 0;
    }

    private void RunOnUiThread()
    {
        s_host = this;
        if (Gtk.gtk_init_check() == 0)
        {
            Log.Line("HOST-FAILURE", "gtk_init_check failed (no display)");
            return;
        }

        _ui = new GLibContext();
        SynchronizationContext.SetSynchronizationContext(_ui);
        _ui.Post(static state => ((LinuxHost)state!).Main(), this);
        _loop = GLib.g_main_loop_new(0, 0);
        GLib.g_main_loop_run(_loop);
    }

    private void Main()
    {
        try
        {
            Start();
            StartSession(_ui!);
            var start = Options.Get("start", "/index.html");
            Log.Line("NAVIGATE", $"uri={Origin}{start}");
            WebKit.webkit_web_view_load_uri(_view, Origin + start);
        }
        catch (Exception exception)
        {
            Log.Line("HOST-FAILURE", exception.ToString().ReplaceLineEndings(" | "));
            Quit();
        }
    }

    // ---- Start-up ------------------------------------------------------------------------------------------------

    private unsafe void Start()
    {
        var started = Stopwatch.GetTimestamp();
        _context = WebKit.webkit_web_context_new();
        WebKit.webkit_web_context_register_uri_scheme(_context, "spar", &OnSchemeRequest, 0, 0);
        WebKit.webkit_security_manager_register_uri_scheme_as_secure(WebKit.webkit_web_context_get_security_manager(_context), "spar");
        var session = WebKit.webkit_network_session_new(Path.Combine(_data, "data"), Path.Combine(_data, "cache"));
        _window = Gtk.gtk_window_new();
        Gtk.gtk_window_set_title(_window, "Page heartbeat spike");
        Gtk.gtk_window_set_default_size(_window, Options.Int("width", 640), Options.Int("height", 420));
        _view = GObject.New(WebKit.webkit_web_view_get_type(),
            ("web-context", _context, WebKit.webkit_web_context_get_type()),
            ("network-session", session, WebKit.webkit_network_session_get_type()));
        GObject.g_object_ref_sink(_view);
        WebKit.webkit_settings_set_hardware_acceleration_policy(WebKit.webkit_web_view_get_settings(_view), WebKit.AccelerationNever);
        var manager = WebKit.webkit_web_view_get_user_content_manager(_view);
        WebKit.webkit_user_content_manager_register_script_message_handler(manager, "spar", null);
        GObject.Connect(manager, "script-message-received::spar", (delegate* unmanaged<nint, nint, nint, void>)&OnScriptMessage);
        if (!Options.Has("no-inject"))
        {
            var script = WebKit.webkit_user_script_new(File.ReadAllText(_www + "inject.js"), WebKit.InjectTopFrame, WebKit.AtDocumentStart, 0, 0);
            WebKit.webkit_user_content_manager_add_script(manager, script);
            WebKit.webkit_user_script_unref(script);
        }

        GObject.Connect(_view, "load-changed", (delegate* unmanaged<nint, int, nint, void>)&OnLoadChanged);
        GObject.Connect(_view, "web-process-terminated", (delegate* unmanaged<nint, int, nint, void>)&OnWebProcessTerminated);
        GObject.Connect(_view, "notify::is-web-process-responsive", (delegate* unmanaged<nint, nint, nint, void>)&OnResponsiveChanged);
        Gtk.gtk_window_set_child(_window, _view);
        Gtk.gtk_window_present(_window);
        Log.Line("START", $"ms={Log.Ms(started):F1} pid={Environment.ProcessId} cpus={Environment.ProcessorCount} responsive={WebKit.webkit_web_view_get_is_web_process_responsive(_view)} gsk={Marshal.PtrToStringUTF8(Libc.getenv("GSK_RENDERER"))}");
    }

    [UnmanagedCallersOnly]
    private static void OnScriptMessage(nint manager, nint value, nint data)
    {
        var host = s_host!;
        var text = WebKit.jsc_value_to_string(value);
        try
        {
            host.OnPageMessage(Marshal.PtrToStringUTF8(text) ?? "");
        }
        catch (Exception exception)
        {
            Log.Line("MESSAGE-FAILURE", exception.Message);
        }
        finally
        {
            GLib.g_free(text);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnLoadChanged(nint view, int loadEvent, nint data)
    {
        try
        {
            // WebKitLoadEvent: STARTED 0, REDIRECTED 1, COMMITTED 2, FINISHED 3.
            var name = loadEvent switch { 0 => "started", 1 => "redirected", 2 => "committed", 3 => "finished", _ => "event" + loadEvent };
            Log.Line("LOAD-CHANGED", $"event={name}");
            s_host!.OnNavigationEvent(loadEvent switch { 0 => "starting", 2 => "content-loading", 3 => "completed-ok", _ => "other" });
        }
        catch (Exception exception)
        {
            Log.Line("LOAD-FAILURE", exception.Message);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnWebProcessTerminated(nint view, int reason, nint data)
    {
        try
        {
            var name = reason switch { 0 => "crashed", 1 => "exceeded-memory-limit", 2 => "terminated-by-api", _ => "reason" + reason };
            var host = s_host!;
            Log.Line("WEB-PROCESS-TERMINATED", $"reason={name} responsive={WebKit.webkit_web_view_get_is_web_process_responsive(view)}");
            host.OnRendererExited($"reason={name}");
        }
        catch (Exception exception)
        {
            Log.Line("TERMINATED-FAILURE", exception.Message);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnResponsiveChanged(nint view, nint spec, nint data)
    {
        var host = s_host!;
        host._responsiveFlips++;
        Log.Line("WEBKIT-RESPONSIVE", $"value={WebKit.webkit_web_view_get_is_web_process_responsive(view)} flips={host._responsiveFlips} webProcess={WebProcessPid()}");
    }

    [UnmanagedCallersOnly]
    private static unsafe void OnSchemeRequest(nint request, nint data)
    {
        var host = s_host!;
        var path = Marshal.PtrToStringUTF8(WebKit.webkit_uri_scheme_request_get_path(request)) ?? "/";
        var file = host._www + path.TrimStart('/');
        var content = File.Exists(file) ? File.ReadAllBytes(file) : [];
        var type = Path.GetExtension(path) switch
        {
            ".js" => "text/javascript",
            ".css" => "text/css",
            _ => "text/html",
        };
        fixed (byte* bytes = content)
        {
            var gbytes = GLib.g_bytes_new(bytes, (nuint)content.Length);
            var stream = Gio.g_memory_input_stream_new_from_bytes(gbytes);
            WebKit.webkit_uri_scheme_request_finish(request, stream, content.Length, type);
            GObject.g_object_unref(stream);
            GLib.g_bytes_unref(gbytes);
        }
    }

    // ---- Session hooks -------------------------------------------------------------------------------------------

    protected override unsafe int PostToPage(string text)
    {
        // The injected script defines window.__sparHost; strings here are ASCII without quotes.
        var bytes = Encoding.UTF8.GetBytes($"window.__sparHost && window.__sparHost('{text}');");
        fixed (byte* script = bytes)
        {
            WebKit.webkit_web_view_evaluate_javascript(_view, script, bytes.Length, 0, 0, 0, null, 0);
        }

        return 0;
    }

    protected override void NavigateTo(string path) => WebKit.webkit_web_view_load_uri(_view, path.Contains("://", StringComparison.Ordinal) || path.StartsWith("about:", StringComparison.Ordinal) ? path : Origin + path);

    protected override void ReloadPage()
    {
        var started = Stopwatch.GetTimestamp();
        WebKit.webkit_web_view_reload(_view);
        Log.Line("RELOAD", $"callUs={Stopwatch.GetElapsedTime(started).TotalMicroseconds:F0}");
    }

    protected override void KillRenderer()
    {
        var pid = WebProcessPid();
        if (Options.Get("kill-how", "api") == "api")
        {
            var started = Stopwatch.GetTimestamp();
            WebKit.webkit_web_view_terminate_web_process(_view);
            Log.Line("KILL-RENDERER", $"how=api pid={pid} callUs={Stopwatch.GetElapsedTime(started).TotalMicroseconds:F0}");
        }
        else
        {
            // An external kill, as a crash or the OOM killer would do: SIGKILL to this app's own web process.
            Log.Line("KILL-RENDERER", $"how=sigkill pid={pid} result={(pid > 0 ? Libc.kill(pid, 9) : -1)}");
        }
    }

    protected override string WindowHiddenReason()
    {
        if (Gtk.gtk_widget_get_mapped(_window) == 0)
        {
            return "unmapped";
        }

        var surface = Gtk.gtk_native_get_surface(_window);
        return surface != 0 && (Gtk.gdk_toplevel_get_state(surface) & 1) != 0 ? "minimised" : "";
    }

    protected override bool HostAction(TimedAction action)
    {
        switch (action.Verb)
        {
            case "minimize":
                Gtk.gtk_window_minimize(_window);
                break;
            case "restore":
                Gtk.gtk_window_unminimize(_window);
                Gtk.gtk_window_present(_window);
                break;
            case "hide":
                Gtk.gtk_widget_set_visible(_window, 0);
                break;
            case "show":
                Gtk.gtk_widget_set_visible(_window, 1);
                Gtk.gtk_window_present(_window);
                break;
            case "cover":
                // Another X client, larger than the screen, raised above the app window by the window manager.
                _cover = Process.Start(new ProcessStartInfo("xlogo", ["-geometry", "1400x900+0+0", "-bg", "black", "-fg", "black"]) { UseShellExecute = false });
                Log.Line("COVER", $"on=True pid={_cover?.Id}");
                break;
            case "uncover":
                _cover?.Kill();
                _cover = null;
                Log.Line("COVER", "on=False");
                break;
            case "sh":
                // Window-manager and input commands (xdotool) with the window's X id in $WID; the harness runs inside
                // a container display only.
                var xid = Gtk.gdk_x11_surface_get_xid(Gtk.gtk_native_get_surface(_window));
                var psi = new ProcessStartInfo("sh", ["-c", action.Arg]) { UseShellExecute = false };
                psi.Environment["WID"] = xid.ToString(CultureInfo.InvariantCulture);
                using (var process = Process.Start(psi))
                {
                    Log.Line("SH", $"cmd={action.Arg} wid={xid}");
                }

                break;
            case "memory":
                LogMemory(action.Arg);
                break;
            case "processes":
                Log.Line("PROCESSES", $"web={WebProcessPid()} responsive={WebKit.webkit_web_view_get_is_web_process_responsive(_view)}");
                break;
            case "stop":
                WebKit.webkit_web_view_stop_loading(_view);
                break;
            default:
                return false;
        }

        return true;
    }

    protected override void OnStressSample() => LogMemory("stress");

    /// <summary>Private memory (statm resident - shared, as spikes/resource-usage measured) of the app and its web process, kB.</summary>
    private static void LogMemory(string label)
    {
        static long PrivateKb(int pid)
        {
            try
            {
                var fields = File.ReadAllText(pid == 0 ? "/proc/self/statm" : $"/proc/{pid}/statm").Split(' ');
                return (long.Parse(fields[1], CultureInfo.InvariantCulture) - long.Parse(fields[2], CultureInfo.InvariantCulture)) * Environment.SystemPageSize / 1024;
            }
            catch (IOException)
            {
                return -1;
            }
        }

        var web = WebProcessPid();
        Log.Line("MEMORY", $"label={label} hostKb={PrivateKb(0)} web:{(web > 0 ? PrivateKb(web) : -1)}");
    }

    protected override void ShowUnresponsiveDialog() => Log.Line("DIALOG-SKIPPED", "not implemented on Linux in this spike");

    protected override void CloseUnresponsiveDialog(string why)
    {
    }

    protected override void Quit()
    {
        _cover?.Kill();
        Gtk.gtk_window_destroy(_window);
        _ = QuitLaterAsync();
    }

    private async Task QuitLaterAsync()
    {
        // Keep the loop running until the web processes have exited (spikes/linux-desktop).
        for (var i = 0; i < 40 && WebProcessPid() > 0; i++)
        {
            await Task.Delay(50);
        }

        GLib.g_main_loop_quit(_loop);
    }

    /// <summary>This app's WebKitWebProcess, found by walking /proc (WebKit has no API for its pids).</summary>
    private static int WebProcessPid()
    {
        var self = Environment.ProcessId;
        var parents = new Dictionary<int, (int Parent, string Name)>();
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory), out var pid))
            {
                continue;
            }

            try
            {
                var stat = File.ReadAllText(Path.Combine(directory, "stat"));
                var close = stat.LastIndexOf(')');
                var name = stat[(stat.IndexOf('(') + 1)..close];
                var fields = stat[(close + 2)..].Split(' ');
                parents[pid] = (int.Parse(fields[1], CultureInfo.InvariantCulture), name);
            }
            catch (IOException)
            {
            }
        }

        foreach (var (pid, (_, name)) in parents)
        {
            if (!name.StartsWith("WebKitWebProces", StringComparison.Ordinal))
            {
                continue;
            }

            for (var p = pid; p > 1 && parents.TryGetValue(p, out var entry); p = entry.Parent)
            {
                if (entry.Parent == self)
                {
                    return pid;
                }
            }
        }

        return 0;
    }
}
