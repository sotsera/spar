using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Spar.Windows.Interop;

// The browser's settings and the events of plan section 3c's Production defaults, hand-declared from WebView2.h (see
// WebView2.Environment.cs for the rules): the WebView's settings (ICoreWebView2Settings8), the environment options Spar
// passes at creation (the one interface the host implements for WebView2 to call), and the arguments of
// DownloadStarting, ContextMenuRequested and AcceleratorKeyPressed. Every one is in
// Runtime 139, Spar's minimum (decision "M1b's remaining questions without a spike"): the newest, Settings8's
// IsReputationCheckingRequired and AcceleratorKeyPressedEventArgs2, came with Runtime 113 and 116.

/// <summary><c>ICoreWebView2Settings8</c>, flat: Settings 3–20, _2 21–22, _3 23–24, _4 25–28, _5 29–30, _6 31–32, _7 33–34, _8 35–36.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("9e6b0e8f-86ad-4e81-8147-a9b5edb68650")]
internal partial interface ICoreWebView2Settings8
{
    // ICoreWebView2Settings
    [PreserveSig] int Slot003_GetIsScriptEnabled();
    [PreserveSig] int Slot004_PutIsScriptEnabled();
    [PreserveSig] int Slot005_GetIsWebMessageEnabled();
    [PreserveSig] int Slot006_PutIsWebMessageEnabled();
    [PreserveSig] int Slot007_GetAreDefaultScriptDialogsEnabled();
    [PreserveSig] int Slot008_PutAreDefaultScriptDialogsEnabled();
    [PreserveSig] int Slot009_GetIsStatusBarEnabled();
    [PreserveSig] int Slot010_PutIsStatusBarEnabled();
    int GetAreDevToolsEnabled();
    void PutAreDevToolsEnabled(int value);
    int GetAreDefaultContextMenusEnabled();
    void PutAreDefaultContextMenusEnabled(int value);
    int GetAreHostObjectsAllowed();
    void PutAreHostObjectsAllowed(int value);
    int GetIsZoomControlEnabled();
    void PutIsZoomControlEnabled(int value);
    [PreserveSig] int Slot019_GetIsBuiltInErrorPageEnabled();
    [PreserveSig] int Slot020_PutIsBuiltInErrorPageEnabled();
    // ICoreWebView2Settings2
    [PreserveSig] int Slot021_GetUserAgent();
    [PreserveSig] int Slot022_PutUserAgent();
    // ICoreWebView2Settings3
    int GetAreBrowserAcceleratorKeysEnabled();
    void PutAreBrowserAcceleratorKeysEnabled(int value);
    // ICoreWebView2Settings4
    int GetIsPasswordAutosaveEnabled();
    void PutIsPasswordAutosaveEnabled(int value);
    int GetIsGeneralAutofillEnabled();
    void PutIsGeneralAutofillEnabled(int value);
    // ICoreWebView2Settings5
    [PreserveSig] int Slot029_GetIsPinchZoomEnabled();
    [PreserveSig] int Slot030_PutIsPinchZoomEnabled();
    // ICoreWebView2Settings6
    int GetIsSwipeNavigationEnabled();
    void PutIsSwipeNavigationEnabled(int value);
    // ICoreWebView2Settings7
    [PreserveSig] int Slot033_GetHiddenPdfToolbarItems();
    [PreserveSig] int Slot034_PutHiddenPdfToolbarItems();
    // ICoreWebView2Settings8
    int GetIsReputationCheckingRequired();
    void PutIsReputationCheckingRequired(int value);
}

/// <summary>
/// <c>ICoreWebView2Settings9</c>, flat: <see cref="ICoreWebView2Settings8"/>'s slots 3–36, then 37–38, whether CSS
/// <c>app-region</c> marks drag areas (Runtime 123, before Spar's minimum; set for a page-drawn title bar only).
/// </summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("0528a73b-e92d-49f4-927a-e547dddaa37d")]
internal partial interface ICoreWebView2Settings9
{
    [PreserveSig] int Slot003_GetIsScriptEnabled();
    [PreserveSig] int Slot004_PutIsScriptEnabled();
    [PreserveSig] int Slot005_GetIsWebMessageEnabled();
    [PreserveSig] int Slot006_PutIsWebMessageEnabled();
    [PreserveSig] int Slot007_GetAreDefaultScriptDialogsEnabled();
    [PreserveSig] int Slot008_PutAreDefaultScriptDialogsEnabled();
    [PreserveSig] int Slot009_GetIsStatusBarEnabled();
    [PreserveSig] int Slot010_PutIsStatusBarEnabled();
    [PreserveSig] int Slot011_GetAreDevToolsEnabled();
    [PreserveSig] int Slot012_PutAreDevToolsEnabled();
    [PreserveSig] int Slot013_GetAreDefaultContextMenusEnabled();
    [PreserveSig] int Slot014_PutAreDefaultContextMenusEnabled();
    [PreserveSig] int Slot015_GetAreHostObjectsAllowed();
    [PreserveSig] int Slot016_PutAreHostObjectsAllowed();
    [PreserveSig] int Slot017_GetIsZoomControlEnabled();
    [PreserveSig] int Slot018_PutIsZoomControlEnabled();
    [PreserveSig] int Slot019_GetIsBuiltInErrorPageEnabled();
    [PreserveSig] int Slot020_PutIsBuiltInErrorPageEnabled();
    [PreserveSig] int Slot021_GetUserAgent();
    [PreserveSig] int Slot022_PutUserAgent();
    [PreserveSig] int Slot023_GetAreBrowserAcceleratorKeysEnabled();
    [PreserveSig] int Slot024_PutAreBrowserAcceleratorKeysEnabled();
    [PreserveSig] int Slot025_GetIsPasswordAutosaveEnabled();
    [PreserveSig] int Slot026_PutIsPasswordAutosaveEnabled();
    [PreserveSig] int Slot027_GetIsGeneralAutofillEnabled();
    [PreserveSig] int Slot028_PutIsGeneralAutofillEnabled();
    [PreserveSig] int Slot029_GetIsPinchZoomEnabled();
    [PreserveSig] int Slot030_PutIsPinchZoomEnabled();
    [PreserveSig] int Slot031_GetIsSwipeNavigationEnabled();
    [PreserveSig] int Slot032_PutIsSwipeNavigationEnabled();
    [PreserveSig] int Slot033_GetHiddenPdfToolbarItems();
    [PreserveSig] int Slot034_PutHiddenPdfToolbarItems();
    [PreserveSig] int Slot035_GetIsReputationCheckingRequired();
    [PreserveSig] int Slot036_PutIsReputationCheckingRequired();
    int GetIsNonClientRegionSupportEnabled();
    void PutIsNonClientRegionSupportEnabled(int value);
}

/// <summary>
/// <c>ICoreWebView2EnvironmentOptions</c> (slots 3–10), which the host implements: WebView2 reads it during
/// <c>CreateCoreWebView2EnvironmentWithOptions</c>. A string it returns is a <c>CoTaskMemAlloc</c> copy WebView2 frees.
/// </summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("2fde08a8-1e9a-4766-8c05-95a9ceb9d1c5")]
internal partial interface ICoreWebView2EnvironmentOptions
{
    string? GetAdditionalBrowserArguments();
    void PutAdditionalBrowserArguments(string? value);
    string? GetLanguage();
    void PutLanguage(string? value);
    string? GetTargetCompatibleBrowserVersion();
    void PutTargetCompatibleBrowserVersion(string? value);
    int GetAllowSingleSignOnUsingOSPrimaryAccount();
    void PutAllowSingleSignOnUsingOSPrimaryAccount(int allow);
}

/// <summary><c>ICoreWebView2DownloadStartingEventArgs</c> (slots 3–10).</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("e99bbe21-43e9-4544-a732-282764eafa60")]
internal partial interface ICoreWebView2DownloadStartingEventArgs
{
    nint GetDownloadOperation();
    [PreserveSig] int Slot004_GetCancel();
    void PutCancel(int cancel);
    [PreserveSig] int Slot006_GetResultFilePath();
    [PreserveSig] int Slot007_PutResultFilePath();
    [PreserveSig] int Slot008_GetHandled();
    void PutHandled(int handled);
}

/// <summary><c>ICoreWebView2DownloadOperation</c> (slots 3–9; the host reads only the address, for its log).</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("3d6b6cf2-afe1-44c7-a995-c65117714336")]
internal partial interface ICoreWebView2DownloadOperation
{
    [PreserveSig] int Slot003_AddBytesReceivedChanged();
    [PreserveSig] int Slot004_RemoveBytesReceivedChanged();
    [PreserveSig] int Slot005_AddEstimatedEndTimeChanged();
    [PreserveSig] int Slot006_RemoveEstimatedEndTimeChanged();
    [PreserveSig] int Slot007_AddStateChanged();
    [PreserveSig] int Slot008_RemoveStateChanged();
    string GetUri();
}

/// <summary><c>ICoreWebView2ContextMenuRequestedEventArgs</c> (slots 3–9).</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("a1d309ee-c03f-11eb-8529-0242ac130003")]
internal partial interface ICoreWebView2ContextMenuRequestedEventArgs
{
    nint GetMenuItems();
    nint GetContextMenuTarget();
    [PreserveSig] int Slot005_GetLocation();
    [PreserveSig] int Slot006_PutSelectedCommandId();
    [PreserveSig] int Slot007_GetSelectedCommandId();
    void PutHandled(int handled);
}

/// <summary><c>ICoreWebView2ContextMenuTarget</c> (slots 3–4: what was clicked, and whether it is editable).</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("b8611d99-eed6-4f3f-902c-a198502ad472")]
internal partial interface ICoreWebView2ContextMenuTarget
{
    int GetKind();
    int GetIsEditable();
}

/// <summary><c>ICoreWebView2ContextMenuItemCollection</c> (slots 3–5).</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("f562a2f5-c415-45cf-b909-d4b7c1e276d3")]
internal partial interface ICoreWebView2ContextMenuItemCollection
{
    uint GetCount();
    nint GetValueAtIndex(uint index);
    void RemoveValueAtIndex(uint index);
}

/// <summary><c>ICoreWebView2ContextMenuItem</c> (slot 3: the item's name, such as <c>copy</c> or <c>inspectElement</c>).</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("7aed49e3-a93f-497a-811c-749c6b6b6c65")]
internal partial interface ICoreWebView2ContextMenuItem
{
    string GetName();
}

/// <summary><c>ICoreWebView2AcceleratorKeyPressedEventArgs2</c>, flat: the base 3–8, _2 9–10 (per key, whether the browser acts on it).</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("03b2c8c8-7799-4e34-bd66-ed26aa85f2bf")]
internal partial interface ICoreWebView2AcceleratorKeyPressedEventArgs2
{
    // ICoreWebView2AcceleratorKeyPressedEventArgs
    int GetKeyEventKind();
    uint GetVirtualKey();
    [PreserveSig] int Slot005_GetKeyEventLParam();
    [PreserveSig] int Slot006_GetPhysicalKeyStatus();
    [PreserveSig] int Slot007_GetHandled();
    [PreserveSig] int Slot008_PutHandled();
    // ICoreWebView2AcceleratorKeyPressedEventArgs2
    [PreserveSig] int Slot009_GetIsBrowserAcceleratorKeyEnabled();
    void PutIsBrowserAcceleratorKeyEnabled(int value);
}
