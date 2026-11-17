using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Spar.Windows.Interop;

// Event arguments and the request and response objects, hand-declared from WebView2.h (see WebView2.Environment.cs
// for the rules). Every pointer a callback receives is borrowed; Com.Borrow wraps it with a reference of its own.

/// <summary><c>ICoreWebView2WebResourceRequestedEventArgs2</c>, flat: the base 3–7, _2 8.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
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

/// <summary><c>ICoreWebView2WebResourceRequest</c> (slots 3–9; the host reads the first three).</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("97055cd4-512c-4264-8b5f-e3f446cea6a5")]
internal partial interface ICoreWebView2WebResourceRequest
{
    string GetUri();
    [PreserveSig] int Slot004_PutUri();
    string GetMethod();
}

/// <summary><c>ICoreWebView2Deferral</c>: completes a deferred event.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("c10e7f7b-b585-46f0-a623-8befbf3e4ee0")]
internal partial interface ICoreWebView2Deferral
{
    void Complete();
}

/// <summary><c>ICoreWebView2WebMessageReceivedEventArgs</c> (slots 3–5).</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("0f99a40c-e962-4207-9e92-e3d542eff849")]
internal partial interface ICoreWebView2WebMessageReceivedEventArgs
{
    string GetSource();
    [PreserveSig] int Slot004_GetWebMessageAsJson();

    /// <summary>Fails with <c>E_INVALIDARG</c> for a message that is not a string, or a string with a lone surrogate.</summary>
    [PreserveSig]
    int TryGetWebMessageAsString(out string? webMessageAsString);
}

/// <summary><c>ICoreWebView2NavigationStartingEventArgs</c> (slots 3–9).</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("5b495469-e119-438a-9b18-7604f25f2e49")]
internal partial interface ICoreWebView2NavigationStartingEventArgs
{
    string GetUri();
    [PreserveSig] int Slot004_GetIsUserInitiated();
    [PreserveSig] int Slot005_GetIsRedirected();
    [PreserveSig] int Slot006_GetRequestHeaders();
    [PreserveSig] int Slot007_GetCancel();
    [PreserveSig] int Slot008_PutCancel();
    ulong GetNavigationId();
}

/// <summary><c>ICoreWebView2ContentLoadingEventArgs</c> (slots 3–4).</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("0c8a1275-9b6b-4901-87ad-70df25bafa6e")]
internal partial interface ICoreWebView2ContentLoadingEventArgs
{
    int GetIsErrorPage();
    ulong GetNavigationId();
}

/// <summary><c>ICoreWebView2NavigationCompletedEventArgs</c> (slots 3–5).</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("30d68b7d-20d9-4752-a9ca-ec8448fbb5c1")]
internal partial interface ICoreWebView2NavigationCompletedEventArgs
{
    int GetIsSuccess();
    int GetWebErrorStatus();
    ulong GetNavigationId();
}

/// <summary><c>ICoreWebView2ProcessFailedEventArgs</c> (slot 3).</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("8155a9a4-1474-4a86-8cae-151b0fa6b8ca")]
internal partial interface ICoreWebView2ProcessFailedEventArgs
{
    int GetProcessFailedKind();
}

/// <summary><c>ICoreWebView2BrowserProcessExitedEventArgs</c> (slots 3–4).</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("1f00663f-af8c-4782-9cdd-dd01c52e34cb")]
internal partial interface ICoreWebView2BrowserProcessExitedEventArgs
{
    int GetBrowserProcessExitKind();
    uint GetBrowserProcessId();
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

    /// <summary><c>COREWEBVIEW2_PREFERRED_COLOR_SCHEME_AUTO</c>: the page follows the system's theme.</summary>
    public const int ColorSchemeAuto = 0;

    /// <summary><c>COREWEBVIEW2_PREFERRED_COLOR_SCHEME_LIGHT</c>.</summary>
    public const int ColorSchemeLight = 1;

    /// <summary><c>COREWEBVIEW2_PREFERRED_COLOR_SCHEME_DARK</c>.</summary>
    public const int ColorSchemeDark = 2;
}
