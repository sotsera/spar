using System.Runtime.InteropServices;

namespace Sotsera.Spar.Windows.Interop;

internal static unsafe partial class NativeMethods
{
    [LibraryImport("ole32.dll")]
    internal static partial int CoInitializeEx(nint reserved, uint concurrency);

    [LibraryImport("ole32.dll")]
    internal static partial void CoUninitialize();

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint GetModuleHandle(string? name);

    [LibraryImport("kernel32.dll")]
    internal static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
    internal static partial ushort RegisterClassEx(in NativeWindowClass windowClass);

    [LibraryImport("user32.dll", EntryPoint = "UnregisterClassW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnregisterClass(string name, nint instance);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16,
        SetLastError = true)]
    internal static partial nint CreateWindowEx(uint extendedStyle, string className, string title, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    internal static partial nint DefWindowProc(nint window, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyWindow(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(nint window, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetClientRect(nint window, out NativeRect rect);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)]
    internal static partial int GetMessage(out NativeMessage message, nint window, uint min, uint max);

    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PeekMessage(out NativeMessage message, nint window, uint min, uint max, uint remove);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TranslateMessage(in NativeMessage message);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    internal static partial nint DispatchMessage(in NativeMessage message);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostMessage(nint window, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    internal static partial void PostQuitMessage(int exitCode);

    [LibraryImport("WebView2Loader.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int CreateCoreWebView2EnvironmentWithOptions(string? browserFolder, string userDataFolder,
        nint options, nint handler);
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRect
{
    internal int Left;
    internal int Top;
    internal int Right;
    internal int Bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeWindowClass
{
    internal uint Size;
    internal uint Style;
    internal delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nint> WindowProcedure;
    internal int ClassExtra;
    internal int WindowExtra;
    internal nint Instance;
    internal nint Icon;
    internal nint Cursor;
    internal nint Background;
    internal nint MenuName;
    internal char* ClassName;
    internal nint SmallIcon;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeMessage
{
    internal nint Window;
    internal uint Message;
    internal nuint WParam;
    internal nint LParam;
    internal uint Time;
    internal int X;
    internal int Y;
    internal uint Private;
}
