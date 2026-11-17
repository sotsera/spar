using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Spar.Windows.Interop;

// The controller's creation options and the profile, hand-declared from WebView2.h like the other interfaces (see
// WebView2.Environment.cs for the rules): plan section 3d's transparent default background is set on the options at
// creation (ICoreWebView2ControllerOptions3, Runtime 137), and the page's colour scheme on the profile
// (ICoreWebView2Profile, reached through ICoreWebView2_13's get_Profile).

/// <summary>The C <c>COREWEBVIEW2_COLOR</c>: alpha, red, green, blue, one byte each, passed by value.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct WebView2Color
{
    public byte A;
    public byte R;
    public byte G;
    public byte B;
}

/// <summary><c>ICoreWebView2ControllerOptions3</c>, flat: Options 3–6, _2 7–8, _3 9–10.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("b32b191a-8998-57ca-b7cb-e04617e4ce4a")]
internal partial interface ICoreWebView2ControllerOptions3
{
    // ICoreWebView2ControllerOptions
    [PreserveSig] int Slot003_GetProfileName();
    [PreserveSig] int Slot004_PutProfileName();
    [PreserveSig] int Slot005_GetIsInPrivateModeEnabled();
    [PreserveSig] int Slot006_PutIsInPrivateModeEnabled();
    // ICoreWebView2ControllerOptions2
    [PreserveSig] int Slot007_GetScriptLocale();
    [PreserveSig] int Slot008_PutScriptLocale();
    // ICoreWebView2ControllerOptions3
    [PreserveSig] int Slot009_GetDefaultBackgroundColor();
    void PutDefaultBackgroundColor(WebView2Color value);
}

/// <summary><c>ICoreWebView2Profile</c>, the base interface (slots 3–9).</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16, Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("79110ad3-cd5d-4373-8bc3-c60658f17a5f")]
internal partial interface ICoreWebView2Profile
{
    [PreserveSig] int Slot003_GetProfileName();
    [PreserveSig] int Slot004_GetIsInPrivateModeEnabled();
    [PreserveSig] int Slot005_GetProfilePath();
    [PreserveSig] int Slot006_GetDefaultDownloadFolderPath();
    [PreserveSig] int Slot007_PutDefaultDownloadFolderPath();
    [PreserveSig] int Slot008_GetPreferredColorScheme();
    void PutPreferredColorScheme(int value);
}
