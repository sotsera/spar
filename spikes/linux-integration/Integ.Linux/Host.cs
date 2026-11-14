using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Integ.Linux;

/// <summary>
/// The spike host: one GTK thread owns the window, the web view, the tray item and the GLib main loop. Scripts drive it
/// through a control FIFO (one command per line) and read its marks on standard output.
/// </summary>
internal sealed partial class Host
{
    private const string Origin = "spar://app";

    private static Host? s_current;

    private readonly Options _options;
    private readonly string _csp =
        "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self'; connect-src 'self'; " +
        "base-uri 'none'; form-action 'none'; frame-ancestors 'none'; object-src 'none'";

    private GLibContext? _ui;
    private nint _window;
    private nint _webView;
    private nint _webContext;
    private nint _loop;
    private nint _press;
    private double _pressAt;
    private bool _buttonHeld;
    private bool _quitting;
    private bool _hidden;
    private double _presentedAt;
    private int _lastState = -1;
#if SPAR_GTK4
    private bool _stateConnected;
#endif
    private Tray? _tray;
#if !SPAR_GTK4
    private (int Width, int Height) _normalSize;
#endif
    private string? _pageRects;

    public Host(Options options)
    {
        _options = options;
    }

    private static Host Current => s_current!;

    public int Run()
    {
        var thread = new Thread(RunOnUiThread) { Name = "Spar UI" };
        thread.Start();
        thread.Join();
        return 0;
    }

    private unsafe void RunOnUiThread()
    {
        s_current = this;
#if SPAR_GTK4
        if (Gtk.gtk_init_check() == 0)
#else
        if (Gtk.gtk_init_check(0, 0) == 0)
#endif
        {
            Log.Mark("error gtk_init_check failed (no display)");
            return;
        }

        _ui = new GLibContext();
        SynchronizationContext.SetSynchronizationContext(_ui);
        Log.Mark($"gtk-init display={GObject.TypeName(Gtk.gdk_display_get_default())}");
        CreateWindow();
        CreateWebView();
        WebKit.webkit_web_view_load_uri(_webView, $"{Origin}/index.html");
        RestorePlacement();
        if (!_options.StartHidden)
        {
            Show();
        }

        if (_options.Tray)
        {
            _tray = new Tray(_options.TrayId, OnTrayAction);
            _tray.Start();
        }

        if (_options.Control is { } control)
        {
            StartControlReader(control);
        }

        _loop = GLib.g_main_loop_new(0, 0);
        GLib.g_main_loop_run(_loop);
        Log.Mark("main-loop-ended");
        _tray?.Stop();
        GObject.g_object_unref(_webContext);
        _webContext = 0;
        WaitForChildren();
        GLib.g_main_loop_unref(_loop);
        _ui.Dispose();
        Log.Mark("exit");
    }

    // ---- Window ----

    private unsafe void CreateWindow()
    {
#if SPAR_GTK4
        _window = Gtk.gtk_window_new();
#else
        _window = Gtk.gtk_window_new(0);
#endif
        Gtk.gtk_window_set_title(_window, _options.Title);
        Gtk.gtk_window_set_default_size(_window, _options.Width, _options.Height);
#if !SPAR_GTK4
        _normalSize = (_options.Width, _options.Height);
#endif
        switch (_options.Frame)
        {
            case "none":
                Gtk.gtk_window_set_decorated(_window, 0);
                break;
            case "titlebar":
                // An empty titlebar widget: GTK keeps its client-side frame (shadow and resize edges) without a title bar.
                var empty = Gtk.gtk_box_new(0, 0);
#if SPAR_GTK4
                Gtk.gtk_widget_set_visible(empty, 0); // a visible empty box still gets the theme's 46 px title bar height
#endif
                Gtk.gtk_window_set_titlebar(_window, empty);
                break;
        }

        GObject.Connect(_window, "destroy", (delegate* unmanaged<nint, nint, void>)&OnDestroy);
        GObject.Connect(_window, "notify::is-active", (delegate* unmanaged<nint, nint, nint, void>)&OnActive);
#if SPAR_GTK4
        GObject.Connect(_window, "close-request", (delegate* unmanaged<nint, nint, int>)&OnCloseRequest);
        GObject.Connect(_window, "notify::default-width", (delegate* unmanaged<nint, nint, nint, void>)&OnDefaultSize);
#else
        GObject.Connect(_window, "delete-event", (delegate* unmanaged<nint, nint, nint, int>)&OnDeleteEvent);
        GObject.Connect(_window, "window-state-event", (delegate* unmanaged<nint, nint, nint, int>)&OnWindowState);
        GObject.Connect(_window, "configure-event", (delegate* unmanaged<nint, nint, nint, int>)&OnConfigure);
#endif
        if (_options.WindowCss)
        {
            // The page is the whole window: without a native title bar the CSD frame is the only GTK chrome left.
            var provider = Gtk.gtk_css_provider_new();
#if SPAR_GTK4
            Gtk.gtk_css_provider_load_from_string(provider, "window { background-color: #1f1f1f; }");
            Gtk.gtk_style_context_add_provider_for_display(Gtk.gdk_display_get_default(), provider, 600);
#else
            Gtk.gtk_css_provider_load_from_data(provider, "window { background-color: #1f1f1f; }", -1, out _);
            Gtk.gtk_style_context_add_provider_for_screen(Gtk.gdk_screen_get_default(), provider, 600);
#endif
        }

        Log.Mark($"window-created frame={_options.Frame}");
    }

    private unsafe void Show()
    {
#if SPAR_GTK4
        Gtk.gtk_window_present(_window);
        var surface = Gtk.gtk_native_get_surface(_window);
        if (surface != 0 && !_stateConnected)
        {
            _stateConnected = true;
            GObject.Connect(surface, "notify::state", (delegate* unmanaged<nint, nint, nint, void>)&OnSurfaceState);
        }
#else
        Gtk.gtk_widget_show(_window);
#endif
        Log.Mark("shown");
    }

    private void Hide()
    {
#if SPAR_GTK4
        Gtk.gtk_widget_set_visible(_window, 0);
#else
        Gtk.gtk_widget_hide(_window);
#endif
        _hidden = true;
        Log.Mark("hidden");
    }

    private void Present(string reason)
    {
        _presentedAt = Log.Monotonic;
        var wasHidden = _hidden;
        _hidden = false;
        Gtk.gtk_window_present(_window);
        Log.Mark($"present ({reason}) was-hidden={wasHidden}");
    }

    private void Quit(string reason)
    {
        if (_quitting)
        {
            return;
        }

        _quitting = true;
        Log.Mark("quit " + reason);
        SavePlacement();
#if SPAR_GTK4
        Gtk.gtk_window_destroy(_window);
#else
        Gtk.gtk_widget_destroy(_window);
#endif
    }

    private void RequestClose(string reason)
    {
        Log.Mark("close-requested " + reason);
        Gtk.gtk_window_close(_window);
    }

    /// <summary>The window's close path (page button, window manager, tray): hide to the tray, or quit.</summary>
    private bool HandleClose()
    {
        if (_quitting)
        {
            return false;
        }

        if (_options.CloseToTray && _tray is not null)
        {
            Log.Mark("close-to-tray");
            Hide();
            return true;
        }

        Quit("window close");
        return true;
    }

    private void Minimize()
    {
#if SPAR_GTK4
        Gtk.gtk_window_minimize(_window);
#else
        Gtk.gtk_window_iconify(_window);
#endif
        Log.Mark("minimize called");
    }

    private void ToggleMaximize(string reason)
    {
        var maximized = Gtk.gtk_window_is_maximized(_window) != 0;
        Log.Mark($"toggle-maximize ({reason}) was={maximized}");
        if (maximized)
        {
            Gtk.gtk_window_unmaximize(_window);
        }
        else
        {
            Gtk.gtk_window_maximize(_window);
        }
    }

    private bool IsX11 => GObject.TypeName(Gtk.gdk_display_get_default()).Contains("X11", StringComparison.Ordinal);

    private nuint Xid()
    {
#if SPAR_GTK4
        var surface = Gtk.gtk_native_get_surface(_window);
        return surface == 0 ? 0 : Gtk.gdk_x11_surface_get_xid(surface);
#else
        var window = Gtk.gtk_widget_get_window(_window);
        return window == 0 ? 0 : Gtk.gdk_x11_window_get_xid(window);
#endif
    }

    /// <summary>The window's root position on X11 (the X window that GTK owns, including any client-side shadow).</summary>
    private (int X, int Y)? X11Origin()
    {
        if (!IsX11 || Xid() is var xid && xid == 0)
        {
            return null;
        }

        var display = Gtk.gdk_x11_display_get_xdisplay(Gtk.gdk_display_get_default());
        X11.XTranslateCoordinates(display, xid, X11.XDefaultRootWindow(display), 0, 0, out var x, out var y, out _);
        return (x, y);
    }

    private int State()
    {
#if SPAR_GTK4
        var surface = Gtk.gtk_native_get_surface(_window);
        return surface == 0 ? 0 : Gtk.gdk_toplevel_get_state(surface);
#else
        var window = Gtk.gtk_widget_get_window(_window);
        return window == 0 ? 0 : Gtk.gdk_window_get_state(window);
#endif
    }

    private static string StateText(int state)
    {
        var parts = new List<string>();
#if SPAR_GTK4
        if ((state & Gtk.StateMinimized) != 0) parts.Add("minimized");
#else
        if ((state & Gtk.StateIconified) != 0) parts.Add("minimized");
        if ((state & Gtk.StateWithdrawn) != 0) parts.Add("withdrawn");
#endif
        if ((state & Gtk.StateMaximized) != 0) parts.Add("maximized");
        if ((state & Gtk.StateFullscreen) != 0) parts.Add("fullscreen");
        if ((state & Gtk.StateFocused) != 0) parts.Add("focused");
        return parts.Count == 0 ? "normal" : string.Join('+', parts);
    }

    private void StateChanged(string source)
    {
        var state = State();
        if (state == _lastState)
        {
            return;
        }

        _lastState = state;
        var maximized = (state & Gtk.StateMaximized) != 0;
        var text = StateText(state);
        Log.Mark($"window-state {text} ({source})");
        Deliver($$"""{"t":"state","state":"{{text}}","maximized":{{(maximized ? "true" : "false")}}}""");
    }

    private unsafe string Facts()
    {
        var builder = new StringBuilder("facts:");
        builder.Append(CultureInfo.InvariantCulture, $" display={GObject.TypeName(Gtk.gdk_display_get_default())} visible={Gtk.gtk_widget_get_visible(_window)} mapped={Gtk.gtk_widget_get_mapped(_window)} maximized={Gtk.gtk_window_is_maximized(_window)} active={Gtk.gtk_window_is_active(_window)} state={StateText(State())}");
#if SPAR_GTK4
        Gtk.gtk_window_get_default_size(_window, out var dw, out var dh);
        var point = new GraphenePoint();
        GraphenePoint view;
        Gtk.gtk_widget_compute_point(_webView, _window, &point, &view);
        Gtk.gtk_native_get_surface_transform(_window, out var sx, out var sy);
        builder.Append(CultureInfo.InvariantCulture, $" csd={Gtk.gtk_widget_has_css_class(_window, "csd")} default-size={dw}x{dh} window-widget={Gtk.gtk_widget_get_width(_window)}x{Gtk.gtk_widget_get_height(_window)} view={Gtk.gtk_widget_get_width(_webView)}x{Gtk.gtk_widget_get_height(_webView)} view-in-surface={view.X + sx:F0},{view.Y + sy:F0}");
#else
        Gtk.gtk_window_get_size(_window, out var w, out var h);
        Gtk.gtk_window_get_position(_window, out var px, out var py);
        Gtk.gtk_widget_translate_coordinates(_webView, _window, 0, 0, out var vx, out var vy);
        builder.Append(CultureInfo.InvariantCulture, $" csd={Gtk.gtk_style_context_has_class(Gtk.gtk_widget_get_style_context(_window), "csd")} composited={Gtk.gdk_screen_is_composited(Gtk.gdk_screen_get_default())} size={w}x{h} position={px},{py} window-widget={Gtk.gtk_widget_get_allocated_width(_window)}x{Gtk.gtk_widget_get_allocated_height(_window)} view={Gtk.gtk_widget_get_allocated_width(_webView)}x{Gtk.gtk_widget_get_allocated_height(_webView)} view-in-surface={vx},{vy}");
#endif
        if (X11Origin() is { } origin)
        {
            builder.Append(CultureInfo.InvariantCulture, $" x11-xid=0x{Xid():x} x11-root={origin.X},{origin.Y}");
        }

        return builder.ToString();
    }

    // ---- Title bar: move, resize, window menu ----

    private bool PressUsable(string what)
    {
        if (_press == 0)
        {
            Log.Mark($"{what} refused: no press recorded");
            return false;
        }

        if (!_buttonHeld)
        {
            Log.Mark($"{what} refused: button already released");
            return false;
        }

        return true;
    }

    private void BeginMove(JsonElement message)
    {
        var received = Log.Monotonic;
        if (!PressUsable("begin-move"))
        {
            return;
        }

        var time = Gtk.gdk_event_get_time(_press);
#if SPAR_GTK4
        var button = (int)Gtk.gdk_button_event_get_button(_press);
        Gtk.gdk_event_get_position(_press, out var x, out var y);
        Gtk.gdk_toplevel_begin_move(Gtk.gtk_native_get_surface(_window), Gtk.gdk_event_get_device(_press), button, x, y, time);
#else
        Gtk.gdk_event_get_button(_press, out var unsignedButton);
        var button = (int)unsignedButton;
        Gtk.gdk_event_get_root_coords(_press, out var x, out var y);
        Gtk.gtk_window_begin_move_drag(_window, button, (int)x, (int)y, time);
#endif
        var called = Log.Monotonic;
        Log.Mark(string.Create(CultureInfo.InvariantCulture,
            $"begin-move mode={message.GetProperty("mode").GetString()} button={button} at={x:F0},{y:F0} event-time={time} press->message={received - _pressAt:F1}ms press->call={called - _pressAt:F1}ms call={called - received:F2}ms"));
    }

    private void BeginResize(JsonElement message)
    {
        var received = Log.Monotonic;
        if (!PressUsable("begin-resize"))
        {
            return;
        }

        var edge = message.GetProperty("edge").GetInt32();
        var time = Gtk.gdk_event_get_time(_press);
#if SPAR_GTK4
        var button = (int)Gtk.gdk_button_event_get_button(_press);
        Gtk.gdk_event_get_position(_press, out var x, out var y);
        Gtk.gdk_toplevel_begin_resize(Gtk.gtk_native_get_surface(_window), edge, Gtk.gdk_event_get_device(_press), button, x, y, time);
#else
        Gtk.gdk_event_get_button(_press, out var unsignedButton);
        var button = (int)unsignedButton;
        Gtk.gdk_event_get_root_coords(_press, out var x, out var y);
        Gtk.gtk_window_begin_resize_drag(_window, edge, button, (int)x, (int)y, time);
#endif
        Log.Mark(string.Create(CultureInfo.InvariantCulture, $"begin-resize edge={edge} button={button} at={x:F0},{y:F0} press->call={Log.Monotonic - _pressAt:F1}ms message->call={Log.Monotonic - received:F2}ms"));
    }

    private void ShowWindowMenu()
    {
        if (_press == 0)
        {
            Log.Mark("window-menu refused: no press recorded");
            return;
        }

#if SPAR_GTK4
        var shown = Gtk.gdk_toplevel_show_window_menu(Gtk.gtk_native_get_surface(_window), _press);
#else
        var shown = Gtk.gdk_window_show_window_menu(Gtk.gtk_widget_get_window(_window), _press);
#endif
        Log.Mark($"window-menu shown={shown} (0: the compositor or window manager offers no window menu)");
    }

    /// <summary>Keeps the latest button press on the web view (before WebKit sees it) for the move, resize and menu calls.</summary>
    private void RecordPress(nint gdkEvent, string kind)
    {
        var type = Gtk.gdk_event_get_event_type(gdkEvent);
        if (type == Gtk.EventButtonRelease)
        {
            _buttonHeld = false;
            return;
        }

        if (type != Gtk.EventButtonPress)
        {
            return;
        }

        if (_press != 0)
        {
#if SPAR_GTK4
            Gtk.gdk_event_unref(_press);
#else
            Gtk.gdk_event_free(_press);
#endif
        }

#if SPAR_GTK4
        _press = Gtk.gdk_event_ref(gdkEvent);
        var button = Gtk.gdk_button_event_get_button(gdkEvent);
        Gtk.gdk_event_get_position(gdkEvent, out var x, out var y);
#else
        _press = Gtk.gdk_event_copy(gdkEvent);
        Gtk.gdk_event_get_button(gdkEvent, out var button);
        Gtk.gdk_event_get_coords(gdkEvent, out var x, out var y);
#endif
        _pressAt = Log.Monotonic;
        _buttonHeld = true;
        Log.Mark(string.Create(CultureInfo.InvariantCulture, $"press ({kind}) button={button} at={x:F0},{y:F0} time={Gtk.gdk_event_get_time(gdkEvent)}"));
    }

    // ---- Web view ----

    private unsafe void CreateWebView()
    {
        var dataPath = Path.Combine(_options.DataDirectory, "data");
        var cachePath = Path.Combine(_options.DataDirectory, "cache");
#if SPAR_GTK4
        var session = WebKit.webkit_network_session_new(dataPath, cachePath);
        _webContext = WebKit.webkit_web_context_new();
        RegisterScheme();
        _webView = GObject.New(
            WebKit.webkit_web_view_get_type(),
            new("web-context", null, _webContext, WebKit.webkit_web_context_get_type()),
            new("network-session", null, session, WebKit.webkit_network_session_get_type()));
        GObject.g_object_unref(session);
#else
        var manager = GObject.New(
            WebKit.webkit_website_data_manager_get_type(),
            new("base-data-directory", dataPath),
            new("base-cache-directory", cachePath));
        _webContext = WebKit.webkit_web_context_new_with_website_data_manager(manager);
        GObject.g_object_unref(manager);
        WebKit.webkit_web_context_set_sandbox_enabled(_webContext, 1);
        RegisterScheme();
        _webView = GObject.New(WebKit.webkit_web_view_get_type(), new GProperty("web-context", null, _webContext, WebKit.webkit_web_context_get_type()));
#endif
        var settings = WebKit.webkit_web_view_get_settings(_webView);
        // Software GPU under WSLg: acceleration off was the fastest mode in the earlier spike.
        WebKit.webkit_settings_set_hardware_acceleration_policy(settings, WebKit.AccelerationNever);
        if (_options.ClipboardAccess)
        {
            WebKit.webkit_settings_set_javascript_can_access_clipboard(settings, 1);
        }

        WebKit.webkit_web_view_set_background_color(_webView, new GdkRgba { Red = 0.12, Green = 0.12, Blue = 0.12, Alpha = 1 });
        var contentManager = WebKit.webkit_web_view_get_user_content_manager(_webView);
#if SPAR_GTK4
        WebKit.webkit_user_content_manager_register_script_message_handler(contentManager, "spar", null);
#else
        WebKit.webkit_user_content_manager_register_script_message_handler(contentManager, "spar");
#endif
        GObject.Connect(contentManager, "script-message-received::spar", (delegate* unmanaged<nint, nint, nint, void>)&OnScriptMessage);
        var script = WebKit.webkit_user_script_new(
            Bridge.Script(_options.DragMode, _options.RegionMode, _options.Edges, _options.Threshold),
            WebKit.InjectTopFrame, WebKit.InjectAtDocumentStart, 0, 0);
        WebKit.webkit_user_content_manager_add_script(contentManager, script);
        WebKit.webkit_user_script_unref(script);
        GObject.Connect(_webView, "web-process-terminated", (delegate* unmanaged<nint, int, nint, void>)&OnWebProcessTerminated);
        GObject.Connect(_webView, "permission-request", (delegate* unmanaged<nint, nint, nint, int>)&OnPermissionRequest);
        GObject.Connect(_webView, "context-menu", (delegate* unmanaged<nint, nint, nint, nint, int>)&OnContextMenu);
#if SPAR_GTK4
        var controller = Gtk.gtk_event_controller_legacy_new();
        Gtk.gtk_event_controller_set_propagation_phase(controller, 1); // GTK_PHASE_CAPTURE: before WebKit's own handling
        GObject.Connect(controller, "event", (delegate* unmanaged<nint, nint, nint, int>)&OnLegacyEvent);
        Gtk.gtk_widget_add_controller(_webView, controller);
        Gtk.gtk_window_set_child(_window, _webView);
#else
        GObject.Connect(_webView, "button-press-event", (delegate* unmanaged<nint, nint, nint, int>)&OnButtonEvent);
        GObject.Connect(_webView, "button-release-event", (delegate* unmanaged<nint, nint, nint, int>)&OnButtonEvent);
        Gtk.gtk_container_add(_window, _webView);
        Gtk.gtk_widget_show(_webView);
#endif
        Gtk.gtk_widget_grab_focus(_webView);
        Log.Mark($"view-created drag={_options.DragMode} region={_options.RegionMode} edges={_options.Edges}");
    }

    private unsafe void RegisterScheme()
    {
        WebKit.webkit_web_context_register_uri_scheme(_webContext, "spar", &OnSchemeRequest, 0, 0);
        WebKit.webkit_security_manager_register_uri_scheme_as_secure(WebKit.webkit_web_context_get_security_manager(_webContext), "spar");
    }

    private unsafe void Deliver(string json)
    {
        if (_webView == 0)
        {
            return;
        }

        var bytes = Encoding.UTF8.GetBytes($"window.__sparDeliver && window.__sparDeliver({json});");
        fixed (byte* script = bytes)
        {
            WebKit.webkit_web_view_evaluate_javascript(_webView, script, bytes.Length, 0, 0, 0, null, 0);
        }
    }

    private void OnMessage(string text)
    {
        using var json = JsonDocument.Parse(text);
        var root = json.RootElement;
        var type = root.GetProperty("t").GetString();
        switch (type)
        {
            case "drag":
                BeginMove(root);
                break;
            case "resize":
                BeginResize(root);
                break;
            case "toggle-maximize":
                ToggleMaximize("page " + root.GetProperty("via").GetString());
                break;
            case "window-menu":
                ShowWindowMenu();
                break;
            case "cmd":
                Command(root.GetProperty("c").GetString() ?? "", "page");
                break;
            case "ready":
                _pageRects = text;
                Log.Mark("ui-ready " + text);
                _lastState = int.MinValue; // deliver the current state now that the page listens
                StateChanged("ready");
                break;
            case "shown-frame":
                Log.Mark(string.Create(CultureInfo.InvariantCulture, $"page-frame-after-present present->message={Log.Monotonic - _presentedAt:F1}ms {text}"));
                break;
            default:
                Log.Mark("page " + text);
                break;
        }
    }

    // ---- Commands (control FIFO, page buttons, tray) ----

    private void StartControlReader(string path)
    {
        var context = _ui!;
        var thread = new Thread(() =>
        {
            while (true)
            {
                using var reader = new StreamReader(path);
                while (reader.ReadLine() is { } line)
                {
                    context.Post(static state => { var (host, command) = ((Host, string))state!; host.Command(command, "control"); }, (this, line));
                }
            }
        })
        { IsBackground = true, Name = "control" };
        thread.Start();
    }

    private void Command(string line, string source)
    {
        var space = line.IndexOf(' ', StringComparison.Ordinal);
        var name = space < 0 ? line : line[..space];
        var argument = space < 0 ? "" : line[(space + 1)..];
        Log.Mark($"command {name} from {source}");
        switch (name)
        {
            case "facts":
                Log.Mark(Facts());
                break;
            case "rects":
                Log.Mark("rects " + _pageRects);
                break;
            case "minimize":
                Minimize();
                break;
            case "maximize":
                Gtk.gtk_window_maximize(_window);
                break;
            case "unmaximize":
                Gtk.gtk_window_unmaximize(_window);
                break;
            case "toggle-maximize":
                ToggleMaximize(source);
                break;
            case "unminimize":
#if SPAR_GTK4
                Gtk.gtk_window_unminimize(_window);
#else
                Gtk.gtk_window_deiconify(_window);
#endif
                break;
            case "present":
                Present(source);
                break;
            case "hide":
                Hide();
                break;
            case "hide-unrealize":
                // WSLg keeps a GTK4 window on screen after hide (GTK4 keeps the wl_surface); unrealizing destroys it.
                Hide();
                Gtk.gtk_widget_unrealize(_window);
                Log.Mark("unrealized");
                break;
            case "close":
                RequestClose(source);
                break;
            case "quit":
                Quit(source);
                break;
            case "move":
                Move(argument);
                break;
            case "resize":
                Resize(argument);
                break;
            case "eval":
                Deliver($$"""{"t":"eval","code":{{JsonSerializer.Serialize(argument, HostJson.Default.String)}}}""");
                break;
            case "print":
                Print(argument);
                break;
            case "pick":
                Pick(int.Parse(argument, CultureInfo.InvariantCulture));
                break;
            case "clip-set":
                ClipboardSet(argument);
                break;
            case "clip-get":
                ClipboardGet();
                break;
            case "tray-status":
                _tray?.SetStatusText(argument);
                break;
            case "tray-icon":
                _tray?.SetIcon(argument);
                break;
            case "tray-attention":
                _tray?.SetStatus(argument.Length == 0 ? "NeedsAttention" : argument);
                break;
            case "launcher":
                Presence.EmitLauncherEntry(argument);
                break;
            default:
                Log.Mark("unknown command " + line);
                break;
        }
    }

    private void OnTrayAction(string action)
    {
        switch (action)
        {
            case "activate":
            case "restore":
                if (_hidden || Gtk.gtk_widget_get_visible(_window) == 0)
                {
                    Present("tray " + action);
                }
                else if (action == "activate" && _options.ActivateToggles)
                {
                    Hide();
                }
                else
                {
                    Present("tray " + action);
                }

                break;
            case "quit":
                Quit("tray menu");
                break;
        }
    }

    private void Move(string argument)
    {
        var parts = argument.Split(' ');
        var x = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var y = int.Parse(parts[1], CultureInfo.InvariantCulture);
#if SPAR_GTK4
        // GTK4 has no move API. On X11 the X window can still be moved directly.
        if (IsX11 && Xid() is var xid && xid != 0)
        {
            var display = Gtk.gdk_x11_display_get_xdisplay(Gtk.gdk_display_get_default());
            X11.XMoveWindow(display, xid, x, y);
            X11.XFlush(display);
            Log.Mark($"move {x},{y} via XMoveWindow");
        }
        else
        {
            Log.Mark("move unsupported (GTK4 has no move API; Wayland gives clients no position)");
        }
#else
        Gtk.gtk_window_move(_window, x, y);
        Log.Mark($"move {x},{y} via gtk_window_move");
#endif
    }

    private void Resize(string argument)
    {
        var parts = argument.Split(' ');
        var w = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var h = int.Parse(parts[1], CultureInfo.InvariantCulture);
#if SPAR_GTK4
        Gtk.gtk_window_set_default_size(_window, w, h);
#else
        Gtk.gtk_window_resize(_window, w, h);
#endif
        Log.Mark($"resize {w}x{h}");
    }

    // ---- Signal handlers ----

    [UnmanagedCallersOnly]
    private static void OnScriptMessage(nint contentManager, nint value, nint data)
    {
        try
        {
#if !SPAR_GTK4
            value = WebKit.webkit_javascript_result_get_js_value(value);
#endif
            if (GLib.TakeString(Jsc.jsc_value_to_string(value)) is { } message)
            {
                Current.OnMessage(message);
            }
        }
        catch (Exception exception)
        {
            Log.Mark("message-exception " + exception);
        }
    }

#if SPAR_GTK4
    [UnmanagedCallersOnly]
    private static int OnLegacyEvent(nint controller, nint gdkEvent, nint data)
    {
        Current.RecordPress(gdkEvent, "legacy-capture");
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int OnCloseRequest(nint window, nint data) => Current.HandleClose() ? 1 : 0;

    [UnmanagedCallersOnly]
    private static void OnSurfaceState(nint surface, nint spec, nint data) => Current.StateChanged("surface notify::state");

    [UnmanagedCallersOnly]
    private static void OnDefaultSize(nint window, nint spec, nint data)
    {
        // GTK4 keeps the unmaximized size in the default size.
    }
#else
    [UnmanagedCallersOnly]
    private static int OnButtonEvent(nint widget, nint gdkEvent, nint data)
    {
        Current.RecordPress(gdkEvent, "button-event");
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int OnDeleteEvent(nint widget, nint gdkEvent, nint data) => Current.HandleClose() ? 1 : 0;

    [UnmanagedCallersOnly]
    private static int OnWindowState(nint widget, nint gdkEvent, nint data)
    {
        Current.StateChanged("window-state-event");
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int OnConfigure(nint widget, nint gdkEvent, nint data)
    {
        var host = Current;
        if (Gtk.gtk_window_is_maximized(widget) == 0)
        {
            Gtk.gtk_window_get_size(widget, out var w, out var h);
            host._normalSize = (w, h);
        }

        return 0;
    }
#endif

    [UnmanagedCallersOnly]
    private static void OnActive(nint window, nint spec, nint data)
    {
        var host = Current;
        Log.Mark("is-active=" + Gtk.gtk_window_is_active(window));
        host.StateChanged("is-active");
    }

    [UnmanagedCallersOnly]
    private static int OnPermissionRequest(nint webView, nint request, nint data)
    {
        var host = Current;
        var type = GObject.TypeName(request);
        if (host._options.AllowClipboardPermission && type.Contains("Clipboard", StringComparison.Ordinal))
        {
            WebKit.webkit_permission_request_allow(request);
            Log.Mark($"permission-request {type} -> allowed (clipboard override)");
        }
        else
        {
            WebKit.webkit_permission_request_deny(request);
            Log.Mark($"permission-request {type} -> denied");
        }

        return 1;
    }

    [UnmanagedCallersOnly]
    private static int OnContextMenu(nint webView, nint menu, nint eventOrHit, nint data)
    {
        // Never show WebKit's own menu (production default); it could also wait for a person.
        Log.Mark("webkit context-menu suppressed");
        return 1;
    }

    [UnmanagedCallersOnly]
    private static void OnWebProcessTerminated(nint webView, int reason, nint data) =>
        Log.Mark("web-process-terminated reason=" + reason.ToString(CultureInfo.InvariantCulture));

    [UnmanagedCallersOnly]
    private static void OnDestroy(nint widget, nint data)
    {
        var host = Current;
        Log.Mark("window-destroyed");
        host._webView = 0;
        host._window = 0;
        GLib.g_main_loop_quit(host._loop);
    }

    // ---- The custom scheme ----

    [UnmanagedCallersOnly]
    private static void OnSchemeRequest(nint request, nint data)
    {
        try
        {
            Current.HandleSchemeRequest(request);
        }
        catch (Exception exception)
        {
            Log.Mark("scheme-exception " + exception);
        }
    }

    private void HandleSchemeRequest(nint request)
    {
        var path = Marshal.PtrToStringUTF8(WebKit.webkit_uri_scheme_request_get_path(request)) ?? "/";
        var root = _options.WebDirectory;
        var file = Path.GetFullPath(Path.Combine(root, path.TrimStart('/')));
        if (!file.StartsWith(root, StringComparison.Ordinal) || !File.Exists(file))
        {
            Respond(request, 404, "text/plain", "not found"u8.ToArray(), false);
            return;
        }

        Respond(request, 200, ContentType(file), File.ReadAllBytes(file), file.EndsWith(".html", StringComparison.Ordinal));
    }

    private void Respond(nint request, uint status, string contentType, byte[] content, bool html)
    {
        var stream = Gio.MemoryStream(content);
        var response = WebKit.webkit_uri_scheme_response_new(stream, content.Length);
        WebKit.webkit_uri_scheme_response_set_status(response, status, status == 200 ? "OK" : "Not Found");
        WebKit.webkit_uri_scheme_response_set_content_type(response, contentType);
        var headers = Soup.soup_message_headers_new(1);
        Soup.soup_message_headers_append(headers, "Cache-Control", "no-store");
        Soup.soup_message_headers_append(headers, "Content-Type", contentType);
        Soup.soup_message_headers_append(headers, "X-Content-Type-Options", "nosniff");
        if (html)
        {
            Soup.soup_message_headers_append(headers, "Content-Security-Policy", _csp);
        }

        WebKit.webkit_uri_scheme_response_set_http_headers(response, headers);
        WebKit.webkit_uri_scheme_request_finish_with_response(request, response);
        GObject.g_object_unref(response);
        GObject.g_object_unref(stream);
    }

    private static string ContentType(string path) => Path.GetExtension(path) switch
    {
        ".html" => "text/html",
        ".js" => "text/javascript",
        ".css" => "text/css",
        _ => "application/octet-stream",
    };

    // ---- Shutdown ----

    private void WaitForChildren()
    {
        var started = Log.Monotonic;
        while (Log.Monotonic - started < 10000)
        {
            if (Processes.Descendants(Environment.ProcessId) == 0)
            {
                Log.Mark(string.Create(CultureInfo.InvariantCulture, $"children-exited after {Log.Monotonic - started:F0} ms"));
                return;
            }

            while (GLib.g_main_context_iteration(0, 0) != 0)
            {
            }

            Thread.Sleep(2);
        }

        Log.Mark($"children still alive after 10 s: {Processes.Descendants(Environment.ProcessId)}");
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(string))]
[System.Text.Json.Serialization.JsonSerializable(typeof(Placement))]
internal sealed partial class HostJson : System.Text.Json.Serialization.JsonSerializerContext;
