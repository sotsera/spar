using System.Runtime.InteropServices;
using Spar.Hosting;
using Spar.Linux.Interop;

namespace Spar.Linux;

/// <summary>
/// The window's WebKit view, on the main loop (plan section 3, "Linux"): its network session over the app's own data
/// and cache folders, its content manager with the bridge's handler and relay in the isolated world
/// (<see cref="ScriptBridge.World"/>), the <c>spar</c> scheme, and the signals the host follows. It recovers from a web
/// process that ends (plan section 3, "Recovery"): its document ends with it, and the page is reloaded once, a budget
/// given back when a document reports <c>ui-ready</c>; a second loss before that is unrecoverable (exit code 5).
/// Nothing is recovered once the teardown has begun, nor a process the host ended itself: that is "Reload page" for a
/// hung page (plan section 3, "Hung pages"), which reloads once the process is gone, or the teardown of a hung page.
/// </summary>
internal sealed class BrowserView
{
    /// <summary><c>WEBKIT_LOAD_STARTED</c>.</summary>
    private const int LoadStarted = 0;

    /// <summary><c>WEBKIT_LOAD_COMMITTED</c>.</summary>
    private const int LoadCommitted = 2;

    /// <summary><c>WEBKIT_WEB_PROCESS_TERMINATED_BY_API</c>; 0 is a crash and 1 the memory limit.</summary>
    private const int TerminatedByApi = 2;

    private readonly MainLoop _ui;
    private readonly HostLog _log;
    private readonly SchemeServer _pages;
    private readonly ScriptBridge _bridge;
    private readonly PageGuard _guard;
    private readonly BrowserDefaults _defaults;
    private readonly BrowserProfile _profile;
    private readonly Action<int, string> _fail;
    private GCHandle _self;
    private nint _view;
    private nint _session;
    private nint _content;
    private nuint _messageHandler;
    private nuint _loadHandler;
    private nuint _terminatedHandler;
    private nuint _responsiveHandler;
    private nuint _policyHandler;
    private nuint _permissionHandler;
    private nuint _queryHandler;
    private nuint _menuHandler;
    private nuint _chooserHandler;
    private nuint _downloadHandler;
    private nint _input;
    private nuint _inputHandler;
    private bool _ending;
    private bool _reloadUsed;
    private bool _reloadAfterTermination;

    /// <param name="fail">An unrecoverable failure: the exit code and a message.</param>
    public BrowserView(
        MainLoop ui,
        HostLog log,
        SchemeServer pages,
        ScriptBridge bridge,
        PageGuard guard,
        BrowserDefaults defaults,
        BrowserProfile profile,
        Action<int, string> fail)
    {
        _ui = ui;
        _log = log;
        _pages = pages;
        _bridge = bridge;
        _guard = guard;
        _defaults = defaults;
        _profile = profile;
        _fail = fail;
        bridge.DocumentReady += () => _reloadUsed = false;
    }

    /// <summary>
    /// Raised on the loop when WebKit reports the web process unresponsive (<c>is-web-process-responsive</c> turned
    /// false). It stays true for an idle hang (pitfalls), so the heartbeat is what finds those; this is one more report
    /// of the same state.
    /// </summary>
    public event Action? EngineUnresponsive;

    /// <summary>
    /// The view, a widget with a floating reference until its window takes it; 0 before <see cref="Create"/> and after
    /// <see cref="Close"/>.
    /// </summary>
    public nint Handle => _view;

    /// <summary>
    /// Creates the network session over the app's own data and cache folders, passed explicitly (plan section 3,
    /// "Data folders"), the content manager with the relay, and the view over both; registers the scheme on the view's
    /// context and connects the navigation lock and the permission gate (<see cref="PageGuard"/>), and the browser
    /// settings with their handlers (<see cref="BrowserDefaults"/>), before the view loads anything (loop thread). The
    /// view is not loading yet: the window attaches it first.
    /// </summary>
    public unsafe void Create(string dataFolder, string cacheFolder, string relay, SparColor background)
    {
        _self = GCHandle.Alloc(this);
        var self = GCHandle.ToIntPtr(_self);
        _session = WebKit.webkit_network_session_new(dataFolder, cacheFolder);
        CheckFolders(dataFolder, cacheFolder);
        _downloadHandler = Connect(_session, "download-started", (delegate* unmanaged<nint, nint, nint, void>)&OnDownloadStarted, self);
        _content = WebKit.webkit_user_content_manager_new();
        if (WebKit.webkit_user_content_manager_register_script_message_handler(_content, ScriptBridge.Handler, ScriptBridge.World) == 0)
        {
            throw new InvalidOperationException("WebKit refused the bridge's script message handler.");
        }

        _messageHandler = GLib.g_signal_connect_data(
            _content,
            "script-message-received::" + ScriptBridge.Handler,
            (delegate* unmanaged<nint, nint, nint, void>)&OnScriptMessage,
            self,
            0,
            0);
        AddRelay(relay);

        _view = NewView(_session, _content);
        var context = WebKit.webkit_web_view_get_context(_view);
        _pages.Register(context);
        SetBackground(background);
        _loadHandler = GLib.g_signal_connect_data(
            _view, "load-changed", (delegate* unmanaged<nint, int, nint, void>)&OnLoadChanged, self, 0, 0);
        _terminatedHandler = GLib.g_signal_connect_data(
            _view, "web-process-terminated", (delegate* unmanaged<nint, int, nint, void>)&OnWebProcessTerminated, self, 0, 0);
        _responsiveHandler = GLib.g_signal_connect_data(
            _view, "notify::is-web-process-responsive", (delegate* unmanaged<nint, nint, nint, void>)&OnResponsiveChanged, self, 0, 0);
        _policyHandler = GLib.g_signal_connect_data(
            _view, "decide-policy", (delegate* unmanaged<nint, nint, int, nint, int>)&OnDecidePolicy, self, 0, 0);
        _permissionHandler = GLib.g_signal_connect_data(
            _view, "permission-request", (delegate* unmanaged<nint, nint, nint, int>)&OnPermissionRequest, self, 0, 0);
        _queryHandler = GLib.g_signal_connect_data(
            _view, "query-permission-state", (delegate* unmanaged<nint, nint, nint, int>)&OnPermissionQuery, self, 0, 0);
        _menuHandler = Connect(_view, "context-menu", (delegate* unmanaged<nint, nint, nint, nint, int>)&OnContextMenu, self);
        _chooserHandler = Connect(_view, "run-file-chooser", (delegate* unmanaged<nint, nint, nint, int>)&OnFileChooser, self);
        _defaults.Apply(_view);

        // Real input on the view, seen before WebKit handles it: only a recent one lets a new window open outside.
        _input = Gtk.gtk_event_controller_legacy_new();
        Gtk.gtk_event_controller_set_propagation_phase(_input, 1);
        _inputHandler = GLib.g_signal_connect_data(_input, "event", (delegate* unmanaged<nint, nint, nint, int>)&OnInputEvent, self, 0, 0);
        Gtk.gtk_widget_add_controller(_view, _input);
        _bridge.Attach(_view);
        _log.Phase("web view created");
    }

    /// <summary>
    /// The view's colour before the page paints (loop thread): opaque, the window's, since a transparent view shows
    /// black without a compositor (plan section 3d).
    /// </summary>
    public unsafe void SetBackground(SparColor background)
    {
        if (_view == 0)
        {
            return;
        }

        var colour = new GdkRgba
        {
            Red = background.Red / 255f,
            Green = background.Green / 255f,
            Blue = background.Blue / 255f,
            Alpha = 1,
        };
        WebKit.webkit_web_view_set_background_color(_view, &colour);
    }

    /// <summary>Opens the app's page (loop thread).</summary>
    public void Load() => WebKit.webkit_web_view_load_uri(_view, SchemeServer.StartUrl);

    /// <summary>No recovery from here on (loop thread): an approved close has started.</summary>
    public void EndRecovery() => _ending = true;

    /// <summary>
    /// "Reload page" in the hung-page dialog (plan section 3, "Hung pages"; loop thread): reloading alone never recovers
    /// a hung page (pitfalls), so the web process is ended first, and the page loads again once WebKit reports it gone
    /// (<c>TERMINATED_BY_API</c>, 0–3 ms later in <c>spikes/page-heartbeat</c>). This is the user's choice, so it does
    /// not use up the automatic reload.
    /// </summary>
    public void ReloadHungPage()
    {
        if (_view == 0 || _ending)
        {
            return;
        }

        _reloadAfterTermination = true;
        WebKit.webkit_web_view_terminate_web_process(_view);
        _log.Step("web process of the hung page ended");
    }

    /// <summary>
    /// The teardown of a window whose page does not answer (loop thread): its web process is ended before the window is
    /// destroyed, since a hung web process outlives the app by about 10 s otherwise (pitfalls).
    /// </summary>
    public void TerminateHungWebProcess()
    {
        if (_view == 0)
        {
            return;
        }

        _ending = true;
        WebKit.webkit_web_view_terminate_web_process(_view);
        _log.Step("web process of the hung page ended before closing");
    }

    /// <summary>Runs <paramref name="script"/> in the page's own world, ignoring its result (loop thread; Development triggers).</summary>
    public unsafe void RunPageScript(string script)
    {
        if (_view == 0)
        {
            return;
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(script);
        fixed (byte* text = bytes)
        {
            WebKit.webkit_web_view_evaluate_javascript(_view, text, bytes.Length, null, 0, 0, 0, 0);
        }
    }

    /// <summary>
    /// Teardown (loop thread), before the window that holds the view is destroyed: no recovery, the signals
    /// disconnected, the bridge detached, and the session and content manager released. The window's destruction then
    /// releases the view, and the web processes exit once nothing holds them.
    /// </summary>
    public void Close()
    {
        _ending = true;
        if (_view != 0)
        {
            GLib.g_signal_handler_disconnect(_view, _loadHandler);
            GLib.g_signal_handler_disconnect(_view, _terminatedHandler);
            GLib.g_signal_handler_disconnect(_view, _responsiveHandler);
            GLib.g_signal_handler_disconnect(_view, _policyHandler);
            GLib.g_signal_handler_disconnect(_view, _permissionHandler);
            GLib.g_signal_handler_disconnect(_view, _queryHandler);
            GLib.g_signal_handler_disconnect(_view, _menuHandler);
            GLib.g_signal_handler_disconnect(_view, _chooserHandler);
            GLib.g_signal_handler_disconnect(_input, _inputHandler);
            _bridge.Detach("window closing");
            _view = 0;
        }

        if (_content != 0)
        {
            GLib.g_signal_handler_disconnect(_content, _messageHandler);
            WebKit.webkit_user_content_manager_unregister_script_message_handler(_content, ScriptBridge.Handler, ScriptBridge.World);
            GLib.g_object_unref(_content);
            _content = 0;
        }

        if (_session != 0)
        {
            GLib.g_signal_handler_disconnect(_session, _downloadHandler);
            GLib.g_object_unref(_session);
            _session = 0;
        }
    }

    /// <summary>Frees the handle the signals found this view by, once the main loop has ended and nothing can call back.</summary>
    public void Release()
    {
        if (_self.IsAllocated)
        {
            _self.Free();
        }
    }

    /// <summary>
    /// Connects a handler that a security default depends on: a signal WebKit does not have (0) stops the start, so a
    /// default is never silently missing.
    /// </summary>
    private static unsafe nuint Connect(nint instance, string signal, void* handler, nint self)
    {
        var id = GLib.g_signal_connect_data(instance, signal, handler, self, 0, 0);
        return id != 0 ? id : throw new InvalidOperationException($"WebKit has no '{signal}' signal, which a security default needs.");
    }

    /// <summary>
    /// The view, with its session and content manager as construct-only properties: <c>g_object_new</c> is variadic, so
    /// its <c>g_object_new_with_properties</c> form. The values hold their own references while the view is built.
    /// </summary>
    private static unsafe nint NewView(nint session, nint content)
    {
        var values = stackalloc GValue[2];
        values[0] = default;
        values[1] = default;
        fixed (byte* sessionName = "network-session\0"u8)
        fixed (byte* contentName = "user-content-manager\0"u8)
        {
            var names = stackalloc nint[2] { (nint)sessionName, (nint)contentName };
            GLib.g_value_init(&values[0], WebKit.webkit_network_session_get_type());
            GLib.g_value_set_object(&values[0], session);
            GLib.g_value_init(&values[1], WebKit.webkit_user_content_manager_get_type());
            GLib.g_value_set_object(&values[1], content);
            try
            {
                return GLib.g_object_new_with_properties(WebKit.webkit_web_view_get_type(), 2, names, values);
            }
            finally
            {
                GLib.g_value_unset(&values[0]);
                GLib.g_value_unset(&values[1]);
            }
        }
    }

    /// <summary>
    /// The folders the session actually uses (plan section 3c), read back from its data manager: they are recorded in
    /// <see cref="BrowserProfile"/>, the folders no file grant may reach, and a moved one is logged, as on Windows.
    /// </summary>
    private void CheckFolders(string dataFolder, string cacheFolder)
    {
        var manager = WebKit.webkit_network_session_get_website_data_manager(_session);
        var data = Marshal.PtrToStringUTF8(WebKit.webkit_website_data_manager_get_base_data_directory(manager)) ?? "";
        var cache = Marshal.PtrToStringUTF8(WebKit.webkit_website_data_manager_get_base_cache_directory(manager)) ?? "";
        if (data.Length == 0 || cache.Length == 0)
        {
            _log.Warn("The browser's network session reports no data or cache folder: it may keep nothing on disk.");
            return;
        }

        if (_profile.RecordInUse(data) || !BrowserProfile.SameFolder(cache, cacheFolder))
        {
            _log.Warn($"Browser data folders differ from the ones Spar passed ({dataFolder}, {cacheFolder}): data {data}, cache {cache}");
            return;
        }

        _log.Info($"Browser data folder {data}, cache {cache}");
    }

    /// <summary>
    /// The relay, at document start, in the top frame, in the bridge's world, and only for documents of the app's
    /// origin (the relay checks it again itself).
    /// </summary>
    private unsafe void AddRelay(string relay)
    {
        fixed (byte* pattern = "spar://app/*\0"u8)
        {
            var allow = stackalloc nint[2] { (nint)pattern, 0 };
            var script = WebKit.webkit_user_script_new_for_world(
                relay, WebKit.InjectTopFrame, WebKit.InjectAtDocumentStart, ScriptBridge.World, allow, null);
            WebKit.webkit_user_content_manager_add_script(_content, script);
            WebKit.webkit_user_script_unref(script);
        }
    }

    private void LoadChanged(int loadEvent)
    {
        switch (loadEvent)
        {
            case LoadStarted:
                // A main-frame load that the lock let through only as an address declared for frames (decide-policy
                // cannot tell frames apart, and the view still names the old address here) is stopped at its response:
                // the current document and its bridge stay. Only a load of the app's origin ends the document's epoch.
                if (!_guard.LastLetThroughWasDeclaredFrame)
                {
                    _bridge.OnLoadStarted();
                }
                else
                {
                    WebKit.webkit_web_view_stop_loading(_view);
                    _guard.DeclaredFrameStopped();
                }

                break;
            case LoadCommitted:
                var address = Marshal.PtrToStringUTF8(WebKit.webkit_web_view_get_uri(_view)) ?? "";
                _log.Phase("page committed");
                _bridge.OnLoadCommitted(address);
                if (!SchemeServer.IsAppOrigin(address) && !_ending)
                {
                    // Nothing the lock lets through should commit here; if a document of another origin does, the
                    // window does not stay on it.
                    _guard.ForeignDocument(address);
                    WebKit.webkit_web_view_load_uri(_view, SchemeServer.StartUrl);
                }

                break;
        }
    }

    private void WebProcessTerminated(int reason)
    {
        _bridge.OnWebProcessEnded();
        var why = reason switch
        {
            0 => "crashed",
            1 => "exceeded its memory limit",
            _ => "ended by the host",
        };
        if (_ending)
        {
            _log.Info($"The web process ended during shutdown ({why}); nothing is recovered.");
            return;
        }

        if (reason == TerminatedByApi)
        {
            _log.Step($"web process {why}");
            if (_reloadAfterTermination)
            {
                _reloadAfterTermination = false;
                _guard.HostReloads();
                WebKit.webkit_web_view_reload(_view);
                _log.Step("hung page reloaded");
            }

            return;
        }

        if (_reloadUsed)
        {
            _log.Error($"The web process {why} again before the page was ready.");
            _fail(5, "The page stopped working again right after it was reloaded.");
            return;
        }

        _reloadUsed = true;
        _log.Warn($"The web process {why}; the page is reloaded once.");
        _guard.HostReloads();
        WebKit.webkit_web_view_reload(_view);
        _log.Step("page reloaded after the web process ended");
    }

    private void ResponsiveChanged()
    {
        if (_view == 0)
        {
            return;
        }

        if (WebKit.webkit_web_view_get_is_web_process_responsive(_view) != 0)
        {
            _log.Info("WebKit reports the web process responsive again.");
            return;
        }

        _log.Warn("WebKit reports the web process is not responding.");
        EngineUnresponsive?.Invoke();
    }

    /// <summary>The address of the view's document (loop thread), or empty.</summary>
    private string CurrentAddress() => _view == 0 ? "" : Marshal.PtrToStringUTF8(WebKit.webkit_web_view_get_uri(_view)) ?? "";

    private void Message(nint value)
    {
        if (WebKit.jsc_value_is_string(value) == 0)
        {
            _log.Warn("Bridge: a script message that is not a string was ignored.");
            return;
        }

        if (GLib.TakeString(WebKit.jsc_value_to_string(value)) is { } text)
        {
            _bridge.OnMessage(text);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnLoadChanged(nint view, int loadEvent, nint data) =>
        Dispatch(data, "load-changed", browser => browser.LoadChanged(loadEvent));

    [UnmanagedCallersOnly]
    private static void OnWebProcessTerminated(nint view, int reason, nint data) =>
        Dispatch(data, "web-process-terminated", browser => browser.WebProcessTerminated(reason));

    /// <summary><c>notify::is-web-process-responsive</c>: the object, its <c>GParamSpec</c> and the user data.</summary>
    [UnmanagedCallersOnly]
    private static void OnResponsiveChanged(nint view, nint property, nint data) =>
        Dispatch(data, "web-process-responsive", browser => browser.ResponsiveChanged());

    /// <summary>
    /// The input controller's <c>event</c> for every event of the view, motion included, so it only notes a press or a
    /// release of a button, a key or a touch, and never stops one (FALSE): WebKit handles each as before.
    /// </summary>
    [UnmanagedCallersOnly]
    private static int OnInputEvent(nint controller, nint gdkEvent, nint data)
    {
        if (Gtk.gdk_event_get_event_type(gdkEvent) is 2 or 3 or 4 or 17 or 19 && GCHandle.FromIntPtr(data).Target is BrowserView browser)
        {
            browser._guard.NoteInput();
        }

        return 0;
    }

    /// <summary><c>decide-policy</c>: the view, the decision, its type and the user data; TRUE when answered here.</summary>
    [UnmanagedCallersOnly]
    private static int OnDecidePolicy(nint view, nint decision, int type, nint data) => Answer(
        data, "decide-policy", browser => browser._guard.OnDecidePolicy(decision, type), () => WebKit.webkit_policy_decision_ignore(decision));

    /// <summary><c>permission-request</c>: always answered here (TRUE), so WebKit's own default never decides.</summary>
    [UnmanagedCallersOnly]
    private static int OnPermissionRequest(nint view, nint request, nint data) => Answer(
        data,
        "permission-request",
        browser => browser._guard.OnPermissionRequest(request, browser.CurrentAddress()),
        () => WebKit.webkit_permission_request_deny(request));

    /// <summary><c>query-permission-state</c>: answered here (TRUE); WebKit's default, "prompt", only if that fails.</summary>
    [UnmanagedCallersOnly]
    private static int OnPermissionQuery(nint view, nint query, nint data) =>
        Answer(data, "query-permission-state", browser => browser._guard.OnPermissionQuery(query), refuse: null);

    /// <summary>
    /// <c>context-menu</c> in 6.0: the view, the menu, the hit test result and the user data; TRUE shows no menu. A
    /// decision that fails shows none.
    /// </summary>
    [UnmanagedCallersOnly]
    private static int OnContextMenu(nint view, nint menu, nint hitTest, nint data) =>
        Answer(data, "context-menu", browser => browser._defaults.OnContextMenu(menu, hitTest), () => { });

    /// <summary><c>run-file-chooser</c>: TRUE when answered here; a decision that fails cancels the picker.</summary>
    [UnmanagedCallersOnly]
    private static int OnFileChooser(nint view, nint request, nint data) => Answer(
        data, "run-file-chooser", browser => browser._defaults.OnFileChooser(request), () => WebKit.webkit_file_chooser_request_cancel(request));

    /// <summary><c>download-started</c> on the network session: the session, the download and the user data.</summary>
    [UnmanagedCallersOnly]
    private static void OnDownloadStarted(nint session, nint download, nint data)
    {
        if (GCHandle.FromIntPtr(data).Target is not BrowserView browser)
        {
            WebKit.webkit_download_cancel(download);
            return;
        }

        using var scope = browser._ui.Enter("download-started");
        try
        {
            browser._defaults.OnDownloadStarted(download);
        }
        catch (Exception exception)
        {
            browser._ui.Report(exception);
            WebKit.webkit_download_cancel(download);
        }
    }

    /// <summary>
    /// Runs a signal's decision in a named scope; nothing unwinds into GLib. A decision that cannot be made fails
    /// closed: <paramref name="refuse"/> answers it (WebKit ignores a second answer), so a failure never lets a
    /// navigation or a permission through.
    /// </summary>
    private static int Answer(nint data, string name, Func<BrowserView, bool> decide, Action? refuse)
    {
        if (GCHandle.FromIntPtr(data).Target is not BrowserView browser)
        {
            refuse?.Invoke();
            return refuse is null ? 0 : 1;
        }

        using var scope = browser._ui.Enter(name);
        try
        {
            return decide(browser) ? 1 : 0;
        }
        catch (Exception exception)
        {
            browser._ui.Report(exception);
            refuse?.Invoke();
            return refuse is null ? 0 : 1;
        }
    }

    /// <summary>The bridge's hot path: one call per message, with no delegate made for it.</summary>
    [UnmanagedCallersOnly]
    private static void OnScriptMessage(nint manager, nint value, nint data)
    {
        if (GCHandle.FromIntPtr(data).Target is not BrowserView browser)
        {
            return;
        }

        using var scope = browser._ui.Enter("script message");
        try
        {
            browser.Message(value);
        }
        catch (Exception exception)
        {
            browser._ui.Report(exception);
        }
    }

    /// <summary>Runs a signal's work in a named scope; nothing unwinds into GLib.</summary>
    private static void Dispatch(nint data, string name, Action<BrowserView> work)
    {
        if (GCHandle.FromIntPtr(data).Target is not BrowserView browser)
        {
            return;
        }

        using var scope = browser._ui.Enter(name);
        try
        {
            work(browser);
        }
        catch (Exception exception)
        {
            browser._ui.Report(exception);
        }
    }
}
