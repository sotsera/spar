using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Startup.Win;

// The statically linked WebView2 loader (WebView2.h 1.0.4314.35, lines 3017-3020) and the few vtable slots this
// check calls, counted from the C vtable structs, IUnknown included:
//   ICoreWebView2Environment  3 CreateCoreWebView2Controller, 5 get_BrowserVersionString
//   ICoreWebView2Environment5 (319e423d-...) 12 add_BrowserProcessExited, 13 remove_BrowserProcessExited
//   ICoreWebView2Controller   24 Close
//   ICoreWebView2BrowserProcessExitedEventArgs 3 get_BrowserProcessExitKind, 4 get_BrowserProcessId
internal static unsafe partial class WebView2
{
    public static readonly Guid Environment5 = new("319e423d-e0d7-4b8d-9254-ae9475de9b17");

    [LibraryImport("WebView2Loader", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GetAvailableCoreWebView2BrowserVersionString(string? browserExecutableFolder, out nint versionInfo);

    [LibraryImport("WebView2Loader", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int CompareBrowserVersions(string version1, string version2, out int result);

    [LibraryImport("WebView2Loader", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int CreateCoreWebView2EnvironmentWithOptions(string? browserExecutableFolder, string? userDataFolder,
        nint environmentOptions, nint environmentCreatedHandler);

    public static nint Wrap<T>(T handler) where T : class => (nint)ComInterfaceMarshaller<T>.ConvertToUnmanaged(handler);

    private static void* Fn(nint p, int slot) => (*(void***)p)[slot];

    public static int CreateController(nint environment, nint hwnd, nint handler) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, nint, int>)Fn(environment, 3))(environment, hwnd, handler);

    public static string? BrowserVersion(nint environment)
    {
        nint s = 0;
        var hr = ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Fn(environment, 5))(environment, &s);
        if (hr < 0 || s == 0)
        {
            return null;
        }

        var text = Marshal.PtrToStringUni(s);
        Native.CoTaskMemFree(s);
        return text;
    }

    public static int AddBrowserProcessExited(nint environment5, nint handler, out long token)
    {
        long t = 0;
        var hr = ((delegate* unmanaged[Stdcall]<nint, nint, long*, int>)Fn(environment5, 12))(environment5, handler, &t);
        token = t;
        return hr;
    }

    public static int CloseController(nint controller) => ((delegate* unmanaged[Stdcall]<nint, int>)Fn(controller, 24))(controller);

    public static (int Kind, uint Pid) ExitArgs(nint args)
    {
        int kind = -1;
        uint pid = 0;
        ((delegate* unmanaged[Stdcall]<nint, int*, int>)Fn(args, 3))(args, &kind);
        ((delegate* unmanaged[Stdcall]<nint, uint*, int>)Fn(args, 4))(args, &pid);
        return (kind, pid);
    }
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
[Guid("fa504257-a216-4911-a860-fe8825712861")]
internal partial interface IBrowserProcessExitedHandler
{
    void Invoke(nint environment, nint args);
}

[GeneratedComClass]
internal sealed partial class EnvironmentCompleted(Action<int, nint> done) : IEnvironmentCompletedHandler
{
    public void Invoke(int errorCode, nint environment) => done(errorCode, environment);
}

[GeneratedComClass]
internal sealed partial class ControllerCompleted(Action<int, nint> done) : IControllerCompletedHandler
{
    public void Invoke(int errorCode, nint controller) => done(errorCode, controller);
}

[GeneratedComClass]
internal sealed partial class BrowserProcessExited(Action<nint> exited) : IBrowserProcessExitedHandler
{
    public void Invoke(nint environment, nint args) => exited(args);
}
