using Spar.Hosting;
using Spar.Runtime;
using Spar.Windows.Interop;

namespace Spar.Windows;

/// <summary>
/// The navigation lock and the permission gate of plan section 3b, on the WebView's events, all on the UI thread and
/// each answered from what is already in memory:
/// <list type="bullet">
/// <item>a top-level or frame navigation outside <c>https://app.localhost</c> is cancelled (frames may also load
/// <c>about:blank</c> and <c>about:srcdoc</c>, which stay on the app's origin and policy), and logged;</item>
/// <item>every new-window request is handled, so the WebView never opens a window: a user's click or <c>window.open</c>
/// to an allowed external address opens it in the system's browser (<see cref="ExternalLinks"/>), anything else is
/// blocked and logged;</item>
/// <item>a permission request is granted only for a feature the app declared and denied otherwise, with no prompt, and
/// the browser never remembers the answer (<c>SavesInProfile = false</c>).</item>
/// </list>
/// The foreign document filter of <see cref="PageServer"/> answers what a cancelled navigation already sent.
/// </summary>
internal sealed class PageGuard
{
    private readonly HostLog _log;
    private readonly SparPageSecurity _security;
    /// <summary>The permission kinds already logged, as bits by kind (WebView2 has fewer than 32).</summary>
    private long _loggedKinds;
    private bool _noSavesInProfileLogged;

    public PageGuard(HostLog log, SparPageSecurity security)
    {
        _log = log;
        _security = security;
    }

    /// <summary>
    /// A top-level navigation: false, with the navigation cancelled, when it leaves the app's origin. A navigation that
    /// stays starts a new document epoch, which the bridge handles.
    /// </summary>
    public bool OnNavigationStarting(nint argsPointer)
    {
        var args = Com.Borrow<ICoreWebView2NavigationStartingEventArgs>(argsPointer);
        try
        {
            var uri = args.GetUri();
            if (PageServer.IsAppOrigin(uri))
            {
                return true;
            }

            args.PutCancel(1);
            _log.Warn($"Navigation lock: a navigation to {Shorten(uri)} was cancelled; the window shows only the app's own page.");
            return false;
        }
        finally
        {
            Com.Release(args);
        }
    }

    /// <summary>A frame's navigation: cancelled when it leaves the app's origin.</summary>
    public void OnFrameNavigationStarting(nint argsPointer)
    {
        var args = Com.Borrow<ICoreWebView2NavigationStartingEventArgs>(argsPointer);
        try
        {
            var uri = args.GetUri();
            if (PageServer.IsAppOrigin(uri) || uri is "about:blank" or "about:srcdoc")
            {
                return;
            }

            args.PutCancel(1);
            _log.Warn($"Navigation lock: a frame's navigation to {Shorten(uri)} was cancelled.");
        }
        finally
        {
            Com.Release(args);
        }
    }

    /// <summary>
    /// A new window, from <c>window.open</c>, <c>target="_blank"</c> or a modified click: always handled, so none opens.
    /// An allowed external address the user asked for goes to the system's browser.
    /// </summary>
    public void OnNewWindowRequested(nint argsPointer)
    {
        var args = Com.Borrow<ICoreWebView2NewWindowRequestedEventArgs>(argsPointer);
        string uri;
        bool byUser;
        try
        {
            args.PutHandled(1);
            uri = args.GetUri();
            byUser = args.GetIsUserInitiated() != 0;
        }
        finally
        {
            Com.Release(args);
        }

        if (!byUser)
        {
            _log.Warn($"Navigation lock: a new window for {Shorten(uri)} was blocked; only a user's click opens an external address.");
            return;
        }

        if (ExternalLinks.Refusal(uri) is { } refusal)
        {
            _log.Warn($"Navigation lock: a new window for {Shorten(uri)} was blocked ({refusal}).");
            return;
        }

        _log.Info($"Opening {Shorten(uri)} in the system's browser (a link the user followed).");
        ExternalLinks.Open(uri, _log);
    }

    /// <summary>A permission request: granted for a declared feature, denied otherwise; never remembered by the browser.</summary>
    public void OnPermissionRequested(nint argsPointer)
    {
        var kind = 0;
        var state = WebView2Values.PermissionDeny;
        string uri;
        if (TryBorrow<ICoreWebView2PermissionRequestedEventArgs3>(argsPointer, out var current))
        {
            try
            {
                current.PutSavesInProfile(0);
                current.PutHandled(1);
                uri = current.GetUri();
                kind = current.GetPermissionKind();
                state = Decide(kind, uri);
                current.PutState(state);
            }
            finally
            {
                Com.Release(current);
            }
        }
        else
        {
            if (!_noSavesInProfileLogged)
            {
                _noSavesInProfileLogged = true;
                _log.Warn("This WebView2 Runtime has no ICoreWebView2PermissionRequestedEventArgs3: answers may be remembered.");
            }

            var older = Com.Borrow<ICoreWebView2PermissionRequestedEventArgs>(argsPointer);
            try
            {
                uri = older.GetUri();
                kind = older.GetPermissionKind();
                state = Decide(kind, uri);
                older.PutState(state);
            }
            finally
            {
                Com.Release(older);
            }
        }

        // Once per kind: a page asking in a loop must not flood the log.
        var bit = 1L << Math.Clamp(kind, 0, 63);
        if ((_loggedKinds & bit) == 0)
        {
            _loggedKinds |= bit;
            var answer = state == WebView2Values.PermissionAllow ? "granted (declared)" : "denied (not declared in spar.Permissions)";
            _log.Info($"Permission request for {KindName(kind)} from {Shorten(uri)}: {answer}; later requests of this kind are not logged.");
        }
    }

    /// <summary>The permission a WebView2 permission kind belongs to; null for kinds no declaration covers.</summary>
    internal static Permission? PermissionOf(int kind) => kind switch
    {
        1 => Permission.Microphone,
        2 => Permission.Camera,
        3 => Permission.Geolocation,
        5 => Permission.Sensors,
        6 => Permission.ClipboardRead,
        9 => Permission.Autoplay,
        10 => Permission.LocalFonts,
        11 => Permission.Midi,
        12 => Permission.WindowManagement,
        _ => null,
    };

    /// <summary>Allowed only for a declared feature, and only from the app's own origin.</summary>
    private int Decide(int kind, string uri) =>
        PageServer.IsAppOrigin(uri) && PermissionOf(kind) is { } permission && PageSecurityNames.Contains(_security.Permissions, permission)
            ? WebView2Values.PermissionAllow
            : WebView2Values.PermissionDeny;

    private static string KindName(int kind) => kind switch
    {
        4 => "notifications",
        7 => "multiple downloads",
        8 => "file access",
        13 => "persistent storage",
        _ => PermissionOf(kind) is { } permission ? PageSecurityNames.Of(permission) : $"kind {kind}",
    };

    private static bool TryBorrow<T>(nint pointer, out T wrapper)
        where T : class
    {
        try
        {
            wrapper = Com.Borrow<T>(pointer);
            return true;
        }
        catch (InvalidCastException)
        {
            wrapper = null!;
            return false;
        }
    }

    private static string Shorten(string text) => text.Length <= 200 ? text : string.Concat(text.AsSpan(0, 200), "…");
}
