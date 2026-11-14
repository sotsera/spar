using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Checks.Desktop.Interop;

// WebView2 and DirectComposition are called through raw vtable slots on owned interface pointers. Slot numbers come
// from the C vtable structs of WebView2.h (Microsoft.Web.WebView2 1.0.4314.35) and dcomp.h (SDK 10.0.26100), listed
// by tools/vtable.mjs; slots 0-2 are IUnknown. Callbacks are source-generated COM objects ([GeneratedComClass]).
internal static unsafe class Vt
{
    private static void* Fn(nint p, int slot) => (*(void***)p)[slot];

    public static int Call(nint p, int slot) => ((delegate* unmanaged[Stdcall]<nint, int>)Fn(p, slot))(p);

    public static int Call(nint p, int slot, int a) => ((delegate* unmanaged[Stdcall]<nint, int, int>)Fn(p, slot))(p, a);

    public static int Call(nint p, int slot, uint a) => ((delegate* unmanaged[Stdcall]<nint, uint, int>)Fn(p, slot))(p, a);

    public static int Call(nint p, int slot, nint a) => ((delegate* unmanaged[Stdcall]<nint, nint, int>)Fn(p, slot))(p, a);

    public static int Call(nint p, int slot, nint a, nint b) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, nint, int>)Fn(p, slot))(p, a, b);

    public static int Call(nint p, int slot, nint a, nint b, nint c) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, nint, nint, int>)Fn(p, slot))(p, a, b, c);

    public static int Call(nint p, int slot, nint a, int b) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, int, int>)Fn(p, slot))(p, a, b);

    public static int Call(nint p, int slot, nint a, int b, uint c) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, int, uint, int>)Fn(p, slot))(p, a, b, c);

    public static int Get(nint p, int slot, out nint value)
    {
        nint v = 0;
        var hr = ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Fn(p, slot))(p, &v);
        value = v;
        return hr;
    }

    public static int GetInt(nint p, int slot, out int value)
    {
        int v = 0;
        var hr = ((delegate* unmanaged[Stdcall]<nint, int*, int>)Fn(p, slot))(p, &v);
        value = v;
        return hr;
    }

    public static int GetDouble(nint p, int slot, out double value)
    {
        double v = 0;
        var hr = ((delegate* unmanaged[Stdcall]<nint, double*, int>)Fn(p, slot))(p, &v);
        value = v;
        return hr;
    }

    public static int GetRect(nint p, int slot, uint index, out Rect value)
    {
        Rect v = default;
        var hr = ((delegate* unmanaged[Stdcall]<nint, uint, Rect*, int>)Fn(p, slot))(p, index, &v);
        value = v;
        return hr;
    }

    public static int PutRect(nint p, int slot, Rect value) =>
        ((delegate* unmanaged[Stdcall]<nint, Rect, int>)Fn(p, slot))(p, value);

    public static int AddHandler(nint p, int slot, nint handler, out long token)
    {
        long t = 0;
        var hr = ((delegate* unmanaged[Stdcall]<nint, nint, long*, int>)Fn(p, slot))(p, handler, &t);
        token = t;
        return hr;
    }

    public static int RemoveHandler(nint p, int slot, long token) =>
        ((delegate* unmanaged[Stdcall]<nint, long, int>)Fn(p, slot))(p, token);

    public static int GetString(nint p, int slot, out string? value)
    {
        char* s = null;
        var hr = ((delegate* unmanaged[Stdcall]<nint, char**, int>)Fn(p, slot))(p, &s);
        value = s == null ? null : new string(s);
        if (s != null)
        {
            Marshal.FreeCoTaskMem((nint)s);
        }

        return hr;
    }

    public static int PutString(nint p, int slot, string value)
    {
        fixed (char* s = value)
        {
            return ((delegate* unmanaged[Stdcall]<nint, char*, int>)Fn(p, slot))(p, s);
        }
    }

    // ICoreWebView2CompositionController4::GetNonClientRegionAtPoint(POINT, COREWEBVIEW2_NON_CLIENT_REGION_KIND*).
    public static int NonClientRegionAtPoint(nint p, Point point, out int kind)
    {
        int k = 0;
        var hr = ((delegate* unmanaged[Stdcall]<nint, Point, int*, int>)Fn(p, 16))(p, point, &k);
        kind = k;
        return hr;
    }

    // ICoreWebView2CompositionController::SendMouseInput(kind, virtualKeys, mouseData, POINT).
    public static int SendMouseInput(nint p, int kind, int keys, uint data, Point point) =>
        ((delegate* unmanaged[Stdcall]<nint, int, int, uint, Point, int>)Fn(p, 5))(p, kind, keys, data, point);

    // ICoreWebView2::AddWebResourceRequestedFilterWithRequestSourceKinds (ICoreWebView2_22 slot 123).
    public static int AddFilterWithSourceKinds(nint p, string uri, int context, uint kinds)
    {
        fixed (char* s = uri)
        {
            return ((delegate* unmanaged[Stdcall]<nint, char*, int, uint, int>)Fn(p, 123))(p, s, context, kinds);
        }
    }

    // ICoreWebView2Environment::CreateWebResourceResponse(IStream*, int, LPCWSTR, LPCWSTR, out response).
    public static int CreateResponse(nint env, nint stream, int status, string reason, string headers, out nint response)
    {
        nint r = 0;
        int hr;
        fixed (char* pr = reason)
        fixed (char* ph = headers)
        {
            hr = ((delegate* unmanaged[Stdcall]<nint, nint, int, char*, char*, nint*, int>)Fn(env, 4))(env, stream, status, pr, ph, &r);
        }

        response = r;
        return hr;
    }

    // ICoreWebView2::ExecuteScript(LPCWSTR, handler).
    public static int ExecuteScript(nint webView, string script, nint handler)
    {
        fixed (char* s = script)
        {
            return ((delegate* unmanaged[Stdcall]<nint, char*, nint, int>)Fn(webView, 29))(webView, s, handler);
        }
    }

    public static nint QueryInterface(nint p, in Guid iid)
    {
        return Marshal.QueryInterface(p, in iid, out var result) >= 0 ? result : 0;
    }

    public static void Release(ref nint p)
    {
        if (p != 0)
        {
            Marshal.Release(p);
            p = 0;
        }
    }

    public static nint Wrap<T>(T handler) where T : class => (nint)ComInterfaceMarshaller<T>.ConvertToUnmanaged(handler);

    public static void Check(int hr, string what)
    {
        if (hr < 0)
        {
            throw new COMException($"{what} failed: 0x{hr:X8}", hr);
        }
    }
}

internal static class Iid
{
    public static readonly Guid Environment3 = new("80a22ae3-be7c-4ce2-afe1-5a50056cdeeb");
    public static readonly Guid Environment10 = new("ee0eb9df-6f12-46ce-b53f-3f47b9c928e0");
    public static readonly Guid ControllerOptions3 = new("b32b191a-8998-57ca-b7cb-e04617e4ce4a");
    public static readonly Guid Controller = new("4d00c0d1-9434-4eb6-8078-8697a560334f");
    public static readonly Guid Controller4 = new("97d418d5-a426-4e49-a151-e1a10f327d9e");
    public static readonly Guid CompositionController = new("3df9b733-b9ae-4a15-86b4-eb9ee9826469");
    public static readonly Guid CompositionController4 = new("7c367b9b-3d2b-450f-9e58-d61a20f486aa");
    public static readonly Guid WebView13 = new("f75f09a8-667e-4983-88d6-c8773f315e84");
    public static readonly Guid WebView22 = new("db75dfc7-a857-4632-a398-6969dde26c0a");
    public static readonly Guid Settings9 = new("0528a73b-e92d-49f4-927a-e547dddaa37d");
    public static readonly Guid DCompositionDevice = new("c37ea93a-e7aa-450d-b16f-9746cb0407f3");
    public static readonly Guid RawElementProviderSimple = new("d6dd68d1-86fd-4332-8666-9abedea2d24c");
    public static readonly Guid PermissionArgs3 = new("e61670bc-3dce-4177-86d2-c629ae3cb6ac");
    public static readonly Guid Profile4 = new("8f4ae680-192e-4ec8-833a-21cfadaef628");
    public static readonly Guid AccPropServicesClsid = new("b5f8350b-0548-48b1-a6ee-88bd00b4a5e7");
    public static readonly Guid AccPropServices = new("6e26e776-04f0-495d-80e4-3330352e3169");
    public static readonly Guid IsControlElementProperty = new("95f35085-abcc-4afd-a5f4-dbb46c230fdb");
    public static readonly Guid IsContentElementProperty = new("4bda64a8-f5d8-480b-8155-ef2e89adb672");
    public static readonly Guid AccName = new("608d3df8-8128-4aa7-a428-f55e49267291");
}

[GeneratedComInterface]
[Guid("4e8a3389-c9d8-4bd2-b6b5-124fee6cc14d")]
internal partial interface IEnvironmentCompletedHandler
{
    void Invoke(int errorCode, nint environment);
}

[GeneratedComInterface]
[Guid("6c4819f3-c9b7-4260-8127-c9f5bde7f68c")]
internal partial interface IControllerCompletedHandler
{
    void Invoke(int errorCode, nint controller);
}

[GeneratedComInterface]
[Guid("02fab84b-1428-4fb7-ad45-1b2e64736184")]
internal partial interface ICompositionControllerCompletedHandler
{
    void Invoke(int errorCode, nint compositionController);
}

[GeneratedComInterface]
[Guid("ab00b74c-15f1-4646-80e8-e76341d25d71")]
internal partial interface IWebResourceRequestedHandler
{
    void Invoke(nint sender, nint args);
}

[GeneratedComInterface]
[Guid("57213f19-00e6-49fa-8e07-898ea01ecbd2")]
internal partial interface IWebMessageReceivedHandler
{
    void Invoke(nint sender, nint args);
}

[GeneratedComInterface]
[Guid("d33a35bf-1c49-4f98-93ab-006e0533fe1c")]
internal partial interface INavigationCompletedHandler
{
    void Invoke(nint sender, nint args);
}

[GeneratedComInterface]
[Guid("9da43ccc-26e1-4dad-b56c-d8961c94c571")]
internal partial interface ICursorChangedHandler
{
    void Invoke(nint sender, nint args);
}

[GeneratedComInterface]
[Guid("4a794e66-aa6c-46bd-93a3-382196837680")]
internal partial interface INonClientRegionChangedHandler
{
    void Invoke(nint sender, nint args);
}

[GeneratedComInterface]
[Guid("fa504257-a216-4911-a860-fe8825712861")]
internal partial interface IBrowserProcessExitedHandler
{
    void Invoke(nint environment, nint args);
}

[GeneratedComInterface]
[Guid("49511172-cc67-4bca-9923-137112f4c4cc")]
internal partial interface IExecuteScriptCompletedHandler
{
    void Invoke(int errorCode, nint resultObjectAsJson);
}

[GeneratedComInterface]
[Guid("15e1c6a3-c72a-4df3-91d7-d097fbec6bfd")]
internal partial interface IPermissionRequestedHandler
{
    void Invoke(nint sender, nint args);
}

[GeneratedComInterface]
[Guid("fc77fb30-9c9e-4076-b8c7-7644a703ca1b")]
internal partial interface ISetPermissionStateCompletedHandler
{
    void Invoke(int errorCode);
}

[GeneratedComInterface]
[Guid("38274481-a15c-4563-94cf-990edc9aeb95")]
internal partial interface IGetNonDefaultPermissionSettingsCompletedHandler
{
    void Invoke(int errorCode, nint collectionView);
}

// ICoreWebView2EnvironmentOptions, implemented by the host (WebView2EnvironmentOptions.h). Strings returned to the
// caller are allocated with CoTaskMemAlloc; the caller frees them.
[GeneratedComInterface]
[Guid("2fde08a8-1e9a-4766-8c05-95a9ceb9d1c5")]
internal partial interface IEnvironmentOptions
{
    [PreserveSig] int GetAdditionalBrowserArguments(out nint value);
    [PreserveSig] int PutAdditionalBrowserArguments(nint value);
    [PreserveSig] int GetLanguage(out nint value);
    [PreserveSig] int PutLanguage(nint value);
    [PreserveSig] int GetTargetCompatibleBrowserVersion(out nint value);
    [PreserveSig] int PutTargetCompatibleBrowserVersion(nint value);
    [PreserveSig] int GetAllowSingleSignOnUsingOSPrimaryAccount(out int value);
    [PreserveSig] int PutAllowSingleSignOnUsingOSPrimaryAccount(int value);
}

// IRawElementProviderSimple (UIAutomationCore.h): slots 3 ProviderOptions, 4 GetPatternProvider, 5 GetPropertyValue,
// 6 HostRawElementProvider. The VARIANT is written through a pointer.
[GeneratedComInterface]
[Guid("d6dd68d1-86fd-4332-8666-9abedea2d24c")]
internal partial interface IRawElementProviderSimple
{
    [PreserveSig] int GetProviderOptions(out int options);
    [PreserveSig] int GetPatternProvider(int patternId, out nint provider);
    [PreserveSig] int GetPropertyValue(int propertyId, nint variant);
    [PreserveSig] int GetHostRawElementProvider(out nint provider);
}

[GeneratedComClass]
internal sealed partial class EnvironmentOptions(string arguments) : IEnvironmentOptions
{
    // CORE_WEBVIEW_TARGET_PRODUCT_VERSION of the SDK headers used (1.0.4314.35).
    private const string TargetVersion = "156.0.4314.35";

    public int GetAdditionalBrowserArguments(out nint value)
    {
        value = Marshal.StringToCoTaskMemUni(arguments);
        return 0;
    }

    public int PutAdditionalBrowserArguments(nint value) => 0;

    public int GetLanguage(out nint value)
    {
        value = Marshal.StringToCoTaskMemUni("");
        return 0;
    }

    public int PutLanguage(nint value) => 0;

    public int GetTargetCompatibleBrowserVersion(out nint value)
    {
        value = Marshal.StringToCoTaskMemUni(TargetVersion);
        return 0;
    }

    public int PutTargetCompatibleBrowserVersion(nint value) => 0;

    public int GetAllowSingleSignOnUsingOSPrimaryAccount(out int value)
    {
        value = 0;
        return 0;
    }

    public int PutAllowSingleSignOnUsingOSPrimaryAccount(int value) => 0;
}

// A server-side UIA provider for the top-level window that only sets provider options (for example
// ProviderOptions_RefuseNonClientSupport) and delegates everything else to the HWND's host provider.
[GeneratedComClass]
internal sealed unsafe partial class TopLevelProvider(nint hwnd, int options) : IRawElementProviderSimple
{
    public int GetProviderOptions(out int value)
    {
        value = options;
        return 0;
    }

    public int GetPatternProvider(int patternId, out nint provider)
    {
        provider = 0;
        return 0;
    }

    public int GetPropertyValue(int propertyId, nint variant)
    {
        new Span<byte>((void*)variant, 24).Clear(); // VT_EMPTY: no override
        return 0;
    }

    public int GetHostRawElementProvider(out nint provider) => Native.UiaHostProviderFromHwnd(hwnd, out provider);
}

// A server-side UIA provider for a child window that marks it as neither a control nor a content element, so it
// leaves the control and content views; everything else comes from the HWND's host provider.
[GeneratedComClass]
internal sealed unsafe partial class HiddenPaneProvider(nint hwnd) : IRawElementProviderSimple
{
    public int GetProviderOptions(out int value)
    {
        value = 0x2; // ProviderOptions_ServerSideProvider
        return 0;
    }

    public int GetPatternProvider(int patternId, out nint provider)
    {
        provider = 0;
        return 0;
    }

    public int GetPropertyValue(int propertyId, nint variant)
    {
        var v = new Span<byte>((void*)variant, 24);
        v.Clear();
        if (propertyId is 30016 or 30017) // UIA_IsControlElementPropertyId, UIA_IsContentElementPropertyId
        {
            *(ushort*)variant = 11; // VT_BOOL, value VARIANT_FALSE
        }

        return 0;
    }

    public int GetHostRawElementProvider(out nint provider) => Native.UiaHostProviderFromHwnd(hwnd, out provider);
}

[GeneratedComClass]
internal sealed partial class PermissionRequested(Action<nint, nint> action) : IPermissionRequestedHandler
{
    public void Invoke(nint sender, nint args) => action(sender, args);
}

[GeneratedComClass]
internal sealed partial class SetPermissionStateCompleted(Action<int> action) : ISetPermissionStateCompletedHandler
{
    public void Invoke(int errorCode) => action(errorCode);
}

[GeneratedComClass]
internal sealed partial class GetPermissionsCompleted(Action<int, nint> action) : IGetNonDefaultPermissionSettingsCompletedHandler
{
    public void Invoke(int errorCode, nint collectionView) => action(errorCode, collectionView);
}

// One callback class per handler interface, each forwarding to a delegate.
[GeneratedComClass]
internal sealed partial class EnvironmentCompleted(Action<int, nint> action) : IEnvironmentCompletedHandler
{
    public void Invoke(int errorCode, nint environment) => action(errorCode, environment);
}

[GeneratedComClass]
internal sealed partial class ControllerCompleted(Action<int, nint> action) : IControllerCompletedHandler
{
    public void Invoke(int errorCode, nint controller) => action(errorCode, controller);
}

[GeneratedComClass]
internal sealed partial class CompositionControllerCompleted(Action<int, nint> action) : ICompositionControllerCompletedHandler
{
    public void Invoke(int errorCode, nint compositionController) => action(errorCode, compositionController);
}

[GeneratedComClass]
internal sealed partial class WebResourceRequested(Action<nint, nint> action) : IWebResourceRequestedHandler
{
    public void Invoke(nint sender, nint args) => action(sender, args);
}

[GeneratedComClass]
internal sealed partial class WebMessageReceived(Action<nint, nint> action) : IWebMessageReceivedHandler
{
    public void Invoke(nint sender, nint args) => action(sender, args);
}

[GeneratedComClass]
internal sealed partial class NavigationCompleted(Action<nint, nint> action) : INavigationCompletedHandler
{
    public void Invoke(nint sender, nint args) => action(sender, args);
}

[GeneratedComClass]
internal sealed partial class CursorChanged(Action<nint, nint> action) : ICursorChangedHandler
{
    public void Invoke(nint sender, nint args) => action(sender, args);
}

[GeneratedComClass]
internal sealed partial class NonClientRegionChanged(Action<nint, nint> action) : INonClientRegionChangedHandler
{
    public void Invoke(nint sender, nint args) => action(sender, args);
}

[GeneratedComClass]
internal sealed partial class BrowserProcessExited(Action<nint, nint> action) : IBrowserProcessExitedHandler
{
    public void Invoke(nint environment, nint args) => action(environment, args);
}

[GeneratedComClass]
internal sealed partial class ExecuteScriptCompleted(Action<int, string?> action) : IExecuteScriptCompletedHandler
{
    public unsafe void Invoke(int errorCode, nint resultObjectAsJson) =>
        action(errorCode, resultObjectAsJson == 0 ? null : new string((char*)resultObjectAsJson));
}

internal static partial class Loader
{
    [LibraryImport("WebView2Loader", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int CreateCoreWebView2EnvironmentWithOptions(string? browserExecutableFolder,
        string? userDataFolder, nint environmentOptions, nint environmentCreatedHandler);
}
