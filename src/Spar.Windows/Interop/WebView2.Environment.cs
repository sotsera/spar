using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Spar.Windows.Interop;

// The WebView2 environment and controller, hand-declared from WebView2.h (Microsoft.Web.WebView2 1.0.4314.35) as flat
// interfaces: every slot of every base interface in header order, IUnknown's three first. Slots the host does not call
// are [PreserveSig] placeholders named SlotNNN_<header name>, which keep the offsets of the ones it does. Spar.Tests'
// slot-order test checks every declared method against the header. A method that hands out an interface returns the
// raw pointer, with one reference the caller owns (Com.Take).

/// <summary>The C <c>RECT</c>: left, top, right, bottom.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct Rect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

/// <summary>What an <c>add_</c> method returns, for its <c>remove_</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct EventRegistrationToken
{
    public long Value;
}

/// <summary><c>ICoreWebView2Environment10</c>, flat: Environment 3–7, _2 8, _3 9–10, _4 11, _5 12–13, _6 14, _7 15, _8 16–18, _9 19, _10 20–22.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("ee0eb9df-6f12-46ce-b53f-3f47b9c928e0")]
internal partial interface ICoreWebView2Environment10
{
    // ICoreWebView2Environment
    void CreateCoreWebView2Controller(nint parentWindow, ICoreWebView2CreateCoreWebView2ControllerCompletedHandler handler);
    nint CreateWebResourceResponse(nint content, int statusCode, string reasonPhrase, string headers);
    string GetBrowserVersionString();
    [PreserveSig] int Slot006_AddNewBrowserVersionAvailable();
    [PreserveSig] int Slot007_RemoveNewBrowserVersionAvailable();
    // ICoreWebView2Environment2
    [PreserveSig] int Slot008_CreateWebResourceRequest();
    // ICoreWebView2Environment3
    [PreserveSig] int Slot009_CreateCoreWebView2CompositionController();
    [PreserveSig] int Slot010_CreateCoreWebView2PointerInfo();
    // ICoreWebView2Environment4
    [PreserveSig] int Slot011_GetAutomationProviderForWindow();
    // ICoreWebView2Environment5
    EventRegistrationToken AddBrowserProcessExited(ICoreWebView2BrowserProcessExitedEventHandler eventHandler);
    void RemoveBrowserProcessExited(EventRegistrationToken token);
    // ICoreWebView2Environment6
    [PreserveSig] int Slot014_CreatePrintSettings();
    // ICoreWebView2Environment7
    string GetUserDataFolder();
    // ICoreWebView2Environment8
    [PreserveSig] int Slot016_AddProcessInfosChanged();
    [PreserveSig] int Slot017_RemoveProcessInfosChanged();
    [PreserveSig] int Slot018_GetProcessInfos();
    // ICoreWebView2Environment9
    [PreserveSig] int Slot019_CreateContextMenuItem();
    // ICoreWebView2Environment10
    [PreserveSig] int Slot020_CreateCoreWebView2ControllerOptions();
    [PreserveSig] int Slot021_CreateCoreWebView2ControllerWithOptions();
    [PreserveSig] int Slot022_CreateCoreWebView2CompositionControllerWithOptions();
}

/// <summary><c>ICoreWebView2Controller</c>, the base interface (slots 3–25).</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("4d00c0d1-9434-4eb6-8078-8697a560334f")]
internal partial interface ICoreWebView2Controller
{
    // ICoreWebView2Controller
    int GetIsVisible();
    void PutIsVisible(int isVisible);
    [PreserveSig] int Slot005_GetBounds();
    void PutBounds(Rect bounds);
    [PreserveSig] int Slot007_GetZoomFactor();
    [PreserveSig] int Slot008_PutZoomFactor();
    [PreserveSig] int Slot009_AddZoomFactorChanged();
    [PreserveSig] int Slot010_RemoveZoomFactorChanged();
    [PreserveSig] int Slot011_SetBoundsAndZoomFactor();
    [PreserveSig] int Slot012_MoveFocus();
    [PreserveSig] int Slot013_AddMoveFocusRequested();
    [PreserveSig] int Slot014_RemoveMoveFocusRequested();
    [PreserveSig] int Slot015_AddGotFocus();
    [PreserveSig] int Slot016_RemoveGotFocus();
    [PreserveSig] int Slot017_AddLostFocus();
    [PreserveSig] int Slot018_RemoveLostFocus();
    [PreserveSig] int Slot019_AddAcceleratorKeyPressed();
    [PreserveSig] int Slot020_RemoveAcceleratorKeyPressed();
    [PreserveSig] int Slot021_GetParentWindow();
    [PreserveSig] int Slot022_PutParentWindow();
    [PreserveSig] int Slot023_NotifyParentWindowPositionChanged();
    void Close();
    nint GetCoreWebView2();
}
