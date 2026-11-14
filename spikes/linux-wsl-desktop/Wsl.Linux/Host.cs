using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Wsl.Linux;

/// <summary>
/// Spike host for WebKitGTK on a real desktop (WSLg). One thread runs GTK, the view and the GLib main loop. Marks use
/// CLOCK_MONOTONIC (Stopwatch) so the X11 capture can be merged. Window facts (backend, decorations, scale, monitor,
/// focus) are recorded natively; page facts (devicePixelRatio, hasFocus, frame timing, input) come from the probe page.
/// Theme layers are the ones from the theme start-up spike:
///   css          a GTK CSS provider paints the window background;
///   prefer-dark  GtkSettings gtk-application-prefer-dark-theme;
///   gtk-theme    GtkSettings gtk-theme-name Adwaita-dark / Adwaita;
///   wv-bg        webkit_web_view_set_background_color (opaque);
///   transparent  webkit_web_view_set_background_color with alpha 0.
/// </summary>
internal sealed class Host
{
    public const string Origin = "spar://app";

    // Presents WebKit's message handler with the WebView2 shape the page uses. Top frame only, at document start.
    private const string Shim = """
        (() => {
          const handler = window.webkit.messageHandlers.spar;
          const target = new EventTarget();
          window.chrome = window.chrome || {};
          window.chrome.webview = {
            postMessage: message => handler.postMessage(message),
            addEventListener: (type, listener) => target.addEventListener(type, listener),
            removeEventListener: (type, listener) => target.removeEventListener(type, listener),
          };
          Object.defineProperty(window, "__sparDeliver", {
            value: messages => { for (const data of messages) target.dispatchEvent(new MessageEvent("message", { data })); },
          });
        })();
        """;

    private static Host? s_current;

    private readonly Options _options;
    private readonly long _monotonicStart = Stopwatch.GetTimestamp();
    private readonly List<string> _marks = [];
    private readonly string _outDirectory;
    private readonly string _csp;
    private readonly TaskCompletionSource _uiReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource? _specialLoaded;
    private GLibContext? _ui;
    private nint _window;
    private nint _webView;
    private nint _webContext;
    private nint _loop;
    private nint _settings;
    private nint _cssProvider;
    private nint _bus;
    private string _choice;
    private string? _simulatedSystem;
    private string? _portalSystem;
    private string _systemSource = "";
    private string _theme;
    private string? _readyJson;
    private double _windowCreatedAt;
    private double _readyAt;
    private bool _shown;

    public Host(Options options)
    {
        _options = options;
        _outDirectory = Path.Combine(options.RepoRoot, "artifacts", "linux-wsl-desktop", "runs", options.Label);
        Directory.CreateDirectory(_outDirectory);
        _simulatedSystem = options.SystemTheme;
        _choice = options.Theme ?? "system";
        _theme = "light";
        _csp = BuildCsp();
    }

    public int Run()
    {
        var thread = new Thread(RunOnUiThread) { Name = "Spar UI" };
        thread.Start();
        thread.Join();
        WriteTimeline();
        return _readyJson is null ? 1 : 0;
    }

    // ---- Theme ----

    private string SystemTheme()
    {
        if (_simulatedSystem is not null)
        {
            _systemSource = "simulated";
            return _simulatedSystem;
        }

        if (_portalSystem is not null)
        {
            _systemSource = "portal";
            return _portalSystem;
        }

        var themeName = _settings == 0 ? null : GObject.GetString(_settings, "gtk-theme-name");
        var preferDark = _settings != 0 && GObject.GetBoolean(_settings, "gtk-application-prefer-dark-theme");
        var gtkTheme = Marshal.PtrToStringUTF8(LibC.getenv("GTK_THEME"));
        _systemSource = $"gtk fallback (gtk-theme-name={themeName} prefer-dark={preferDark} GTK_THEME={gtkTheme})";
        return preferDark || (themeName?.EndsWith("-dark", StringComparison.OrdinalIgnoreCase) ?? false)
            || (gtkTheme?.EndsWith(":dark", StringComparison.OrdinalIgnoreCase) ?? false) ? "dark" : "light";
    }

    /// <summary>Reads the XDG settings portal once and subscribes to its SettingChanged signal.</summary>
    private unsafe void ConnectPortal()
    {
        var started = Stopwatch.GetTimestamp();
        _bus = Gio.g_bus_get_sync(Gio.BusTypeSession, 0, out var error);
        if (_bus == 0)
        {
            Mark("portal no session bus: " + GLib.ErrorMessage(error));
            GLib.g_error_free(error);
            return;
        }

        var children = new nint[] { GLib.g_variant_new_string("org.freedesktop.appearance"), GLib.g_variant_new_string("color-scheme") };
        nint parameters;
        fixed (nint* items = children)
        {
            parameters = GLib.g_variant_new_tuple(items, 2);
        }

        var reply = Gio.g_dbus_connection_call_sync(_bus, "org.freedesktop.portal.Desktop", "/org/freedesktop/portal/desktop",
            "org.freedesktop.portal.Settings", "ReadOne", parameters, 0, 0, 1000, 0, out error);
        var took = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (reply == 0)
        {
            Mark(string.Create(CultureInfo.InvariantCulture, $"portal ReadOne failed after {took:F1} ms: {GLib.ErrorMessage(error)}"));
            GLib.g_error_free(error);
        }
        else
        {
            var boxed = GLib.g_variant_get_child_value(reply, 0);
            var inner = GLib.g_variant_get_variant(boxed);
            var value = GLib.g_variant_get_uint32(inner);
            // 0 is "no preference": light, never a reason to fall back to GTK settings the host sets itself.
            _portalSystem = value == 1 ? "dark" : "light";
            Mark(string.Create(CultureInfo.InvariantCulture, $"portal color-scheme={value} ({_portalSystem}) in {took:F1} ms"));
            GLib.g_variant_unref(inner);
            GLib.g_variant_unref(boxed);
            GLib.g_variant_unref(reply);
        }

        var id = Gio.g_dbus_connection_signal_subscribe(_bus, "org.freedesktop.portal.Desktop", "org.freedesktop.portal.Settings",
            "SettingChanged", "/org/freedesktop/portal/desktop", null, 0, &OnPortalSettingChanged, 0, 0);
        Mark("portal SettingChanged subscribed id=" + id);
    }

    [UnmanagedCallersOnly]
    private static void OnPortalSettingChanged(nint connection, nint sender, nint path, nint iface, nint signal, nint parameters, nint data)
    {
        try
        {
            var host = s_current!;
            var printed = GLib.TakeString(GLib.g_variant_print(parameters, 0));
            host.Mark("portal SettingChanged " + printed);
            var ns = GLib.g_variant_get_child_value(parameters, 0);
            var key = GLib.g_variant_get_child_value(parameters, 1);
            var isColorScheme = Marshal.PtrToStringUTF8(GLib.g_variant_get_string(ns, 0)) == "org.freedesktop.appearance"
                && Marshal.PtrToStringUTF8(GLib.g_variant_get_string(key, 0)) == "color-scheme";
            GLib.g_variant_unref(ns);
            GLib.g_variant_unref(key);
            if (!isColorScheme)
            {
                return;
            }

            var boxed = GLib.g_variant_get_child_value(parameters, 2);
            var inner = GLib.g_variant_get_variant(boxed);
            var value = GLib.g_variant_get_uint32(inner);
            GLib.g_variant_unref(inner);
            GLib.g_variant_unref(boxed);
            host._portalSystem = value == 1 ? "dark" : "light";
            if (host._choice == "system")
            {
                host.ApplyTheme("portal change");
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("SPAR_PORTAL_EXCEPTION " + exception);
        }
    }

    private string Effective() => _choice == "system" ? SystemTheme() : _choice;

    private static uint Background(string theme) => theme == "dark" ? 0x1f1f1fu : 0xf3f3f3u;

    private bool Has(string layer) => _options.Layers.Contains(layer);

    private void ApplyGtkSettings(string theme)
    {
        if (Has("prefer-dark")) GObject.SetBoolean(_settings, "gtk-application-prefer-dark-theme", theme == "dark");
        if (Has("gtk-theme")) GObject.SetString(_settings, "gtk-theme-name", theme == "dark" ? "Adwaita-dark" : "Adwaita");
    }

    private void ApplyCss(string theme)
    {
        if (!Has("css")) return;
        var css = $"window, window.background {{ background-color: #{Background(theme):x6}; }}";
#if SPAR_GTK4
        Gtk.gtk_css_provider_load_from_string(_cssProvider, css);
#else
        if (Gtk.gtk_css_provider_load_from_data(_cssProvider, css, -1, out var error) == 0)
        {
            Mark("css failed: " + GLib.ErrorMessage(error));
            GLib.g_error_free(error);
        }
#endif
    }

    private void ApplyWebViewBackground(string theme)
    {
        if (_webView == 0) return;
        if (Has("transparent"))
        {
            WebKit.webkit_web_view_set_background_color(_webView, default);
        }
        else if (Has("wv-bg"))
        {
            WebKit.webkit_web_view_set_background_color(_webView, GdkRgba.FromRgb(Background(theme)));
        }
    }

    private void ApplyTheme(string reason)
    {
        var theme = Effective();
        if (theme == _theme)
        {
            Mark($"theme-unchanged {theme} ({reason})");
            return;
        }

        _theme = theme;
        Mark($"theme-apply {theme} ({reason})");
        ApplyGtkSettings(theme);
        ApplyCss(theme);
        ApplyWebViewBackground(theme);
        Mark($"theme-applied {theme}");
    }

    // ---- GTK and WebKit ----

    private unsafe void RunOnUiThread()
    {
        s_current = this;
#if SPAR_GTK4
        if (Gtk.gtk_init_check() == 0)
#else
        if (Gtk.gtk_init_check(0, 0) == 0)
#endif
        {
            Console.Error.WriteLine("SPAR_ERROR gtk_init_check failed: no display?");
            return;
        }

        _ui = new GLibContext();
        SynchronizationContext.SetSynchronizationContext(_ui);
        _settings = Gtk.gtk_settings_get_default();
        var display = Gtk.gdk_display_get_default();
        Mark($"gtk-init display={GObject.TypeName(display)} prefer-dark={GObject.GetBoolean(_settings, "gtk-application-prefer-dark-theme")} theme-name={GObject.GetString(_settings, "gtk-theme-name")} font={GObject.GetString(_settings, "gtk-font-name")} decoration-layout={GObject.GetString(_settings, "gtk-decoration-layout")}");
        if (_simulatedSystem is null && _choice == "system")
        {
            ConnectPortal();
        }

        _theme = Effective();
        Mark($"start choice={_choice} system-source={_systemSource} effective={_theme} layers={string.Join(',', _options.Layers)}");
        ApplyGtkSettings(_theme);
        if (Has("css"))
        {
            _cssProvider = Gtk.gtk_css_provider_new();
            ApplyCss(_theme);
#if SPAR_GTK4
            Gtk.gtk_style_context_add_provider_for_display(display, _cssProvider, Gtk.StyleProviderPriorityApplication);
#else
            Gtk.gtk_style_context_add_provider_for_screen(Gtk.gdk_screen_get_default(), _cssProvider, Gtk.StyleProviderPriorityApplication);
#endif
        }

        _window = Gtk.gtk_window_new(
#if !SPAR_GTK4
            0 // GTK_WINDOW_TOPLEVEL
#endif
            );
        _windowCreatedAt = Now();
        Gtk.gtk_window_set_title(_window, _options.Title);
        Gtk.gtk_window_set_default_size(_window, _options.Width, _options.Height);
        if (_options.HeaderBar)
        {
            var bar = Gtk.gtk_header_bar_new();
#if !SPAR_GTK4
            Gtk.gtk_header_bar_set_show_close_button(bar, 1);
            Gtk.gtk_widget_show(bar);
#endif
            Gtk.gtk_window_set_titlebar(_window, bar);
        }

        GObject.Connect(_window, "destroy", (delegate* unmanaged<nint, nint, void>)&OnWindowDestroyed);
        GObject.Connect(_window, "notify::is-active", (delegate* unmanaged<nint, nint, nint, void>)&OnActiveChanged);
#if !SPAR_GTK4
        GObject.Connect(_window, "map-event", (delegate* unmanaged<nint, nint, nint, int>)&OnMapEvent);
#endif
        Mark("window-created");
        if (_options.Order == "show-first")
        {
            Show();
        }

        CreateWebView();
        if (_options.Order != "show-first")
        {
            Load();
        }

        if (_options.Order == "load-first")
        {
            Show();
        }

        _loop = GLib.g_main_loop_new(0, 0);
        PostStart();
        GLib.g_main_loop_run(_loop);
        Mark("main-loop-ended");
        GObject.g_object_unref(_webContext);
        _webContext = 0;
        WaitForChildren();
        GLib.g_main_loop_unref(_loop);
        if (_bus != 0) GObject.g_object_unref(_bus);
        _ui.Dispose();
    }

    /// <summary>Shows the window (once). The order relative to creating the view and loading is --order.</summary>
    private unsafe void Show()
    {
        if (_shown) return;
        _shown = true;
#if SPAR_GTK4
        Gtk.gtk_window_present(_window);
        GObject.Connect(Gtk.gtk_native_get_surface(_window), "notify::mapped", (delegate* unmanaged<nint, nint, nint, void>)&OnSurfaceMapped);
#else
        Gtk.gtk_widget_show_all(_window);
#endif
        Mark("visible: shown");
    }

    private void Load()
    {
        var url = $"{Origin}/{_options.Page}{(_options.Query is { Length: > 0 } q ? "?" + q : "")}";
        WebKit.webkit_web_view_load_uri(_webView, url);
        Mark("navigate " + url);
    }

    // Kept out of the unsafe member: C# 15 rejects await in a lambda inside an unsafe method (CS8652).
    private void PostStart() => _ui!.Post(static async state => await ((Host)state!).StartAsync(), this);

    private unsafe void CreateWebView()
    {
        var dataPath = Path.Combine(_options.DataDirectory, "data");
        var cachePath = Path.Combine(_options.DataDirectory, "cache");
#if SPAR_GTK4
        var session = WebKit.webkit_network_session_new(dataPath, cachePath);
        _webContext = WebKit.webkit_web_context_new();
        ConfigureContext();
        _webView = GObject.New(
            WebKit.webkit_web_view_get_type(),
            new("web-context", _webContext, WebKit.webkit_web_context_get_type()),
            new("network-session", session, WebKit.webkit_network_session_get_type()));
        GObject.g_object_unref(session);
#else
        var manager = GObject.New(
            WebKit.webkit_website_data_manager_get_type(),
            new("base-data-directory", dataPath),
            new("base-cache-directory", cachePath));
        _webContext = WebKit.webkit_web_context_new_with_website_data_manager(manager);
        GObject.g_object_unref(manager);
        if (_options.Sandbox)
        {
            WebKit.webkit_web_context_set_sandbox_enabled(_webContext, 1);
        }

        ConfigureContext();
        _webView = GObject.New(WebKit.webkit_web_view_get_type(), new GProperty("web-context", _webContext, WebKit.webkit_web_context_get_type()));
#endif
        var settings = WebKit.webkit_web_view_get_settings(_webView);
        if (_options.AccelerationPolicy is { } policy)
        {
            WebKit.webkit_settings_set_hardware_acceleration_policy(settings, WebKit.AccelerationPolicy(policy));
        }

        Mark($"view-created sandbox={(_options.Sandbox ? "on" : "default")} hw-policy={WebKit.AccelerationPolicyName(WebKit.webkit_settings_get_hardware_acceleration_policy(settings))}");
        ApplyWebViewBackground(_theme);
        var contentManager = WebKit.webkit_web_view_get_user_content_manager(_webView);
#if SPAR_GTK4
        WebKit.webkit_user_content_manager_register_script_message_handler(contentManager, "spar", null);
#else
        WebKit.webkit_user_content_manager_register_script_message_handler(contentManager, "spar");
#endif
        GObject.Connect(contentManager, "script-message-received::spar", (delegate* unmanaged<nint, nint, nint, void>)&OnScriptMessage);
        var script = WebKit.webkit_user_script_new(Shim, WebKit.InjectTopFrame, WebKit.InjectAtDocumentStart, 0, 0);
        WebKit.webkit_user_content_manager_add_script(contentManager, script);
        WebKit.webkit_user_script_unref(script);
        GObject.Connect(_webView, "load-changed", (delegate* unmanaged<nint, int, nint, void>)&OnLoadChanged);
        GObject.Connect(_webView, "web-process-terminated", (delegate* unmanaged<nint, int, nint, void>)&OnWebProcessTerminated);
#if SPAR_GTK4
        Gtk.gtk_window_set_child(_window, _webView);
#else
        Gtk.gtk_container_add(_window, _webView);
        Gtk.gtk_widget_show(_webView);
#endif
        if (_options.GrabFocus)
        {
            Gtk.gtk_widget_grab_focus(_webView);
        }

        Mark($"view-attached grab-focus={_options.GrabFocus}");
    }

    private unsafe void ConfigureContext()
    {
        WebKit.webkit_web_context_register_uri_scheme(_webContext, "spar", &OnSchemeRequest, 0, 0);
        var security = WebKit.webkit_web_context_get_security_manager(_webContext);
        WebKit.webkit_security_manager_register_uri_scheme_as_secure(security, "spar");
        WebKit.webkit_security_manager_register_uri_scheme_as_cors_enabled(security, "spar");
    }

    private async Task StartAsync()
    {
        try
        {
            if (_options.Order == "show-first")
            {
                Load();
            }

            if (await Task.WhenAny(_uiReady.Task, Task.Delay(20000)) != _uiReady.Task)
            {
                Mark("ui-ready-timeout (20 s)");
            }

            WindowFacts("at-ready");
            if (_options.ResizeTest)
            {
                await Task.Delay(600);
                Mark("resize maximize");
                Gtk.gtk_window_maximize(_window);
                await Task.Delay(1500);
                WindowFacts("maximized");
                Mark("resize unmaximize");
                Gtk.gtk_window_unmaximize(_window);
                await Task.Delay(1500);
                WindowFacts("unmaximized");
            }

            await Task.Delay(_options.AfterReadyMs);
            WindowFacts("before-close");
            if (_options.GpuPage)
            {
                await SaveGpuPageAsync();
            }
        }
        catch (Exception exception)
        {
            Mark("host-failure " + exception);
        }

        Mark("closing");
        Gtk.gtk_window_close(_window);
    }

    /// <summary>Opens webkit://gpu (WebKitGTK's GPU information page) and saves its text.</summary>
    private async Task SaveGpuPageAsync()
    {
        _specialLoaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        WebKit.webkit_web_view_load_uri(_webView, "webkit://gpu");
        Mark("navigate webkit://gpu");
        if (await Task.WhenAny(_specialLoaded.Task, Task.Delay(5000)) != _specialLoaded.Task)
        {
            Mark("webkit://gpu did not finish loading in 5 s");
            return;
        }

        await Task.Delay(300);
        var text = await EvaluateAsync("return document.body ? document.body.innerText : '(no body)';");
        File.WriteAllText(Path.Combine(_outDirectory, "gpu.txt"), text ?? "(no result)");
        Mark($"gpu.txt saved ({text?.Length ?? 0} chars)");
    }

    private unsafe Task<string?> EvaluateAsync(string body)
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = GCHandle.Alloc(completion);
        var bytes = Encoding.UTF8.GetBytes(body);
        fixed (byte* text = bytes)
        {
            WebKit.webkit_web_view_call_async_javascript_function(_webView, text, bytes.Length, 0, 0, 0, 0, &OnEvaluated, GCHandle.ToIntPtr(handle));
        }

        return completion.Task;
    }

    [UnmanagedCallersOnly]
    private static void OnEvaluated(nint source, nint result, nint data)
    {
        var handle = GCHandle.FromIntPtr(data);
        var completion = (TaskCompletionSource<string?>)handle.Target!;
        handle.Free();
        try
        {
            var value = WebKit.webkit_web_view_call_async_javascript_function_finish(source, result, out var error);
            if (value == 0)
            {
                completion.TrySetResult("(error) " + GLib.ErrorMessage(error));
                GLib.g_error_free(error);
                return;
            }

            completion.TrySetResult(GLib.TakeString(Jsc.jsc_value_to_string(value)));
            GObject.g_object_unref(value);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    /// <summary>Records the window's backend, decorations, size, scale, monitor and focus.</summary>
    private void WindowFacts(string when)
    {
        if (_window == 0) return;
        var display = Gtk.gtk_widget_get_display(_window);
        var displayType = GObject.TypeName(display);
        var isX11 = displayType.Contains("X11", StringComparison.Ordinal);
        var builder = new StringBuilder($"window-facts {when}: display={displayType}");
        builder.Append(CultureInfo.InvariantCulture, $" decorated={Gtk.gtk_window_get_decorated(_window)} active={Gtk.gtk_window_is_active(_window)} scale-factor={Gtk.gtk_widget_get_scale_factor(_window)}");
#if SPAR_GTK4
        var surface = Gtk.gtk_native_get_surface(_window);
        builder.Append(CultureInfo.InvariantCulture, $" csd={Gtk.gtk_widget_has_css_class(_window, "csd")} size={Gtk.gtk_widget_get_width(_window)}x{Gtk.gtk_widget_get_height(_window)} surface-scale={Gtk.gdk_surface_get_scale(surface):F2}");
        var monitor = surface == 0 ? 0 : Gtk.gdk_display_get_monitor_at_surface(display, surface);
        if (isX11 && surface != 0) builder.Append(CultureInfo.InvariantCulture, $" xid=0x{Gtk.gdk_x11_surface_get_xid(surface):x}");
#else
        var gdkWindow = Gtk.gtk_widget_get_window(_window);
        var style = Gtk.gtk_widget_get_style_context(_window);
        builder.Append(CultureInfo.InvariantCulture, $" csd={Gtk.gtk_style_context_has_class(style, "csd")} toplevel-focus={Gtk.gtk_window_has_toplevel_focus(_window)} size={Gtk.gtk_widget_get_allocated_width(_window)}x{Gtk.gtk_widget_get_allocated_height(_window)}");
        var monitor = gdkWindow == 0 ? 0 : Gtk.gdk_display_get_monitor_at_window(display, gdkWindow);
        if (isX11 && gdkWindow != 0) builder.Append(CultureInfo.InvariantCulture, $" xid=0x{Gtk.gdk_x11_window_get_xid(gdkWindow):x}");
#endif
        if (monitor != 0)
        {
            Gtk.gdk_monitor_get_geometry(monitor, out var geometry);
            builder.Append(CultureInfo.InvariantCulture, $" monitor={geometry.Width}x{geometry.Height}@{geometry.X},{geometry.Y} monitor-scale-factor={Gtk.gdk_monitor_get_scale_factor(monitor)} refresh={Gtk.gdk_monitor_get_refresh_rate(monitor)}mHz");
#if SPAR_GTK4
            builder.Append(CultureInfo.InvariantCulture, $" monitor-scale={Gtk.gdk_monitor_get_scale(monitor):F2}");
#endif
        }

        Mark(builder.ToString());
    }

    private void OnMessage(string text)
    {
        using var json = JsonDocument.Parse(text);
        var root = json.RootElement;
        switch (root.GetProperty("t").GetString())
        {
            case "mark":
            case "report":
                Mark($"page:{root.GetProperty("n").GetString()} {text}");
                break;
            case "scheme":
                _choice = root.GetProperty("v").GetString() ?? "system";
                ApplyTheme("app choice");
                break;
            case "ready":
                _readyJson = text;
                _readyAt = Now();
                Mark("ui-ready");
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"SPAR_READY window->ready {_readyAt - _windowCreatedAt:F1} ms {text}"));
                _uiReady.TrySetResult();
                break;
        }
    }

    [UnmanagedCallersOnly]
    private static void OnScriptMessage(nint contentManager, nint value, nint data)
    {
        try
        {
#if !SPAR_GTK4
            value = WebKit.webkit_javascript_result_get_js_value(value);
#endif
            var message = GLib.TakeString(Jsc.jsc_value_to_string(value));
            if (message is not null) s_current!.OnMessage(message);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("SPAR_MESSAGE_EXCEPTION " + exception);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnLoadChanged(nint webView, int loadEvent, nint data)
    {
        var host = s_current!;
        host.Mark(loadEvent switch { 0 => "load-started", 1 => "load-redirected", 2 => "load-committed", 3 => "load-finished", _ => "load-" + loadEvent });
        if (loadEvent == 2 && host._options.Order == "show-on-commit")
        {
            host.Show();
        }

        if (loadEvent == 3 && host._specialLoaded is { } loaded)
        {
            loaded.TrySetResult();
        }
    }

    [UnmanagedCallersOnly]
    private static void OnWebProcessTerminated(nint webView, int reason, nint data) =>
        s_current!.Mark("web-process-terminated reason=" + reason switch { 0 => "crashed", 1 => "exceeded-memory-limit", 2 => "terminated-by-api", _ => reason.ToString(CultureInfo.InvariantCulture) });

    [UnmanagedCallersOnly]
    private static void OnActiveChanged(nint window, nint spec, nint data) =>
        s_current!.Mark("window is-active=" + Gtk.gtk_window_is_active(window));

#if SPAR_GTK4
    [UnmanagedCallersOnly]
    private static void OnSurfaceMapped(nint surface, nint spec, nint data) => s_current!.Mark("window-mapped (surface)");
#else
    [UnmanagedCallersOnly]
    private static int OnMapEvent(nint widget, nint gdkEvent, nint data)
    {
        s_current!.Mark("window-mapped (map-event)");
        return 0;
    }
#endif

    [UnmanagedCallersOnly]
    private static void OnWindowDestroyed(nint widget, nint data)
    {
        var host = s_current!;
        host.Mark("window-destroyed");
        host._webView = 0;
        host._window = 0;
        GLib.g_main_loop_quit(host._loop);
    }

    // ---- The custom scheme: files from the web folder, with the CSP header on HTML ----

    [UnmanagedCallersOnly]
    private static void OnSchemeRequest(nint request, nint data)
    {
        try
        {
            s_current!.HandleSchemeRequest(request);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("SPAR_SCHEME_EXCEPTION " + exception);
        }
    }

    private void HandleSchemeRequest(nint request)
    {
        var path = Marshal.PtrToStringUTF8(WebKit.webkit_uri_scheme_request_get_path(request)) ?? "/";
        var file = Path.GetFullPath(Path.Combine(_options.WebDirectory, path.TrimStart('/')));
        if (!file.StartsWith(_options.WebDirectory, StringComparison.Ordinal) || !File.Exists(file))
        {
            Respond(request, 404, "Not Found", "text/plain", "not found"u8.ToArray(), null);
            return;
        }

        var isHtml = file.EndsWith(".html", StringComparison.Ordinal);
        Mark("serve " + path);
        Respond(request, 200, "OK", ContentType(file), File.ReadAllBytes(file), isHtml ? _csp : null);
    }

    private static void Respond(nint request, uint status, string reason, string contentType, byte[] content, string? csp)
    {
        var stream = Gio.MemoryStream(content);
        var response = WebKit.webkit_uri_scheme_response_new(stream, content.Length);
        WebKit.webkit_uri_scheme_response_set_status(response, status, reason);
        WebKit.webkit_uri_scheme_response_set_content_type(response, contentType);
        var headers = Soup.soup_message_headers_new(Soup.MessageHeadersResponse);
        Soup.soup_message_headers_append(headers, "Cache-Control", "no-store");
        // The headers object replaces the response headers: without Content-Type here, nosniff makes WebKit drop the CSS.
        Soup.soup_message_headers_append(headers, "Content-Type", contentType);
        Soup.soup_message_headers_append(headers, "X-Content-Type-Options", "nosniff");
        if (csp is not null) Soup.soup_message_headers_append(headers, "Content-Security-Policy", csp);
        WebKit.webkit_uri_scheme_response_set_http_headers(response, headers);
        WebKit.webkit_uri_scheme_request_finish_with_response(request, response);
        GObject.g_object_unref(response);
        GObject.g_object_unref(stream);
    }

    private string BuildCsp()
    {
        var styleSources = "'self'";
        var hashes = Path.Combine(_options.WebDirectory, "spar-csp.json");
        if (File.Exists(hashes))
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(hashes));
            foreach (var source in json.RootElement.GetProperty("styleSrc").EnumerateArray())
            {
                styleSources += " " + source.GetString();
            }
        }

        return $"default-src 'none'; script-src 'self'; style-src {styleSources}; img-src 'self'; font-src 'self'; connect-src 'self'; " +
            "base-uri 'none'; form-action 'none'; frame-ancestors 'none'; object-src 'none'";
    }

    private static string ContentType(string path) => Path.GetExtension(path) switch
    {
        ".html" => "text/html",
        ".js" => "text/javascript",
        ".css" => "text/css",
        ".json" => "application/json",
        ".svg" => "image/svg+xml",
        _ => "application/octet-stream",
    };

    // ---- Shutdown and output ----

    private void WaitForChildren()
    {
        var started = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(10))
        {
            if (Descendants() == 0)
            {
                Mark("children-exited");
                return;
            }

            while (GLib.g_main_context_iteration(0, 0) != 0)
            {
            }

            Thread.Sleep(1);
        }

        Mark($"children still alive after 10 s: {Descendants()}");
    }

    private static int Descendants()
    {
        var parents = new Dictionary<int, int>();
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory), out var pid)) continue;
            try
            {
                var stat = File.ReadAllText(Path.Combine(directory, "stat"));
                var rest = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
                if (rest[0] != "Z") parents[pid] = int.Parse(rest[1], CultureInfo.InvariantCulture);
            }
            catch (IOException)
            {
                // The process ended while we looked.
            }
        }

        var count = 0;
        var frontier = new Queue<int>([Environment.ProcessId]);
        while (frontier.TryDequeue(out var parent))
        {
            foreach (var (pid, ppid) in parents)
            {
                if (ppid == parent)
                {
                    count++;
                    frontier.Enqueue(pid);
                }
            }
        }

        return count;
    }

    private void WriteTimeline()
    {
        var builder = new StringBuilder();
        builder.Append("{\n\"label\":").Append(JsonString(_options.Label))
            .Append(",\n\"args\":").Append(JsonString(string.Join(' ', _options.Raw)))
            .Append(",\n\"monotonicStartMs\":").Append((_monotonicStart * 1000.0 / Stopwatch.Frequency).ToString("F3", CultureInfo.InvariantCulture))
            .Append(",\n\"windowCreatedMs\":").Append(_windowCreatedAt.ToString("F1", CultureInfo.InvariantCulture))
            .Append(",\n\"readyMs\":").Append(_readyJson is null ? "null" : _readyAt.ToString("F1", CultureInfo.InvariantCulture))
            .Append(",\n\"ready\":").Append(_readyJson ?? "null")
            .Append(",\n\"marks\":[\n");
        lock (_marks)
        {
            builder.Append(string.Join(",\n", _marks));
        }

        builder.Append("\n]\n}\n");
        File.WriteAllText(Path.Combine(_outDirectory, "timeline.json"), builder.ToString());
        Console.WriteLine("SPAR_TIMELINE " + _options.Label);
    }

    private static string JsonString(string value) => JsonSerializer.Serialize(value, HostJson.Default.String);

    private double Now() => Stopwatch.GetElapsedTime(_monotonicStart).TotalMilliseconds;

    private void Mark(string name)
    {
        var at = Now();
        lock (_marks)
        {
            _marks.Add(string.Create(CultureInfo.InvariantCulture, $"{{\"at\":{at:F1},\"name\":{JsonString(name)}}}"));
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[{at,8:F1} ms] {name}"));
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(string))]
internal sealed partial class HostJson : System.Text.Json.Serialization.JsonSerializerContext;
