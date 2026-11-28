using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Spar.Windows.Interop;

// UI Automation's server side, hand-declared from UIAutomationCore.h, UIAutomationCoreApi.h and UIAutomationClient.h
// (Windows SDK 10.0.26100): the one provider interface the host implements, for the page-drawn title bar's window and
// the windows over it (plan 3, "Title bar drawn by the page", accessibility), and the two calls that serve it.

/// <summary>
/// <c>IRawElementProviderSimple</c> (slots 3–6), which the host implements: a window's provider that changes only how
/// UI Automation sees the window itself, leaving everything else to the host provider Windows makes for it.
/// </summary>
[GeneratedComInterface]
[Guid("d6dd68d1-86fd-4332-8666-9abedea2d24c")]
internal partial interface IRawElementProviderSimple
{
    [PreserveSig]
    int GetProviderOptions(out int options);

    [PreserveSig]
    int GetPatternProvider(int patternId, out nint provider);

    [PreserveSig]
    int GetPropertyValue(int propertyId, out Variant value);

    [PreserveSig]
    int GetHostRawElementProvider(out nint provider);
}

/// <summary>
/// A <c>VARIANT</c>: its type, three reserved words, and the value's 8 bytes plus the 8 a record would use (16 bytes on
/// 32-bit Windows, 24 on 64-bit). The providers here answer only <c>VT_EMPTY</c> and <c>VT_BOOL</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct Variant
{
    public const ushort Empty = 0;
    public const ushort Bool = 11;

    public ushort Type;
    public ushort Reserved1;
    public ushort Reserved2;
    public ushort Reserved3;
    public nint Value;
    public nint Record;
}

internal static partial class Automation
{
    /// <summary><c>WM_GETOBJECT</c>'s object id for a UI Automation provider.</summary>
    public const int RootObjectId = -25;

    /// <summary><c>ProviderOptions_ServerSideProvider</c>: the provider runs in the window's own process.</summary>
    public const int ServerSideProvider = 0x2;

    /// <summary>
    /// <c>ProviderOptions_RefuseNonClientSupport</c>: UI Automation adds no non-client provider for the window, so the
    /// native title bar the custom frame no longer has (its "System" menu and empty Minimize, Maximize and Close buttons)
    /// stays out of the tree.
    /// </summary>
    public const int RefuseNonClientSupport = 0x40;

    public const int IsControlElementPropertyId = 30016;
    public const int IsContentElementPropertyId = 30017;

    /// <summary>Hands <paramref name="provider"/> to UI Automation for <c>WM_GETOBJECT</c>; a null provider, at the window's end, releases UI Automation's.</summary>
    [LibraryImport("UIAutomationCore")]
    public static partial nint UiaReturnRawElementProvider(nint window, nuint wParam, nint lParam, IRawElementProviderSimple? provider);

    /// <summary>The provider Windows makes for a window, with one reference the caller owns.</summary>
    [LibraryImport("UIAutomationCore")]
    public static partial int UiaHostProviderFromHwnd(nint window, out nint provider);
}
