using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Theme.Linux;

/// <summary>
/// Spike host for WebKitGTK. One thread runs GTK, the view and the GLib main loop. Each theme layer is behind a switch:
///   css          a GTK CSS provider paints the window background;
///   prefer-dark  GtkSettings gtk-application-prefer-dark-theme (title bar / header bar, and maybe prefers-color-scheme);
///   gtk-theme    GtkSettings gtk-theme-name Adwaita-dark / Adwaita;
///   wv-bg        webkit_web_view_set_background_color;
///   transparent  webkit_web_view_set_background_color with alpha 0 (the window shows through).
/// Frames are captured outside the process (linux/xcap.c); marks use CLOCK_MONOTONIC so both can be merged.
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
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly long _monotonicStart = Stopwatch.GetTimestamp();
    private readonly List<string> _marks = [];
    private readonly string _outDirectory;
    private readonly string _csp;
    private readonly TaskCompletionSource _uiReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private GLibContext? _ui;
    private nint _window;
    private nint _webView;
    private nint _webContext;
    private nint _loop;
    private nint _settings;
    private nint _cssProvider;
    private string _choice;
    private string? _simulatedSystem;
    private string _systemSource = "";
    private string _theme;
    private string? _readyJson;
    private bool _shown;

    public Host(Options options)
    {
        _options = options;
        _outDirectory = Path.Combine(options.RepoRoot, "artifacts", "theme-startup", options.Label);
        Directory.CreateDirectory(_outDirectory);
        _simulatedSystem = options.SystemTheme;
        _choice = options.Theme ?? ThemeStore.Load(options.AppDataDirectory) ?? "system";
        _theme = "light";
        _csp = BuildCsp();
    }

    public int Run()
    {
        var thread = new Thread(RunOnUiThread) { Name = "Spar UI" };
        thread.Start();
        thread.Join();
        WriteTimeline();
        return 0;
    }

    // ---- Theme ----

    private string SystemTheme()
    {
        if (_simulatedSystem is not null)
        {
            _systemSource = "simulated";
            return _simulatedSystem;
        }

        // The XDG settings portal: org.freedesktop.appearance color-scheme (0 no preference, 1 dark, 2 light).
        var portal = ReadPortalColorScheme(out var detail);
        Mark("portal " + detail);
        if (portal is 1 or 2)
        {
            _systemSource = "portal";
            return portal == 1 ? "dark" : "light";
        }

        // Fallback: GTK's own settings (a theme name ending in -dark, or the dark preference set by the desktop).
        var themeName = _settings == 0 ? null : GObject.GetString(_settings, "gtk-theme-name");
        var preferDark = _settings != 0 && GObject.GetBoolean(_settings, "gtk-application-prefer-dark-theme");
        var gtkTheme = Marshal.PtrToStringUTF8(LibC.getenv("GTK_THEME"));
        Mark($"fallback gtk-theme-name={themeName} prefer-dark={preferDark} GTK_THEME={gtkTheme}");
        _systemSource = "gtk fallback";
        return preferDark || (themeName?.EndsWith("-dark", StringComparison.OrdinalIgnoreCase) ?? false)
            || (gtkTheme?.EndsWith(":dark", StringComparison.OrdinalIgnoreCase) ?? false) ? "dark" : "light";
    }

    private static uint? ReadPortalColorScheme(out string detail)
    {
        var bus = Gio.g_bus_get_sync(Gio.BusTypeSession, 0, out var error);
        if (bus == 0)
        {
            detail = "no session bus: " + GLib.ErrorMessage(error);
            GLib.g_error_free(error);
            return null;
        }

        try
        {
            var children = new nint[] { GLib.g_variant_new_string("org.freedesktop.appearance"), GLib.g_variant_new_string("color-scheme") };
            nint parameters;
            unsafe
            {
                fixed (nint* items = children)
                {
                    parameters = GLib.g_variant_new_tuple(items, 2);
                }
            }

            var reply = Gio.g_dbus_connection_call_sync(bus, "org.freedesktop.portal.Desktop", "/org/freedesktop/portal/desktop",
                "org.freedesktop.portal.Settings", "ReadOne", parameters, 0, 0, 1000, 0, out error);
            if (reply == 0)
            {
                detail = "ReadOne failed: " + GLib.ErrorMessage(error);
                GLib.g_error_free(error);
                return null;
            }

            var boxed = GLib.g_variant_get_child_value(reply, 0);
            var inner = GLib.g_variant_get_variant(boxed);
            var value = GLib.g_variant_get_uint32(inner);
            detail = "color-scheme=" + value;
            GLib.g_variant_unref(inner);
            GLib.g_variant_unref(boxed);
            GLib.g_variant_unref(reply);
            return value;
        }
        finally
        {
            GObject.g_object_unref(bus);
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

    /// <summary>What the portal's SettingChanged signal would trigger; simulated here (no portal in the container).</summary>
    private void SimulateSystemChange(string theme)
    {
        _simulatedSystem = theme;
        Mark($"simulate-system {theme}");
        if (_choice == "system") ApplyTheme("system change");
    }

    private void OnSchemeFromPage(string scheme)
    {
        Mark($"page-set-scheme {scheme}");
        _choice = scheme;
        ThemeStore.Save(_options.AppDataDirectory, scheme);
        ApplyTheme("app choice");
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
        Mark($"gtk-init defaults: prefer-dark={GObject.GetBoolean(_settings, "gtk-application-prefer-dark-theme")} theme-name={GObject.GetString(_settings, "gtk-theme-name")} animations={GObject.GetBoolean(_settings, "gtk-enable-animations")}");
        _theme = Effective();
        Mark($"start choice={_choice} system-source={_systemSource} effective={_theme} layers={string.Join(',', _options.Layers)} show={_options.Show}");
        ApplyGtkSettings(_theme);
        if (_options.ReducedMotion)
        {
            GObject.SetBoolean(_settings, "gtk-enable-animations", false);
            Mark("gtk-enable-animations=false");
        }

        if (Has("css"))
        {
            _cssProvider = Gtk.gtk_css_provider_new();
            ApplyCss(_theme);
#if SPAR_GTK4
            Gtk.gtk_style_context_add_provider_for_display(Gtk.gdk_display_get_default(), _cssProvider, Gtk.StyleProviderPriorityApplication);
#else
            Gtk.gtk_style_context_add_provider_for_screen(Gtk.gdk_screen_get_default(), _cssProvider, Gtk.StyleProviderPriorityApplication);
#endif
        }

        _window = Gtk.gtk_window_new(
#if !SPAR_GTK4
            0 // GTK_WINDOW_TOPLEVEL
#endif
            );
        Gtk.gtk_window_set_title(_window, "Theme startup spike");
        Gtk.gtk_window_set_default_size(_window, 960, 720);
        if (_options.HeaderBar)
        {
            var bar = Gtk.gtk_header_bar_new();
#if !SPAR_GTK4
            Gtk.gtk_header_bar_set_show_close_button(bar, 1);
            Gtk.gtk_widget_show(bar);
#endif
            Gtk.gtk_window_set_titlebar(_window, bar);
        }

#if !SPAR_GTK4
        Gtk.gtk_window_move(_window, 0, 0);
#endif
        GObject.Connect(_window, "destroy", (delegate* unmanaged<nint, nint, void>)&OnWindowDestroyed);
        Mark("window-created");
        if (_options.Show == "now") Show("immediate");

        CreateWebView();
        _loop = GLib.g_main_loop_new(0, 0);
        PostStart();
        GLib.g_main_loop_run(_loop);
        Mark("main-loop-ended");
        GObject.g_object_unref(_webContext);
        _webContext = 0;
        WaitForChildren();
        GLib.g_main_loop_unref(_loop);
        _ui.Dispose();
    }

    private void PostStart() => _ui!.Post(static async state => await ((Host)state!).StartAsync(), this);

    private void Show(string why)
    {
        if (_shown) return;
        _shown = true;
#if SPAR_GTK4
        Gtk.gtk_window_present(_window);
#else
        Gtk.gtk_widget_show_all(_window);
#endif
        Mark($"visible: shown ({why})");
    }

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
        ConfigureContext();
        _webView = GObject.New(WebKit.webkit_web_view_get_type(), new GProperty("web-context", _webContext, WebKit.webkit_web_context_get_type()));
#endif
        Mark("view-created");
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
#if SPAR_GTK4
        Gtk.gtk_window_set_child(_window, _webView);
#else
        Gtk.gtk_container_add(_window, _webView);
        Gtk.gtk_widget_show(_webView);
#endif
        Mark("view-attached");
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
            var url = $"{Origin}/{_options.Page}{(_options.Query is { Length: > 0 } q ? "?" + q : "")}";
            WebKit.webkit_web_view_load_uri(_webView, url);
            Mark("navigate " + url);
            if (_options.Raw.Contains("--redraw")) _ = RedrawLoopAsync();

            var timeout = Task.Delay(_options.ShowTimeoutMs);
            if (_options.Show == "ready" && await Task.WhenAny(_uiReady.Task, timeout) == timeout)
            {
                Show("timeout");
            }

            if (await Task.WhenAny(_uiReady.Task, Task.Delay(15000)) != _uiReady.Task)
            {
                Mark("ui-ready-timeout (15 s)");
                Show("no ui-ready");
            }

            await Task.Delay(_options.AfterReadyMs);
            if (_options.Live)
            {
                var step = TimeSpan.FromMilliseconds(900);
                string Opposite() => SystemTheme() == "dark" ? "light" : "dark";
                SimulateSystemChange(Opposite());
                await Task.Delay(step);
                Deliver("set-scheme:" + Opposite());
                await Task.Delay(step);
                SimulateSystemChange(Opposite());
                await Task.Delay(step);
                Deliver("set-scheme:system");
                await Task.Delay(step);
                SimulateSystemChange(Opposite());
                await Task.Delay(step);
            }
        }
        catch (Exception exception)
        {
            Mark("host-failure " + exception);
        }

        Mark("closing");
        Gtk.gtk_window_close(_window);
    }

    /// <summary>Diagnostic: asks GTK to redraw the view every 50 ms (does the X window then show new web frames?).</summary>
    private async Task RedrawLoopAsync()
    {
        while (_webView != 0)
        {
            await Task.Delay(50);
            if (_webView != 0) Gtk.gtk_widget_queue_draw(_webView);
        }
    }

    private unsafe void Deliver(string message)
    {
        var script = Encoding.UTF8.GetBytes($"window.__sparDeliver([{JsonSerializer.Serialize(message, HostJson.Default.String)}])");
        fixed (byte* text = script)
        {
            WebKit.webkit_web_view_evaluate_javascript(_webView, text, script.Length, 0, 0, 0, null, 0);
        }
    }

    private void OnMessage(string text)
    {
        using var json = JsonDocument.Parse(text);
        var root = json.RootElement;
        switch (root.GetProperty("t").GetString())
        {
            case "mark":
                Mark($"page:{root.GetProperty("n").GetString()} p={root.GetProperty("p").GetDouble():F1} {text}");
                break;
            case "scheme":
                OnSchemeFromPage(root.GetProperty("v").GetString() ?? "system");
                break;
            case "ready":
                _readyJson = text;
                Mark("ui-ready");
                Console.WriteLine("SPAR_READY " + text);
                if (_options.Show == "ready") Show("ui-ready");
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
    private static void OnLoadChanged(nint webView, int loadEvent, nint data) =>
        s_current!.Mark(loadEvent switch { 0 => "load-started", 1 => "load-redirected", 2 => "load-committed", 3 => "load-finished", _ => "load-" + loadEvent });

    [UnmanagedCallersOnly]
    private static void OnWindowDestroyed(nint widget, nint data)
    {
        var host = s_current!;
        host.Mark("window-destroyed");
        host._webView = 0;
        GLib.g_main_loop_quit(host._loop);
    }

    // ---- The custom scheme: files from the built frontend, with the CSP header on HTML ----

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
        var asset = path.StartsWith("/assets/", StringComparison.Ordinal);
        var delay = isHtml ? _options.HtmlDelayMs
            : asset && file.EndsWith(".js", StringComparison.Ordinal) ? _options.BundleDelayMs
            : asset && file.EndsWith(".css", StringComparison.Ordinal) ? _options.CssDelayMs : 0;
        var bytes = File.ReadAllBytes(file);
        var csp = isHtml && !_options.NoCsp ? _csp : null;
        if (isHtml && _options.CspMeta)
        {
            // Fallback check: the same policy as a <meta> element, in case the engine ignores the header here.
            var html = Encoding.UTF8.GetString(bytes);
            var at = html.IndexOf("<head>", StringComparison.Ordinal) + "<head>".Length;
            bytes = Encoding.UTF8.GetBytes(html[..at] + $"<meta http-equiv=\"Content-Security-Policy\" content=\"{_csp}\">" + html[at..]);
            csp = null;
        }

        Mark("serve " + path);
        if (delay > 0)
        {
            GObject.g_object_ref(request);
            _ = RespondLaterAsync(request, path, delay, ContentType(file), bytes, csp);
            return;
        }

        Respond(request, 200, "OK", ContentType(file), bytes, csp);
    }

    private async Task RespondLaterAsync(nint request, string path, int delay, string contentType, byte[] bytes, string? csp)
    {
        await Task.Delay(delay);
        Respond(request, 200, "OK", contentType, bytes, csp);
        GObject.g_object_unref(request);
        Mark($"delivered {path} (delayed {delay} ms)");
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
        if (!_options.NoHash && File.Exists(hashes))
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
            .Append(",\n\"csp\":").Append(JsonString(_csp))
            .Append(",\n\"monotonicStartMs\":").Append((_monotonicStart * 1000.0 / Stopwatch.Frequency).ToString("F3", CultureInfo.InvariantCulture))
            .Append(",\n\"framesFile\":\"frames.jsonl\"")
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

    private void Mark(string name)
    {
        var at = Stopwatch.GetElapsedTime(_monotonicStart).TotalMilliseconds;
        lock (_marks)
        {
            _marks.Add(string.Create(CultureInfo.InvariantCulture, $"{{\"at\":{at:F1},\"name\":{JsonString(name)}}}"));
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[{at,8:F1} ms] {name}"));
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(string))]
internal sealed partial class HostJson : System.Text.Json.Serialization.JsonSerializerContext;

/// <summary>The app's own colour scheme choice, saved in the app's data folder and read before the window exists.</summary>
internal static class ThemeStore
{
    public static string? Load(string directory)
    {
        var file = Path.Combine(directory, "color-scheme.txt");
        var value = File.Exists(file) ? File.ReadAllText(file).Trim() : null;
        return value is "light" or "dark" or "system" ? value : null;
    }

    public static void Save(string directory, string scheme)
    {
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "color-scheme.txt");
        File.WriteAllText(file + ".tmp", scheme);
        File.Move(file + ".tmp", file, overwrite: true);
    }
}
