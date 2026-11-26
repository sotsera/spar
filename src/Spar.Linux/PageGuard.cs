using System.Runtime.InteropServices;
using Spar.Hosting;
using Spar.Linux.Interop;
using Spar.Runtime;

namespace Spar.Linux;

/// <summary>
/// The navigation lock and the permission gate of plan section 3b on the view's signals, on the main loop, each answered
/// at once from what is in memory:
/// <list type="bullet">
/// <item><c>decide-policy</c> for a navigation, which WebKit raises for the main frame and for frames alike, with no
/// flag to tell them apart (<c>docs/evidence/csp-navigation</c>): it goes ahead on the app's origin and to an address the app
/// declared in <c>csp.FrameSrc(…)</c>; anything else is ignored and logged, <c>about:blank</c> and <c>about:srcdoc</c>
/// included, since one of them in the main frame would replace the app's page with nothing it could stop (a frame keeps
/// its first, blank document; a <c>srcdoc</c> frame stays empty). A declared frame address that is the main frame's own
/// document after all is stopped at its response, the one decision that knows the main frame;</item>
/// <item>every new-window decision is ignored, so the view never opens a window: a user's click to an allowed external
/// address opens it with the desktop's handler, on a thread of its own, when the view had a person's input within
/// <see cref="InputWindowMilliseconds"/>; anything else is logged;</item>
/// <item><c>permission-request</c> is the only gate on Linux, since WebKitGTK ignores <c>Permissions-Policy</c>: a
/// request is granted only for a feature the app declared, from the app's own page, and denied otherwise, with no
/// prompt. WebKitGTK remembers no answer;</item>
/// <item>a back or forward navigation to another document is ignored unless the app allowed history navigation
/// (plan section 3c);</item>
/// <item><c>query-permission-state</c> (<c>navigator.permissions.query</c>) is answered "denied" for what is not
/// declared and "prompt" for what is; WebKit itself shows a "denied" as "prompt" to the page, and a feature it granted
/// in this document as "granted".</item>
/// </list>
/// </summary>
internal sealed class PageGuard
{
    /// <summary>How many refused navigations and windows are logged in a run; a page that loops on them is counted only.</summary>
    internal const int LoggedRefusals = 100;

    /// <summary>
    /// How recent a person's input to the view must be for a new window to open an external address: the HTML transient
    /// activation Chromium gives a click (5 s). WebKitGTK's own gesture flag is not enough on its own: it also says "a
    /// user's gesture" for script that runs within about a second of a load the host started (found in M1c step 4).
    /// </summary>
    internal const int InputWindowMilliseconds = 5_000;

    private const string UserMediaRequest = "WebKitUserMediaPermissionRequest";

    /// <summary><c>WEBKIT_NAVIGATION_TYPE_BACK_FORWARD</c>.</summary>
    private const int BackForward = 2;

    private readonly HostLog _log;
    private readonly SparPageSecurity _security;
    private readonly bool _allowHistory;
    private int _refused;

    /// <summary>The host reloads the page itself (after a lost or ended web process); its next navigation is that load.</summary>
    private bool _hostReload;

    /// <summary>When the view last had a person's input (a <see cref="System.Diagnostics.Stopwatch"/> timestamp); 0 before any.</summary>
    private long _lastInput;

    /// <summary>The last address let through for frames only, for the log when the window starts to load it.</summary>
    private string _lastDeclaredFrame = "";

    /// <summary>The request kinds already logged, as bits by <see cref="RequestKind"/>.</summary>
    private int _loggedKinds;

    /// <param name="allowHistory"><c>AllowHistoryNavigation()</c>: back and forward to another document go ahead.</param>
    public PageGuard(HostLog log, SparPageSecurity security, bool allowHistory)
    {
        _log = log;
        _security = security;
        _allowHistory = allowHistory;
    }

    /// <summary>What a permission request asks for, as the log names it.</summary>
    internal enum RequestKind
    {
        Geolocation,
        Notifications,
        Camera,
        Microphone,
        CameraAndMicrophone,
        ScreenCapture,
        ClipboardRead,
        DeviceList,
        PointerLock,
        MediaKeys,
        StorageAccess,
        Xr,
        Other,
    }

    /// <summary>
    /// Whether the last navigation the lock let through was to an address the app declared for frames rather than to
    /// its own origin (loop thread). Every load of the window is decided before it starts, and the view still names the
    /// old address when it starts, so a load that starts right after such a decision is taken for that address in the
    /// main frame, which the lock stops at its response: the window's document and its bridge stay. The guess is wrong
    /// only when a frame's navigation is decided between the window's own decision and its start; the app's document
    /// then closes at its commit instead of its start.
    /// </summary>
    public bool LastLetThroughWasDeclaredFrame { get; private set; }

    /// <summary>
    /// Whether the lock lets a navigation of the main frame or of a frame go ahead: the app's own origin, and the
    /// addresses the app declared for frames.
    /// </summary>
    internal static bool Allows(string address, IReadOnlyList<string> frameSources) =>
        SchemeServer.IsAppOrigin(address) || NavigationRules.IsDeclaredFrame(address, frameSources, SchemeServer.Origin);
    /// <summary>
    /// What a permission request asks for and whether the app's declarations grant it: each feature it needs declared
    /// (the camera and the microphone both, for a request of both). The list of devices goes with the camera or the
    /// microphone; screen capture, notifications, pointer lock, media keys, storage access, XR and anything unknown have
    /// no declaration and are always denied.
    /// </summary>
    internal static (RequestKind Kind, bool Granted) Decide(
        string typeName, bool audio, bool video, bool display, Permission[] declared)
    {
        bool Has(Permission permission) => PageSecurityNames.Contains(declared, permission);
        return typeName switch
        {
            "WebKitGeolocationPermissionRequest" => (RequestKind.Geolocation, Has(Permission.Geolocation)),
            "WebKitNotificationPermissionRequest" => (RequestKind.Notifications, false),
            UserMediaRequest when display => (RequestKind.ScreenCapture, false),
            UserMediaRequest when audio && video => (RequestKind.CameraAndMicrophone, Has(Permission.Camera) && Has(Permission.Microphone)),
            UserMediaRequest when video => (RequestKind.Camera, Has(Permission.Camera)),
            UserMediaRequest when audio => (RequestKind.Microphone, Has(Permission.Microphone)),
            "WebKitClipboardPermissionRequest" => (RequestKind.ClipboardRead, Has(Permission.ClipboardRead)),
            "WebKitDeviceInfoPermissionRequest" => (RequestKind.DeviceList, Has(Permission.Camera) || Has(Permission.Microphone)),
            "WebKitPointerLockPermissionRequest" => (RequestKind.PointerLock, false),
            "WebKitMediaKeySystemPermissionRequest" => (RequestKind.MediaKeys, false),
            "WebKitWebsiteDataAccessPermissionRequest" => (RequestKind.StorageAccess, false),
            "WebKitXRPermissionRequest" => (RequestKind.Xr, false),
            _ => (RequestKind.Other, false),
        };
    }

    /// <summary>
    /// What <c>navigator.permissions.query</c> learns for <paramref name="name"/>: "prompt" for a declared feature (the
    /// page's request is then granted), "denied" for anything else.
    /// </summary>
    internal static int QueryState(string name, Permission[] declared)
    {
        Permission? permission = name switch
        {
            "geolocation" => Permission.Geolocation,
            "camera" => Permission.Camera,
            "microphone" => Permission.Microphone,
            "clipboard-read" => Permission.ClipboardRead,
            _ => null,
        };
        return permission is { } feature && PageSecurityNames.Contains(declared, feature) ? WebKit.PermissionPrompt : WebKit.PermissionDenied;
    }

    /// <summary>
    /// <c>decide-policy</c> (loop thread): true when the decision was answered here, false to leave it to WebKit's
    /// default, which lets a navigation or a response go ahead.
    /// </summary>
    public bool OnDecidePolicy(nint decision, int type)
    {
        switch (type)
        {
            case WebKit.PolicyNavigation:
                return Navigation(decision);
            case WebKit.PolicyNewWindow:
                NewWindow(decision);
                return true;
            case WebKit.PolicyResponse:
                return Response(decision);
            default:
                return false;
        }
    }

    /// <summary><c>permission-request</c> (loop thread): answered at once, always; <paramref name="page"/> is the view's address.</summary>
    public bool OnPermissionRequest(nint request, string page)
    {
        var type = Marshal.PtrToStringUTF8(GLib.g_type_name_from_instance(request)) ?? "";
        var media = type == UserMediaRequest;
        var (kind, granted) = Decide(
            type,
            media && WebKit.webkit_user_media_permission_is_for_audio_device(request) != 0,
            media && WebKit.webkit_user_media_permission_is_for_video_device(request) != 0,
            media && WebKit.webkit_user_media_permission_is_for_display_device(request) != 0,
            _security.Permissions);
        granted &= SchemeServer.IsAppOrigin(page);
        if (granted)
        {
            WebKit.webkit_permission_request_allow(request);
        }
        else
        {
            WebKit.webkit_permission_request_deny(request);
        }

        // Once per kind: a page asking in a loop must not flood the log.
        var bit = 1 << (int)kind;
        if ((_loggedKinds & bit) == 0)
        {
            _loggedKinds |= bit;
            var answer = granted ? "granted (declared)" : "denied (not declared in spar.Permissions, or not declarable)";
            _log.Info($"Permission request for {KindName(kind, type)} from {Shorten(page)}: {answer}; later requests of this kind are not logged.");
        }

        return true;
    }

    /// <summary>
    /// A document outside the app's origin committed in the window after all (loop thread), which the lock should have
    /// stopped before: the view loads the app's page again, so the window never stays on another document; this logs it.
    /// </summary>
    public void ForeignDocument(string address) =>
        Refused($"Navigation lock: the window's document became {Shorten(address)}; the app's page is loaded again.");

    /// <summary>
    /// The window started to load the address last let through for frames only (loop thread): the view stops the load
    /// at once, before its response; this logs it.
    /// </summary>
    public void DeclaredFrameStopped() =>
        Refused($"Navigation lock: the window started to load {Shorten(_lastDeclaredFrame)}, an address declared for frames only; the load was stopped.");

    /// <summary>
    /// The host is about to reload the page itself (loop thread): after a lost or ended web process, WebKit loads the
    /// current history entry again, which it decides as a back or forward navigation (found in M1c step 6), so the next
    /// navigation to the app's own origin goes ahead whatever its type.
    /// </summary>
    public void HostReloads() => _hostReload = true;

    /// <summary>A press or release of a button, a key or a touch on the view (loop thread, from its input controller).</summary>
    public void NoteInput() => _lastInput = System.Diagnostics.Stopwatch.GetTimestamp();

    /// <summary><c>query-permission-state</c> (loop thread): answered at once.</summary>
    public bool OnPermissionQuery(nint query)
    {
        var name = Marshal.PtrToStringUTF8(WebKit.webkit_permission_state_query_get_name(query)) ?? "";
        WebKit.webkit_permission_state_query_finish(query, QueryState(name, _security.Permissions));
        return true;
    }

    private static string KindName(RequestKind kind, string type) => kind switch
    {
        RequestKind.Geolocation => "geolocation",
        RequestKind.Notifications => "notifications",
        RequestKind.Camera => "the camera",
        RequestKind.Microphone => "the microphone",
        RequestKind.CameraAndMicrophone => "the camera and the microphone",
        RequestKind.ScreenCapture => "screen capture",
        RequestKind.ClipboardRead => "reading the clipboard",
        RequestKind.DeviceList => "the list of cameras and microphones",
        RequestKind.PointerLock => "pointer lock",
        RequestKind.MediaKeys => "media keys",
        RequestKind.StorageAccess => "storage access",
        RequestKind.Xr => "XR",
        _ => type.Length == 0 ? "an unknown feature" : type,
    };

    private static string AddressOf(nint request) => Marshal.PtrToStringUTF8(WebKit.webkit_uri_request_get_uri(request)) ?? "";

    /// <summary>
    /// Opens an allowed address with the desktop's handler for its scheme, on a thread of its own: GIO may spawn the
    /// handler or call the OpenURI portal over D-Bus, which never runs on the main loop. Failures are logged.
    /// </summary>
    private static void OpenExternal(string address, HostLog log)
    {
        var thread = new Thread(() => Launch(address, log)) { IsBackground = true, Name = "Spar external link" };
        thread.Start();
    }

    private static unsafe void Launch(string address, HostLog log)
    {
        try
        {
            nint error = 0;
            if (GLib.g_app_info_launch_default_for_uri(address, 0, &error) == 0)
            {
                log.Warn($"The desktop could not open the address: {GLib.ErrorMessage(error)}");
                GLib.g_error_free(error);
            }
        }
        catch (Exception exception)
        {
            log.Error("Opening an address outside the app failed", exception);
        }
    }

    private static string Shorten(string text) => text.Length <= 200 ? text : string.Concat(text.AsSpan(0, 200), "…");

    private bool Navigation(nint decision)
    {
        var action = WebKit.webkit_navigation_policy_decision_get_navigation_action(decision);
        var address = AddressOf(WebKit.webkit_navigation_action_get_request(action));

        // History navigation (plan section 3c): the mouse's side buttons and history.back() to another document, which
        // WebKit decides here; moves within one document (a router's pushState entries) are not decided here and stay.
        // The host's own reload of the app's page is one too, and goes ahead.
        var hostReload = _hostReload && SchemeServer.IsAppOrigin(address);
        _hostReload &= !hostReload;
        if (!_allowHistory && !hostReload && WebKit.webkit_navigation_action_get_navigation_type(action) == BackForward)
        {
            WebKit.webkit_policy_decision_ignore(decision);
            Refused("Back or forward navigation cancelled (AllowHistoryNavigation allows it).");
            return true;
        }

        if (Allows(address, _security.FrameSrc))
        {
            LastLetThroughWasDeclaredFrame = !SchemeServer.IsAppOrigin(address);
            _lastDeclaredFrame = LastLetThroughWasDeclaredFrame ? address : "";
            return false;
        }

        WebKit.webkit_policy_decision_ignore(decision);
        var redirect = WebKit.webkit_navigation_action_is_redirect(action) != 0 ? " (a redirect)" : "";
        Refused($"Navigation lock: a navigation to {Shorten(address)}{redirect} was ignored; the window shows only the app's own page.");
        return true;
    }

    /// <summary>A new window, from <c>window.open</c>, <c>target="_blank"</c> or a modified click: never opened.</summary>
    private void NewWindow(nint decision)
    {
        var action = WebKit.webkit_navigation_policy_decision_get_navigation_action(decision);
        var address = AddressOf(WebKit.webkit_navigation_action_get_request(action));
        var byUser = WebKit.webkit_navigation_action_is_user_gesture(action) != 0
            && _lastInput != 0
            && System.Diagnostics.Stopwatch.GetElapsedTime(_lastInput).TotalMilliseconds <= InputWindowMilliseconds;
        WebKit.webkit_policy_decision_ignore(decision);
        if (!byUser)
        {
            Refused($"Navigation lock: a new window for {Shorten(address)} was blocked; only a user's click opens an external address.");
            return;
        }

        if (NavigationRules.ExternalRefusal(address, SchemeServer.Origin) is { } refusal)
        {
            Refused($"Navigation lock: a new window for {Shorten(address)} was blocked ({refusal}).");
            return;
        }

        _log.Info($"Opening {Shorten(address)} with the desktop's handler (a link the user followed).");
        OpenExternal(address, _log);
    }

    /// <summary>
    /// The main frame's document from an address the lock let through for frames: ignored, so the page stays. Every
    /// other response is WebKit's to decide.
    /// </summary>
    private bool Response(nint decision)
    {
        if (WebKit.webkit_response_policy_decision_is_main_frame_main_resource(decision) == 0)
        {
            return false;
        }

        var address = AddressOf(WebKit.webkit_response_policy_decision_get_request(decision));
        if (SchemeServer.IsAppOrigin(address))
        {
            return false;
        }

        WebKit.webkit_policy_decision_ignore(decision);
        Refused($"Navigation lock: the window's document from {Shorten(address)} was ignored; the window shows only the app's own page.");
        return true;
    }

    private void Refused(string line)
    {
        _refused++;
        if (_refused <= LoggedRefusals)
        {
            _log.Warn(line);
        }
        else if (_refused == LoggedRefusals + 1)
        {
            _log.Warn($"Navigation lock: more than {LoggedRefusals} refused navigations and windows in this run; further ones are not logged.");
        }
    }
}
