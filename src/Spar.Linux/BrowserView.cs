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
/// Nothing is recovered once the teardown has begun, nor a process the host ended itself.
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
    private readonly Action<int, string> _fail;
    private GCHandle _self;
    private nint _view;
    private nint _session;
    private nint _content;
    private nuint _messageHandler;
    private nuint _loadHandler;
    private nuint _terminatedHandler;
    private bool _ending;
    private bool _reloadUsed;

    /// <param name="fail">An unrecoverable failure: the exit code and a message.</param>
    public BrowserView(MainLoop ui, HostLog log, SchemeServer pages, ScriptBridge bridge, Action<int, string> fail)
    {
        _ui = ui;
        _log = log;
        _pages = pages;
        _bridge = bridge;
        _fail = fail;
        bridge.DocumentReady += () => _reloadUsed = false;
    }

    /// <summary>
    /// The view, a widget with a floating reference until its window takes it; 0 before <see cref="Create"/> and after
    /// <see cref="Close"/>.
    /// </summary>
    public nint Handle => _view;

    /// <summary>
    /// Creates the network session, the content manager with the relay, and the view over both, and registers the
    /// scheme on the view's context (loop thread). The view is not loading yet: the window attaches it first.
    /// </summary>
    public unsafe void Create(string dataFolder, string cacheFolder, string relay, SparColor background)
    {
        _self = GCHandle.Alloc(this);
        var self = GCHandle.ToIntPtr(_self);
        _session = WebKit.webkit_network_session_new(dataFolder, cacheFolder);
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
        _pages.Register(WebKit.webkit_web_view_get_context(_view));
        SetBackground(background);
        _loadHandler = GLib.g_signal_connect_data(
            _view, "load-changed", (delegate* unmanaged<nint, int, nint, void>)&OnLoadChanged, self, 0, 0);
        _terminatedHandler = GLib.g_signal_connect_data(
            _view, "web-process-terminated", (delegate* unmanaged<nint, int, nint, void>)&OnWebProcessTerminated, self, 0, 0);
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
                _bridge.OnLoadStarted();
                break;
            case LoadCommitted:
                var address = Marshal.PtrToStringUTF8(WebKit.webkit_web_view_get_uri(_view)) ?? "";
                _log.Phase("page committed");
                _bridge.OnLoadCommitted(address);
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
        WebKit.webkit_web_view_reload(_view);
        _log.Step("page reloaded after the web process ended");
    }

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
