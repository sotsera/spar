using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Spar.Windows.Interop;

// Event arguments and the request and response objects, hand-declared from WebView2.h (see WebView2.Environment.cs
// for the rules). Every pointer a callback receives is borrowed; Com.Borrow wraps it with a reference of its own.

/// <summary><c>ICoreWebView2WebResourceRequestedEventArgs2</c>, flat: the base 3–7, _2 8.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("9c562c24-b219-4d7f-92f6-b187fbbadd56")]
internal partial interface ICoreWebView2WebResourceRequestedEventArgs2
{
    // ICoreWebView2WebResourceRequestedEventArgs
    nint GetRequest();
    [PreserveSig] int Slot004_GetResponse();
    void PutResponse(nint response);
    nint GetDeferral();
    int GetResourceContext();
    // ICoreWebView2WebResourceRequestedEventArgs2
    int GetRequestedSourceKind();
}

/// <summary>
/// <c>ICoreWebView2WebResourceRequest</c> (slots 3–9; the host reads the address, the method and the body, and a
/// Development build's proxy to the Vite development server the headers it forwards).
/// </summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("97055cd4-512c-4264-8b5f-e3f446cea6a5")]
internal partial interface ICoreWebView2WebResourceRequest
{
    string GetUri();
    [PreserveSig] int Slot004_PutUri();
    string GetMethod();
    [PreserveSig] int Slot006_PutMethod();

    /// <summary>The request body as an <c>IStream</c> (one reference the caller owns), or 0 when it has none.</summary>
    nint GetContent();

    [PreserveSig] int Slot008_PutContent();

    /// <summary>The request's headers, an <see cref="ICoreWebView2HttpRequestHeaders"/> the caller owns.</summary>
    nint GetHeaders();
}

/// <summary><c>ICoreWebView2HttpRequestHeaders</c> (slots 3–5): one request's headers, read by name.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("e86cac0e-5523-465c-b536-8fb9fc8c8c60")]
internal partial interface ICoreWebView2HttpRequestHeaders
{
    string GetHeader(string name);
    [PreserveSig] int Slot004_GetHeaders();
    int Contains(string name);
}

/// <summary><c>ICoreWebView2Deferral</c>: completes a deferred event.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("c10e7f7b-b585-46f0-a623-8befbf3e4ee0")]
internal partial interface ICoreWebView2Deferral
{
    void Complete();
}

/// <summary>
/// <c>ICoreWebView2DevToolsProtocolEventReceiver</c> (slots 3–4): one DevTools Protocol event of the WebView. Only the
/// Development build's console (<c>DevelopmentConsole</c>) uses it, so a Production image carries no call to it.
/// </summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("b32ca51a-8371-45e9-9317-af021d080367")]
internal partial interface ICoreWebView2DevToolsProtocolEventReceiver
{
    EventRegistrationToken AddDevToolsProtocolEventReceived(ICoreWebView2DevToolsProtocolEventReceivedEventHandler handler);
    void RemoveDevToolsProtocolEventReceived(EventRegistrationToken token);
}

/// <summary><c>ICoreWebView2DevToolsProtocolEventReceivedEventArgs</c> (slot 3): the event's parameters as JSON.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("653c2959-bb3a-4377-8632-b58ada4e66c4")]
internal partial interface ICoreWebView2DevToolsProtocolEventReceivedEventArgs
{
    string GetParameterObjectAsJson();
}

/// <summary><c>ICoreWebView2WebMessageReceivedEventArgs</c> (slots 3–5).</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("0f99a40c-e962-4207-9e92-e3d542eff849")]
internal partial interface ICoreWebView2WebMessageReceivedEventArgs
{
    string GetSource();
    [PreserveSig] int Slot004_GetWebMessageAsJson();

    /// <summary>Fails with <c>E_INVALIDARG</c> for a message that is not a string, or a string with a lone surrogate.</summary>
    [PreserveSig]
    int TryGetWebMessageAsString(out string? webMessageAsString);
}

/// <summary>
/// <c>ICoreWebView2WebMessageReceivedEventArgs2</c>, flat: the base 3–5, _2 6, the objects a page posted with
/// <c>chrome.webview.postMessageWithAdditionalObjects</c> (a drop's <c>File</c>s). Feature-detected: queried from the
/// args, and a message without it carries no files.
/// </summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("06fc7ab7-c90c-4297-9389-33ca01cf6d5e")]
internal partial interface ICoreWebView2WebMessageReceivedEventArgs2
{
    [PreserveSig] int Slot003_GetSource();
    [PreserveSig] int Slot004_GetWebMessageAsJson();
    [PreserveSig] int Slot005_TryGetWebMessageAsString();

    /// <summary>The objects, as an <c>ICoreWebView2ObjectCollectionView</c> the caller owns; 0 for none.</summary>
    [PreserveSig]
    int GetAdditionalObjects(out nint value);
}

/// <summary><c>ICoreWebView2ObjectCollectionView</c> (slots 3–4).</summary>
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("0f36fd87-4f69-4415-98da-888f89fb9a33")]
internal partial interface ICoreWebView2ObjectCollectionView
{
    [PreserveSig]
    int GetCount(out uint value);

    /// <summary>The object at <paramref name="index"/>, an <c>IUnknown</c> the caller owns.</summary>
    [PreserveSig]
    int GetValueAtIndex(uint index, out nint value);
}

/// <summary><c>ICoreWebView2File</c> (slot 3): a file the user gave the page, which only the browser can make.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("f2c19559-6bc1-4583-a757-90021be9afec")]
internal partial interface ICoreWebView2File
{
    string GetPath();
}

/// <summary>
/// <c>ICoreWebView2NavigationStartingEventArgs3</c>, flat: the base 3–9, _2 10–11, _3 12 (the navigation's kind, Runtime
/// 115). Declared once at _3, which every Runtime Spar supports has, for top-level and frame navigations alike.
/// </summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("ddffe494-4942-4bd2-ab73-35b8ff40e19f")]
internal partial interface ICoreWebView2NavigationStartingEventArgs3
{
    // ICoreWebView2NavigationStartingEventArgs
    string GetUri();
    int GetIsUserInitiated();
    [PreserveSig] int Slot005_GetIsRedirected();
    [PreserveSig] int Slot006_GetRequestHeaders();
    [PreserveSig] int Slot007_GetCancel();
    void PutCancel(int cancel);
    ulong GetNavigationId();
    // ICoreWebView2NavigationStartingEventArgs2
    [PreserveSig] int Slot010_GetAdditionalAllowedFrameAncestors();
    [PreserveSig] int Slot011_PutAdditionalAllowedFrameAncestors();
    // ICoreWebView2NavigationStartingEventArgs3
    int GetNavigationKind();
}

/// <summary><c>ICoreWebView2ContentLoadingEventArgs</c> (slots 3–4): an error page gets no heartbeat.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("0c8a1275-9b6b-4901-87ad-70df25bafa6e")]
internal partial interface ICoreWebView2ContentLoadingEventArgs
{
    int GetIsErrorPage();
    ulong GetNavigationId();
}

/// <summary><c>ICoreWebView2NavigationCompletedEventArgs</c> (slots 3–5).</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("30d68b7d-20d9-4752-a9ca-ec8448fbb5c1")]
internal partial interface ICoreWebView2NavigationCompletedEventArgs
{
    int GetIsSuccess();
    int GetWebErrorStatus();
    ulong GetNavigationId();
}

/// <summary><c>ICoreWebView2ProcessFailedEventArgs</c> (slot 3).</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("8155a9a4-1474-4a86-8cae-151b0fa6b8ca")]
internal partial interface ICoreWebView2ProcessFailedEventArgs
{
    int GetProcessFailedKind();
}

/// <summary><c>ICoreWebView2BrowserProcessExitedEventArgs</c> (slots 3–4).</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("1f00663f-af8c-4782-9cdd-dd01c52e34cb")]
internal partial interface ICoreWebView2BrowserProcessExitedEventArgs
{
    int GetBrowserProcessExitKind();
    uint GetBrowserProcessId();
}

/// <summary><c>ICoreWebView2MoveFocusRequestedEventArgs</c> (slots 3–5): Tab or Shift+Tab is leaving the page.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("2d6aa13b-3839-4a15-92fc-d88b3c0d9c9d")]
internal partial interface ICoreWebView2MoveFocusRequestedEventArgs
{
    int GetReason();
    [PreserveSig] int Slot004_GetHandled();
    void PutHandled(int value);
}

/// <summary>The values of the WebView2 enums the host reads or passes, as WebView2.h defines them.</summary>
internal static class WebView2Values
{
    /// <summary><c>COREWEBVIEW2_WEB_RESOURCE_CONTEXT_ALL</c>.</summary>
    public const int ContextAll = 0;

    /// <summary><c>COREWEBVIEW2_WEB_RESOURCE_CONTEXT_DOCUMENT</c>.</summary>
    public const int ContextDocument = 1;

    /// <summary><c>COREWEBVIEW2_WEB_RESOURCE_REQUEST_SOURCE_KINDS_ALL</c>: documents, shared and service workers.</summary>
    public const int SourceKindsAll = unchecked((int)0xFFFFFFFF);

    /// <summary><c>COREWEBVIEW2_PROCESS_FAILED_KIND_BROWSER_PROCESS_EXITED</c>.</summary>
    public const int BrowserProcessExited = 0;

    /// <summary><c>COREWEBVIEW2_PROCESS_FAILED_KIND_RENDER_PROCESS_EXITED</c>.</summary>
    public const int RenderProcessExited = 1;

    /// <summary><c>COREWEBVIEW2_PROCESS_FAILED_KIND_RENDER_PROCESS_UNRESPONSIVE</c>.</summary>
    public const int RenderProcessUnresponsive = 2;

    /// <summary><c>COREWEBVIEW2_PROCESS_KIND_RENDERER</c>, in <c>ICoreWebView2ProcessInfo</c>.</summary>
    public const int RendererProcess = 1;

    /// <summary><c>COREWEBVIEW2_BROWSER_PROCESS_EXIT_KIND_FAILED</c>; 0 is a normal exit.</summary>
    public const int BrowserExitFailed = 1;

    /// <summary><c>COREWEBVIEW2_PERMISSION_STATE_ALLOW</c>.</summary>
    public const int PermissionAllow = 1;

    /// <summary><c>COREWEBVIEW2_PERMISSION_STATE_DENY</c>.</summary>
    public const int PermissionDeny = 2;

    /// <summary><c>COREWEBVIEW2_PREFERRED_COLOR_SCHEME_AUTO</c>: the page follows the system's theme.</summary>
    public const int ColorSchemeAuto = 0;

    /// <summary><c>COREWEBVIEW2_PREFERRED_COLOR_SCHEME_LIGHT</c>.</summary>
    public const int ColorSchemeLight = 1;

    /// <summary><c>COREWEBVIEW2_PREFERRED_COLOR_SCHEME_DARK</c>.</summary>
    public const int ColorSchemeDark = 2;

    /// <summary><c>COREWEBVIEW2_NAVIGATION_KIND_BACK_OR_FORWARD</c>, in <c>ICoreWebView2NavigationStartingEventArgs3</c>.</summary>
    public const int NavigationBackOrForward = 1;

    /// <summary><c>COREWEBVIEW2_MOVE_FOCUS_REASON_PROGRAMMATIC</c>: focus to the page's last focused element.</summary>
    public const int FocusProgrammatic = 0;
}
