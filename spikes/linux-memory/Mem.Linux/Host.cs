using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Mem.Linux;

/// <summary>
/// One GTK thread with a GLib main loop. Commands from the control FIFO are queued and run by an idle source on that
/// thread. The shutdown path is selectable (--fix) so the GTK4 web-process lifetime can be compared across orders.
/// </summary>
internal sealed unsafe class Host
{
    private const string Origin = "spar://app";
    private static Host? s_host;

    private readonly Options _options;
    private readonly ConcurrentQueue<string> _commands = new();
    private int _drainScheduled;
    private nint _loop;
    private nint _window;
    private nint _view;
    private nint _context;
    private bool _shuttingDown;
    private bool _windowGone;
    private string _page = "";

    // The last button press on the view, for the window-manager drag.
    private bool _pressed;
    private uint _pressButton;
    private uint _pressTime;
    private double _pressX;
    private double _pressY;
    private nint _pressDevice;

    public Host(Options options)
    {
        _options = options;
    }

    private static Host Current => s_host!;

    public int Run()
    {
        var thread = new Thread(RunOnUiThread) { Name = "Spar UI" };
        thread.Start();
        thread.Join();
        return 0;
    }

    private void RunOnUiThread()
    {
        s_host = this;
#if SPAR_GTK4
        if (Gtk.gtk_init_check() == 0)
#else
        if (Gtk.gtk_init_check(0, 0) == 0)
#endif
        {
            Log.Mark("error gtk_init_check failed (no display)");
            return;
        }

        Log.Mark("gtk-init display=" + GObject.TypeName(Gtk.gdk_display_get_default()));
        CreateWindow();
        if (_options.Mode == "gtk")
        {
            var label = Gtk.gtk_label_new("Spar memory floor: GTK only");
            SetChild(label);
        }
        else
        {
            CreateWebView();
            Load(_options.Page);
        }

        Show();
        if (_options.Mode == "gtk")
        {
            GLib.g_timeout_add_full(0, 200, &OnGtkReady, 0, 0);
        }

        if (_options.Control is { } control)
        {
            StartControlReader(control);
        }

        _loop = GLib.g_main_loop_new(0, 0);
        GLib.g_main_loop_run(_loop);
        Log.Mark("main-loop-ended view-refs=" + GObject.RefCount(_view).ToString(CultureInfo.InvariantCulture));
        if (_view != 0)
        {
            // The host's own reference; the view is disposed here unless something else still holds it.
            GObject.g_object_unref(_view);
        }

        if (_context != 0)
        {
            GObject.g_object_unref(_context);
        }

        WaitForChildren();
        GLib.g_main_loop_unref(_loop);
        Log.Mark("exit");
    }

    // ---- Window and view ----

    private void CreateWindow()
    {
#if SPAR_GTK4
        _window = Gtk.gtk_window_new();
        GObject.Connect(_window, "close-request", (delegate* unmanaged<nint, nint, int>)&OnCloseRequest);
#else
        _window = Gtk.gtk_window_new(0);
        GObject.Connect(_window, "delete-event", (delegate* unmanaged<nint, nint, nint, int>)&OnDeleteEvent);
#endif
        Gtk.gtk_window_set_title(_window, _options.Title);
        Gtk.gtk_window_set_default_size(_window, _options.Width, _options.Height);
        GObject.Connect(_window, "destroy", (delegate* unmanaged<nint, nint, void>)&OnDestroy);
        Track(_window, "window");
    }

    private void SetChild(nint child)
    {
#if SPAR_GTK4
        Gtk.gtk_window_set_child(_window, child);
#else
        Gtk.gtk_container_add(_window, child);
#endif
    }

    private void Show()
    {
#if SPAR_GTK4
        Gtk.gtk_window_present(_window);
        var renderer = Gtk.gtk_native_get_renderer(_window);
        Log.Mark("shown gsk-renderer=" + GObject.TypeName(renderer));
#else
        Gtk.gtk_widget_show_all(_window);
        Log.Mark("shown");
#endif
    }

    private void CreateWebView()
    {
        var data = Path.Combine(_options.DataDirectory, "data");
        var cache = Path.Combine(_options.DataDirectory, "cache");
#if SPAR_GTK4
        var session = WebKit.webkit_network_session_new(data, cache);
        _context = WebKit.webkit_web_context_new();
        RegisterScheme();
        _view = GObject.New(
            WebKit.webkit_web_view_get_type(),
            new Property("web-context", null, _context, WebKit.webkit_web_context_get_type()),
            new Property("network-session", null, session, WebKit.webkit_network_session_get_type()));
        GObject.g_object_unref(session);
#else
        var manager = GObject.New(
            WebKit.webkit_website_data_manager_get_type(),
            new Property("base-data-directory", data),
            new Property("base-cache-directory", cache));
        _context = WebKit.webkit_web_context_new_with_website_data_manager(manager);
        GObject.g_object_unref(manager);
        WebKit.webkit_web_context_set_sandbox_enabled(_context, _options.Sandbox ? 1 : 0);
        RegisterScheme();
        _view = GObject.New(WebKit.webkit_web_view_get_type(), new Property("web-context", null, _context, WebKit.webkit_web_context_get_type()));
#endif
        // Keep a reference of our own, so the reference count after the window is gone can be read safely.
        GObject.g_object_ref_sink(_view);
        Track(_view, "view");
        var settings = WebKit.webkit_web_view_get_settings(_view);
        if (_options.Hw != "default")
        {
            WebKit.webkit_settings_set_hardware_acceleration_policy(settings, _options.Hw == "never" ? WebKit.AccelerationNever : WebKit.AccelerationAlways);
        }

        var manager2 = WebKit.webkit_web_view_get_user_content_manager(_view);
#if SPAR_GTK4
        WebKit.webkit_user_content_manager_register_script_message_handler(manager2, "spar", null);
#else
        WebKit.webkit_user_content_manager_register_script_message_handler(manager2, "spar");
#endif
        GObject.Connect(manager2, "script-message-received::spar", (delegate* unmanaged<nint, nint, nint, void>)&OnScriptMessage);
        GObject.Connect(_view, "load-changed", (delegate* unmanaged<nint, int, nint, void>)&OnLoadChanged);
        GObject.Connect(_view, "web-process-terminated", (delegate* unmanaged<nint, int, nint, void>)&OnWebProcessTerminated);
        if (_options.Drag != "off")
        {
#if SPAR_GTK4
            var controller = Gtk.gtk_event_controller_legacy_new();
            Gtk.gtk_event_controller_set_propagation_phase(controller, 1); // GTK_PHASE_CAPTURE: before WebKit sees it
            GObject.Connect(controller, "event", (delegate* unmanaged<nint, nint, nint, int>)&OnLegacyEvent);
            Gtk.gtk_widget_add_controller(_view, controller);
#else
            GObject.Connect(_view, "button-press-event", (delegate* unmanaged<nint, nint, nint, int>)&OnGtk3Event);
            GObject.Connect(_view, "motion-notify-event", (delegate* unmanaged<nint, nint, nint, int>)&OnGtk3Event);
            GObject.Connect(_view, "button-release-event", (delegate* unmanaged<nint, nint, nint, int>)&OnGtk3Event);
#endif
        }

        SetChild(_view);
        Gtk.gtk_widget_grab_focus(_view);
        Log.Mark($"view-created hw={_options.Hw} policy={WebKit.webkit_settings_get_hardware_acceleration_policy(settings)} sandbox={_options.Sandbox} drag={_options.Drag} fix={_options.Fix}");
    }

    private void RegisterScheme()
    {
        WebKit.webkit_web_context_register_uri_scheme(_context, "spar", &OnSchemeRequest, 0, 0);
        WebKit.webkit_security_manager_register_uri_scheme_as_secure(WebKit.webkit_web_context_get_security_manager(_context), "spar");
    }

    private void Load(string page)
    {
        Log.Mark("load " + page);
        _page = page;
        WebKit.webkit_web_view_load_uri(_view, $"{Origin}/{page}.html");
    }

    /// <summary>Weak reference (called at dispose) and object data (freed at finalize) on an object, both logged.</summary>
    private static void Track(nint instance, string name)
    {
        var tag = Marshal.StringToCoTaskMemUTF8(name);
        GObject.g_object_weak_ref(instance, &OnWeakNotify, tag);
        GObject.g_object_set_data_full(instance, "spar-finalize", Marshal.StringToCoTaskMemUTF8(name), &OnFinalizeData);
    }

    // ---- Shutdown ----

    /// <summary>
    /// The close path. none: destroy the window. unparent: remove the view from the window first. dispose: remove it and
    /// run its dispose. terminate: end the web process, then destroy. insensitive: make the view insensitive first.
    /// hide-first: hide, let the loop run 200 ms, then destroy. try-close: webkit_web_view_try_close, destroy on "close".
    /// </summary>
    private void Shutdown(string reason)
    {
        if (_shuttingDown)
        {
            return;
        }

        _shuttingDown = true;
        Log.Mark($"shutdown reason={reason} fix={_options.Fix} view-refs={GObject.RefCount(_view)} window-refs={GObject.RefCount(_window)}");
        if (_view == 0)
        {
            DestroyWindow();
            return;
        }

        switch (_options.Fix)
        {
            case "unparent":
                Unparent();
                break;
            case "dispose":
                Unparent();
                GObject.g_object_run_dispose(_view);
                Log.Mark("view dispose run view-refs=" + GObject.RefCount(_view).ToString(CultureInfo.InvariantCulture));
                break;
            case "terminate":
                WebKit.webkit_web_view_terminate_web_process(_view);
                Log.Mark("web process terminated by host");
                break;
            case "insensitive":
                Gtk.gtk_widget_set_sensitive(_view, 0);
                Log.Mark("view insensitive view-refs=" + GObject.RefCount(_view).ToString(CultureInfo.InvariantCulture));
                break;
            case "reset":
                ResetControllers("shutdown");
                break;
            case "hide-first":
#if SPAR_GTK4
                Gtk.gtk_widget_set_visible(_window, 0);
#else
                Gtk.gtk_widget_hide(_window);
#endif
                GLib.g_timeout_add_full(0, 200, &OnDeferredDestroy, 0, 0);
                return;
            case "try-close":
                GObject.Connect(_view, "close", (delegate* unmanaged<nint, nint, void>)&OnViewClose);
                WebKit.webkit_web_view_try_close(_view);
                return;
        }

        DestroyWindow();
    }

    private void Unparent()
    {
#if SPAR_GTK4
        Gtk.gtk_window_set_child(_window, 0);
#else
        Gtk.gtk_container_remove(_window, _view);
#endif
        Log.Mark("view unparented view-refs=" + GObject.RefCount(_view).ToString(CultureInfo.InvariantCulture));
    }

    private void DestroyWindow()
    {
        if (_windowGone)
        {
            return;
        }

#if SPAR_GTK4
        Gtk.gtk_window_destroy(_window);
#else
        Gtk.gtk_widget_destroy(_window);
#endif
        Log.Mark("window destroy called view-refs=" + GObject.RefCount(_view).ToString(CultureInfo.InvariantCulture));
        if (!_windowGone)
        {
            // GTK4 emits "destroy" only at dispose; if something still holds the window, end the loop anyway.
            _windowGone = true;
            GLib.g_main_loop_quit(_loop);
        }
    }

    private void WaitForChildren()
    {
        var started = Log.Monotonic;
        var last = -1;
        while (Log.Monotonic - started < _options.WaitChildren)
        {
            var alive = Descendants.List(Environment.ProcessId);
            if (alive.Count != last)
            {
                last = alive.Count;
                Log.Mark(string.Create(CultureInfo.InvariantCulture, $"children {alive.Count} after {Log.Monotonic - started:F0} ms: {string.Join(' ', alive)}"));
            }

            if (alive.Count == 0)
            {
                Log.Mark(string.Create(CultureInfo.InvariantCulture, $"children-exited after {Log.Monotonic - started:F0} ms"));
                return;
            }

            while (GLib.g_main_context_iteration(0, 0) != 0)
            {
            }

            Thread.Sleep(5);
        }

        Log.Mark($"children-alive after {_options.WaitChildren} ms: {string.Join(' ', Descendants.List(Environment.ProcessId))} view-refs={GObject.RefCount(_view)}");
    }

    // ---- Control ----

    private void StartControlReader(string path)
    {
        var reader = new Thread(() =>
        {
            while (true)
            {
                using var stream = new StreamReader(path);
                while (stream.ReadLine() is { } line)
                {
                    _commands.Enqueue(line.Trim());
                    if (Interlocked.Exchange(ref _drainScheduled, 1) == 0)
                    {
                        GLib.g_idle_add_full(0, &OnDrainCommands, 0, 0);
                    }
                }
            }
        })
        { IsBackground = true, Name = "control" };
        reader.Start();
    }

    private void Execute(string command)
    {
        Log.Mark("command " + command);
        var parts = command.Split(' ', 2);
        switch (parts[0])
        {
            case "load" when _view != 0 && !_shuttingDown:
                Load(parts[1]);
                break;
            case "close" when !_windowGone:
                Gtk.gtk_window_close(_window);
                break;
            case "quit":
                Shutdown("command");
                break;
            case "gc":
                GC.Collect();
                var info = GC.GetGCMemoryInfo();
                Log.Mark(string.Create(CultureInfo.InvariantCulture, $"gc committed={info.TotalCommittedBytes} heap={info.HeapSizeBytes} total-memory={GC.GetTotalMemory(false)} working-set={Environment.WorkingSet}"));
                break;
            case "facts":
                Log.Mark($"facts children={string.Join(' ', Descendants.List(Environment.ProcessId))} view-refs={GObject.RefCount(_view)} window-refs={GObject.RefCount(_window)}");
                break;
        }
    }

    // ---- Drag ----

    private void Press(uint button, uint time, double x, double y, nint device)
    {
        _pressed = true;
        _pressButton = button;
        _pressTime = time;
        _pressX = x;
        _pressY = y;
        _pressDevice = device;
        Log.Mark(string.Create(CultureInfo.InvariantCulture, $"press button={button} at {x:F0},{y:F0} view-refs={GObject.RefCount(_view)}"));
    }

    private void Motion(double x, double y)
    {
        if (!_pressed || Math.Abs(x - _pressX) + Math.Abs(y - _pressY) < 4)
        {
            return;
        }

        _pressed = false;
#if SPAR_GTK4
        var surface = Gtk.gtk_native_get_surface(_window);
        if (_options.Drag == "resize")
        {
            Gtk.gdk_toplevel_begin_resize(surface, 7, _pressDevice, (int)_pressButton, _pressX, _pressY, _pressTime);
        }
        else
        {
            Gtk.gdk_toplevel_begin_move(surface, _pressDevice, (int)_pressButton, _pressX, _pressY, _pressTime);
        }
#else
        if (_options.Drag == "resize")
        {
            Gtk.gtk_window_begin_resize_drag(_window, 7, (int)_pressButton, (int)_pressX, (int)_pressY, _pressTime);
        }
        else
        {
            Gtk.gtk_window_begin_move_drag(_window, (int)_pressButton, (int)_pressX, (int)_pressY, _pressTime);
        }
#endif
        Log.Mark("begin-" + _options.Drag + " view-refs=" + GObject.RefCount(_view).ToString(CultureInfo.InvariantCulture));
        if (_options.ResetAfterBegin)
        {
            // The window manager now owns the pointer, so the press's gesture sequence can never complete here.
            GLib.g_idle_add_full(0, &OnResetAfterBegin, 0, 0);
        }
    }

    /// <summary>Resets each event controller of the view (GTK4), logging the view's reference count around each one.</summary>
    private void ResetControllers(string when)
    {
#if SPAR_GTK4
        var list = Gtk.gtk_widget_observe_controllers(_view);
        var count = Gtk.g_list_model_get_n_items(list);
        var controllers = new List<nint>();
        for (var i = 0u; i < count; i++)
        {
            controllers.Add(Gtk.g_list_model_get_item(list, i));
        }

        GObject.g_object_unref(list);
        foreach (var controller in controllers)
        {
            var before = GObject.RefCount(_view);
            Gtk.gtk_event_controller_reset(controller);
            var name = Marshal.PtrToStringUTF8(Gtk.gtk_event_controller_get_name(controller));
            Log.Mark($"reset {when} {GObject.TypeName(controller)}{(name is null ? "" : " " + name)} view-refs {before} -> {GObject.RefCount(_view)}");
            GObject.g_object_unref(controller);
        }
#else
        Log.Mark("reset " + when + " (GTK4 only)");
#endif
    }

    [UnmanagedCallersOnly]
    private static int OnResetAfterBegin(nint data)
    {
        Current.ResetControllers("after-begin");
        return 0;
    }

    private void Release()
    {
        _pressed = false;
        Log.Mark("release view-refs=" + GObject.RefCount(_view).ToString(CultureInfo.InvariantCulture));
    }

    // ---- Callbacks (never unwind into GLib) ----

    [UnmanagedCallersOnly]
    private static int OnDrainCommands(nint data)
    {
        var host = Current;
        Volatile.Write(ref host._drainScheduled, 0);
        while (host._commands.TryDequeue(out var command))
        {
            try
            {
                host.Execute(command);
            }
            catch (Exception exception)
            {
                Log.Mark("command-exception " + exception);
            }
        }

        return 0;
    }

    [UnmanagedCallersOnly]
    private static int OnGtkReady(nint data)
    {
        Log.Mark("SPAR_READY gtk-only");
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int OnDeferredDestroy(nint data)
    {
        Current.DestroyWindow();
        return 0;
    }

    [UnmanagedCallersOnly]
    private static void OnViewClose(nint view, nint data)
    {
        Log.Mark("view close signal");
        Current.DestroyWindow();
    }

#if SPAR_GTK4
    [UnmanagedCallersOnly]
    private static int OnCloseRequest(nint window, nint data)
    {
        Current.Shutdown("close-request");
        return 1;
    }

    [UnmanagedCallersOnly]
    private static int OnLegacyEvent(nint controller, nint gdkEvent, nint data)
    {
        var host = Current;
        var type = Gtk.gdk_event_get_event_type(gdkEvent);
        Gtk.gdk_event_get_position(gdkEvent, out var x, out var y);
        switch (type)
        {
            case 2: // GDK_BUTTON_PRESS
                host.Press(Gtk.gdk_button_event_get_button(gdkEvent), Gtk.gdk_event_get_time(gdkEvent), x, y, Gtk.gdk_event_get_device(gdkEvent));
                break;
            case 1: // GDK_MOTION_NOTIFY
                host.Motion(x, y);
                break;
            case 3: // GDK_BUTTON_RELEASE
                host.Release();
                break;
        }

        return 0;
    }
#else
    [UnmanagedCallersOnly]
    private static int OnDeleteEvent(nint window, nint gdkEvent, nint data)
    {
        Current.Shutdown("delete-event");
        return 1;
    }

    [UnmanagedCallersOnly]
    private static int OnGtk3Event(nint widget, nint gdkEvent, nint data)
    {
        var host = Current;
        var type = Gtk.gdk_event_get_event_type(gdkEvent);
        Gtk.gdk_event_get_root_coords(gdkEvent, out var x, out var y);
        switch (type)
        {
            case 4: // GDK_BUTTON_PRESS
                Gtk.gdk_event_get_button(gdkEvent, out var button);
                host.Press(button, Gtk.gdk_event_get_time(gdkEvent), x, y, 0);
                break;
            case 3: // GDK_MOTION_NOTIFY
                host.Motion(x, y);
                break;
            case 7: // GDK_BUTTON_RELEASE
                host.Release();
                break;
        }

        return 0;
    }
#endif

    [UnmanagedCallersOnly]
    private static void OnDestroy(nint widget, nint data)
    {
        var host = Current;
        Log.Mark("window destroy signal");
        if (!host._windowGone)
        {
            host._windowGone = true;
            GLib.g_main_loop_quit(host._loop);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnWeakNotify(nint data, nint instance)
    {
        Log.Mark("disposed " + Marshal.PtrToStringUTF8(data));
    }

    [UnmanagedCallersOnly]
    private static void OnFinalizeData(nint data)
    {
        Log.Mark("finalized " + Marshal.PtrToStringUTF8(data));
        Marshal.FreeCoTaskMem(data);
    }

    [UnmanagedCallersOnly]
    private static void OnLoadChanged(nint view, int loadEvent, nint data)
    {
        if (loadEvent == 3)
        {
            // The empty page runs no script, so its ready mark is the end of the load.
            Log.Mark(Current._page == "empty" ? "load-finished SPAR_READY empty" : "load-finished");
        }
    }

    [UnmanagedCallersOnly]
    private static void OnWebProcessTerminated(nint view, int reason, nint data) =>
        Log.Mark("web-process-terminated reason=" + reason.ToString(CultureInfo.InvariantCulture));

    [UnmanagedCallersOnly]
    private static void OnScriptMessage(nint manager, nint message, nint data)
    {
#if SPAR_GTK4
        var value = message;
#else
        var value = WebKit.webkit_javascript_result_get_js_value(message);
#endif
        var text = WebKit.jsc_value_to_string(value);
        Log.Mark("page " + Marshal.PtrToStringUTF8(text));
        GLib.g_free(text);
    }

    [UnmanagedCallersOnly]
    private static void OnSchemeRequest(nint request, nint data)
    {
        try
        {
            Current.Serve(request);
        }
        catch (Exception exception)
        {
            Log.Mark("scheme-exception " + exception);
        }
    }

    private void Serve(nint request)
    {
        var path = Marshal.PtrToStringUTF8(WebKit.webkit_uri_scheme_request_get_path(request)) ?? "/";
        var root = _options.WebDirectory;
        var file = Path.GetFullPath(Path.Combine(root, path.TrimStart('/')));
        var found = file.StartsWith(root, StringComparison.Ordinal) && File.Exists(file);
        var content = found ? File.ReadAllBytes(file) : "not found"u8.ToArray();
        var type = !found ? "text/plain" : Path.GetExtension(file) switch
        {
            ".html" => "text/html",
            ".js" => "text/javascript",
            ".css" => "text/css",
            _ => "application/octet-stream",
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
}
