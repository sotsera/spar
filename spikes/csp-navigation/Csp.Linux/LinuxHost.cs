using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Csp.Shared;

namespace Csp.Linux;

/// <summary>
/// Spike WebKitGTK host: serves <c>spar://app/</c> with a header profile, injects the boot script at document start,
/// locks navigation with decide-policy, blocks new windows, denies permission requests, and performs trusted clicks
/// with XTEST (xdotool) when the page asks for them.
/// </summary>
internal sealed class LinuxHost
{
    public const string AppOrigin = "spar://app";
    private const string Title = "Csp Spike";

    private static LinuxHost? s_current;

    private readonly HostOptions _options;
    private readonly string _origin;
    private readonly AppRoutes _routes;
    private nint _window;
    private nint _webView;
    private nint _loop;
    private GLibContext? _ui;
    private bool _closing;

    private LinuxHost(HostOptions options)
    {
        _options = options;
        _origin = options.DevUrl is { } dev ? new Uri(dev).GetLeftPart(UriPartial.Authority) : AppOrigin;
        var external = $"http://127.0.0.1:{options.ExternalPort}";
        var profile = HeaderProfile.Load(options, _origin, external);
        Log.Write($"HOST linux-webkitgtk-{Variant} origin={_origin} profile={profile.Name} csp={profile.Csp ?? "-"} meta={profile.Meta ?? "-"} navlock={options.NavigationLock} inject={options.Inject} secure={!options.Has("--no-secure")} cors={!options.Has("--no-cors")}");
        _routes = new AppRoutes(options.Roots, profile, options.Tamper, OnPageMessage);
    }

#if SPAR_GTK4
    private const string Variant = "6.0";
#else
    private const string Variant = "4.1";
#endif

    public static int Run(HostOptions options)
    {
        using var external = new MiniHttp(options.ExternalPort, ExternalServer.Create(options.Label));
        var exit = 0;
        var thread = new Thread(() => exit = new LinuxHost(options).RunOnUiThread()) { Name = "Spar UI" };
        thread.Start();
        thread.Join();
        return exit;
    }

    private unsafe int RunOnUiThread()
    {
        s_current = this;
#if SPAR_GTK4
        if (Gtk.gtk_init_check() == 0)
#else
        if (Gtk.gtk_init_check(0, 0) == 0)
#endif
        {
            Log.Write("HOST_FAILURE gtk_init_check failed: no display?");
            return 2;
        }

        _ui = new GLibContext();
        SynchronizationContext.SetSynchronizationContext(_ui);
        var data = _options.DataDirectory ?? throw new ArgumentException("Pass --data-dir <folder> (never the user's home).");
        var dataPath = Path.Combine(Path.GetFullPath(data), "data");
        var cachePath = Path.Combine(Path.GetFullPath(data), "cache");
#if SPAR_GTK4
        var session = WebKit.webkit_network_session_new(dataPath, cachePath);
        var context = WebKit.webkit_web_context_new();
        ConfigureContext(context);
        _webView = GObject.New(
            WebKit.webkit_web_view_get_type(),
            new("web-context", context, WebKit.webkit_web_context_get_type()),
            new("network-session", session, WebKit.webkit_network_session_get_type()));
        GObject.g_object_unref(session);
#else
        var manager = GObject.New(
            WebKit.webkit_website_data_manager_get_type(),
            new("base-data-directory", dataPath),
            new("base-cache-directory", cachePath));
        var context = WebKit.webkit_web_context_new_with_website_data_manager(manager);
        GObject.g_object_unref(manager);
        ConfigureContext(context);
        _webView = GObject.New(WebKit.webkit_web_view_get_type(), new GProperty("web-context", context, WebKit.webkit_web_context_get_type()));
#endif

        var settings = WebKit.webkit_web_view_get_settings(_webView);
        Log.Write("MEDIA_STREAM_DEFAULT " + WebKitPolicy.webkit_settings_get_enable_media_stream(settings));
        if (_options.Has("--media-stream"))
        {
            WebKitPolicy.webkit_settings_set_enable_media_stream(settings, 1);
        }

        var contentManager = WebKit.webkit_web_view_get_user_content_manager(_webView);
#if SPAR_GTK4
        WebKit.webkit_user_content_manager_register_script_message_handler(contentManager, "spar", null);
#else
        WebKit.webkit_user_content_manager_register_script_message_handler(contentManager, "spar");
#endif
        GObject.Connect(contentManager, "script-message-received::spar", (delegate* unmanaged<nint, nint, nint, void>)&OnScriptMessage);
        if (_options.Inject)
        {
            var script = WebKit.webkit_user_script_new(Injected.Script, WebKit.InjectTopFrame, WebKit.InjectAtDocumentStart, 0, 0);
            WebKit.webkit_user_content_manager_add_script(contentManager, script);
            WebKit.webkit_user_script_unref(script);
        }

        GObject.Connect(_webView, "decide-policy", (delegate* unmanaged<nint, nint, int, nint, int>)&OnDecidePolicy);
        GObject.Connect(_webView, "create", (delegate* unmanaged<nint, nint, nint, nint>)&OnCreate);
        GObject.Connect(_webView, "permission-request", (delegate* unmanaged<nint, nint, nint, int>)&OnPermissionRequest);
        GObject.Connect(_webView, "load-changed", (delegate* unmanaged<nint, int, nint, void>)&OnLoadChanged);

        _window = Gtk.gtk_window_new(
#if !SPAR_GTK4
            0
#endif
            );
        Gtk.gtk_window_set_title(_window, Title);
        Gtk.gtk_window_set_default_size(_window, 1000, 720);
#if SPAR_GTK4
        Gtk.gtk_window_set_child(_window, _webView);
        Gtk.gtk_window_present(_window);
#else
        Gtk.gtk_container_add(_window, _webView);
        Gtk.gtk_widget_show_all(_window);
#endif
        GObject.Connect(_window, "destroy", (delegate* unmanaged<nint, nint, void>)&OnWindowDestroyed);

        var start = _options.StartUrl ?? (_options.DevUrl is { } dev ? new Uri(new Uri(dev), _options.Page).ToString() : AppOrigin + _options.Page);
        Log.Write("NAVIGATE " + start);
        WebKit.webkit_web_view_load_uri(_webView, start);
        _ = TimeoutAsync();
        _loop = GLib.g_main_loop_new(0, 0);
        GLib.g_main_loop_run(_loop);
        GObject.g_object_unref(context);
        WaitForChildren();
        GLib.g_main_loop_unref(_loop);
        _ui.Dispose();
        return 0;
    }

    private unsafe void ConfigureContext(nint context)
    {
        WebKit.webkit_web_context_register_uri_scheme(context, "spar", &OnSchemeRequest, 0, 0);
        var security = WebKit.webkit_web_context_get_security_manager(context);
        if (!_options.Has("--no-secure"))
        {
            WebKit.webkit_security_manager_register_uri_scheme_as_secure(security, "spar");
        }

        if (!_options.Has("--no-cors"))
        {
            WebKit.webkit_security_manager_register_uri_scheme_as_cors_enabled(security, "spar");
        }
    }

    private async Task TimeoutAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        if (!_closing)
        {
            Log.Write("TIMEOUT");
            Close();
        }
    }

    private void Close()
    {
        _closing = true;
        if (_window != 0)
        {
            Gtk.gtk_window_close(_window);
        }
    }

    // ---- Navigation lock, new windows, permissions ----

    private bool IsAppUri(string uri) =>
        uri == _origin || uri.StartsWith(_origin + "/", StringComparison.Ordinal) || uri == "about:blank" || uri == "about:srcdoc";

    [UnmanagedCallersOnly]
    private static int OnDecidePolicy(nint webView, nint decision, int type, nint data)
    {
        var host = s_current!;
        try
        {
            switch (type)
            {
                case WebKitPolicy.NavigationAction:
                case WebKitPolicy.NewWindowAction:
                {
                    var action = WebKitPolicy.webkit_navigation_policy_decision_get_navigation_action(decision);
                    var uri = Marshal.PtrToStringUTF8(WebKitPolicy.webkit_uri_request_get_uri(WebKitPolicy.webkit_navigation_action_get_request(action))) ?? "";
                    var detail = $"{(type == 0 ? "NAVIGATION" : "NEW_WINDOW")} user={WebKitPolicy.webkit_navigation_action_is_user_gesture(action)} redirect={WebKitPolicy.webkit_navigation_action_is_redirect(action)} button={WebKitPolicy.webkit_navigation_action_get_mouse_button(action)} navtype={WebKitPolicy.webkit_navigation_action_get_navigation_type(action)} frame={Marshal.PtrToStringUTF8(WebKitPolicy.webkit_navigation_action_get_frame_name(action)) ?? "-"} {uri}";
                    if (type == WebKitPolicy.NewWindowAction)
                    {
                        // No new windows at all, even with the lock off (they would need a host window).
                        WebKitPolicy.webkit_policy_decision_ignore(decision);
                        Log.Write("NEW_WINDOW_BLOCKED " + detail);
                        return 1;
                    }

                    if (host._options.NavigationLock && !host.IsAppUri(uri))
                    {
                        WebKitPolicy.webkit_policy_decision_ignore(decision);
                        Log.Write("NAV_BLOCKED " + detail);
                        return 1;
                    }

                    Log.Write("NAV_ALLOWED " + detail);
                    return 0; // Default handling (use).
                }

                case WebKitPolicy.Response:
                {
                    var request = WebKitPolicy.webkit_response_policy_decision_get_request(decision);
                    var uri = Marshal.PtrToStringUTF8(WebKitPolicy.webkit_uri_request_get_uri(request)) ?? "";
                    if (WebKitPolicy.webkit_response_policy_decision_is_main_frame_main_resource(decision) != 0 || !host.IsAppUri(uri))
                    {
                        Log.Write($"RESPONSE_POLICY main={WebKitPolicy.webkit_response_policy_decision_is_main_frame_main_resource(decision)} {uri}");
                    }

                    return 0;
                }
            }
        }
        catch (Exception exception)
        {
            Log.Write("DECIDE_POLICY_FAILURE " + exception.Message);
        }

        return 0;
    }

    /// <summary>Backstop: if a new-window decision was ever used, creating the view is refused here.</summary>
    [UnmanagedCallersOnly]
    private static nint OnCreate(nint webView, nint action, nint data)
    {
        var uri = Marshal.PtrToStringUTF8(WebKitPolicy.webkit_uri_request_get_uri(WebKitPolicy.webkit_navigation_action_get_request(action))) ?? "";
        Log.Write("CREATE_REFUSED " + uri);
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int OnPermissionRequest(nint webView, nint request, nint data)
    {
        WebKitPolicy.webkit_permission_request_deny(request);
        Log.Write("PERMISSION_DENIED kind=" + GType.Name(request));
        return 1;
    }

    [UnmanagedCallersOnly]
    private static void OnLoadChanged(nint webView, int loadEvent, nint data)
    {
        if (loadEvent == WebKit.LoadCommitted || loadEvent == WebKit.LoadFinished)
        {
            Log.Write($"LOAD_{(loadEvent == WebKit.LoadCommitted ? "COMMITTED" : "FINISHED")} {Marshal.PtrToStringUTF8(WebKit.webkit_web_view_get_uri(webView))}");
        }
    }

    [UnmanagedCallersOnly]
    private static void OnWindowDestroyed(nint widget, nint data)
    {
        var host = s_current!;
        host._window = 0;
        host._webView = 0;
        GLib.g_main_loop_quit(host._loop);
    }

    [UnmanagedCallersOnly]
    private static void OnScriptMessage(nint contentManager, nint value, nint data)
    {
#if !SPAR_GTK4
        value = WebKit.webkit_javascript_result_get_js_value(value);
#endif
        Log.Write($"NATIVE source={Marshal.PtrToStringUTF8(WebKit.webkit_web_view_get_uri(s_current!._webView))} {GLib.TakeString(Jsc.jsc_value_to_string(value))}");
    }

    // ---- The app origin ----

    [UnmanagedCallersOnly]
    private static void OnSchemeRequest(nint request, nint data)
    {
        try
        {
            s_current!.HandleSchemeRequest(request);
        }
        catch (Exception exception)
        {
            Log.Write("SCHEME_FAILURE " + exception);
        }
    }

    private void HandleSchemeRequest(nint request)
    {
        var uri = new Uri(Marshal.PtrToStringUTF8(WebKitPolicy.webkit_uri_scheme_request_get_uri(request)) ?? AppOrigin + "/");
        var method = Marshal.PtrToStringUTF8(WebKit.webkit_uri_scheme_request_get_http_method(request)) ?? "GET";
        var headers = WebKit.webkit_uri_scheme_request_get_http_headers(request);
        string? Header(string name) => Soup.Header(headers, name);
        var body = ReadBody(request);
        if (!uri.AbsolutePath.StartsWith("/_spar/log", StringComparison.Ordinal))
        {
            Log.Write($"SERVE {method} {uri.PathAndQuery} referer={Header("Referer") ?? "-"} origin={Header("Origin") ?? "-"}");
        }

        var response = _routes.Handle(method, uri.PathAndQuery, body, Header);
        var stream = Gio.MemoryStream(response.Body);
        var reply = WebKit.webkit_uri_scheme_response_new(stream, response.Body.Length);
        WebKit.webkit_uri_scheme_response_set_status(reply, (uint)response.Status, response.Reason);
        // --plain-types: the bare MIME type, without "; charset=utf-8" (checks how nosniff reads the type).
        var type = _options.Has("--plain-types") ? response.ContentType.Split(';')[0] : response.ContentType;
        WebKit.webkit_uri_scheme_response_set_content_type(reply, type);
        var soup = Soup.soup_message_headers_new(Soup.MessageHeadersResponse);
        if (!_options.Has("--no-type-header"))
        {
            // nosniff reads the Content-Type header: set_content_type alone left it empty for the check (see RESULTS.md).
            Soup.soup_message_headers_append(soup, "Content-Type", type);
        }

        foreach (var (name, value) in response.Headers)
        {
            Soup.soup_message_headers_append(soup, name, value);
        }

        WebKit.webkit_uri_scheme_response_set_http_headers(reply, soup);
        WebKit.webkit_uri_scheme_request_finish_with_response(request, reply);
        GObject.g_object_unref(reply);
        GObject.g_object_unref(stream);
    }

    private static byte[] ReadBody(nint request)
    {
        var stream = WebKit.webkit_uri_scheme_request_get_http_body(request);
        if (stream == 0)
        {
            return [];
        }

        try
        {
            return Gio.ReadAll(stream);
        }
        finally
        {
            GObject.g_object_unref(stream);
        }
    }

    private void OnPageMessage(JsonElement message)
    {
        switch (message.TryGetProperty("kind", out var kind) ? kind.GetString() : null)
        {
            case "click":
                var button = message.GetProperty("button").GetString() == "middle" ? "2" : "1";
                var x = message.GetProperty("x").GetInt32();
                var y = message.GetProperty("y").GetInt32();
                var modifiers = message.GetProperty("modifiers").GetInt32();
                _ = Task.Run(() => Click(button, x, y, modifiers));
                break;
            case "open-external":
                OpenExternal(message.GetProperty("url").GetString()!);
                break;
            case "done":
                _ui!.Post(static async state =>
                {
                    var host = (LinuxHost)state!;
                    await Task.Delay(int.Parse(host._options.Value("--linger") ?? "300", System.Globalization.CultureInfo.InvariantCulture));
                    host.Close();
                }, this);
                break;
        }
    }

    /// <summary>Trusted input through XTEST, relative to the host window (X11 / Xvfb only).</summary>
    private static void Click(string button, int x, int y, int modifiers)
    {
        var window = Run("xdotool", ["search", "--onlyvisible", "--name", "^" + Title + "$"]).Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (window is null)
        {
            Log.Write("CLICK_FAILED no window");
            return;
        }

        var key = (modifiers & 2) != 0 ? "ctrl" : (modifiers & 8) != 0 ? "shift" : null;
        List<string> args = ["mousemove", "--window", window, x.ToString(System.Globalization.CultureInfo.InvariantCulture), y.ToString(System.Globalization.CultureInfo.InvariantCulture)];
        if (key is not null)
        {
            args.AddRange(["keydown", key]);
        }

        args.AddRange(["click", button]);
        if (key is not null)
        {
            args.AddRange(["keyup", key]);
        }

        Run("xdotool", args);
        Log.Write($"CLICKED button={button} at {x},{y} modifiers={modifiers} window={window}");
    }

    private static string Run(string file, IEnumerable<string> args)
    {
        var start = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output;
    }

    /// <summary>The explicit "open in the system browser" path: http(s) only, through GIO's default handler.</summary>
    private static void OpenExternal(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            Log.Write("OPEN_EXTERNAL_REFUSED " + url);
            return;
        }

        var timer = Stopwatch.StartNew();
        var ok = GioApps.g_app_info_launch_default_for_uri(uri.AbsoluteUri, 0, out var error);
        var message = ok != 0 ? "" : GLib.ErrorMessage(error);
        if (error != 0)
        {
            GLib.g_error_free(error);
        }

        Log.Write($"OPEN_EXTERNAL_GIO ok={ok} {timer.Elapsed.TotalMilliseconds:F1}ms error=\"{message}\" {uri.AbsoluteUri}");
    }

    private static void WaitForChildren()
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(10))
        {
            var children = Directory.EnumerateDirectories("/proc")
                .Select(d => Path.Combine(d, "stat"))
                .Where(File.Exists)
                .Select(f =>
                {
                    try
                    {
                        return File.ReadAllText(f);
                    }
                    catch (IOException)
                    {
                        return "";
                    }
                })
                .Count(s => s.Contains("WebKit", StringComparison.Ordinal) && !s.Contains(") Z ", StringComparison.Ordinal));
            if (children == 0)
            {
                Log.Write($"WEBKIT_EXIT after={timer.ElapsedMilliseconds}ms");
                return;
            }

            while (GLib.g_main_context_iteration(0, 0) != 0)
            {
            }

            Thread.Sleep(5);
        }

        Log.Write("WEBKIT_STILL_RUNNING after 10 s");
    }
}
