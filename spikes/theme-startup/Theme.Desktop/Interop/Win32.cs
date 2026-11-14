using System.Runtime.InteropServices;

namespace Theme.Desktop.Interop;

// Hand-written Win32 imports for the spike (x64). HWND, HDC, HBRUSH and friends are nint.

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WindowClass
{
    public uint Size;
    public uint Style;
    public delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nint> WindowProcedure;
    public int ClassExtra;
    public int WindowExtra;
    public nint Instance;
    public nint Icon;
    public nint Cursor;
    public nint Background;
    public char* MenuName;
    public char* ClassName;
    public nint SmallIcon;
}

[StructLayout(LayoutKind.Sequential)]
internal struct Message
{
    public nint Window;
    public uint Id;
    public nuint WParam;
    public nint LParam;
    public uint Time;
    public int X;
    public int Y;
    public uint Private;
}

[StructLayout(LayoutKind.Sequential)]
internal struct Point
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BitmapInfoHeader
{
    public uint Size;
    public int Width;
    public int Height;
    public ushort Planes;
    public ushort BitCount;
    public uint Compression;
    public uint SizeImage;
    public int XPelsPerMeter;
    public int YPelsPerMeter;
    public uint ClrUsed;
    public uint ClrImportant;
}

internal static unsafe partial class User32
{
    public const uint WmDestroy = 0x0002;
    public const uint WmSize = 0x0005;
    public const uint WmClose = 0x0010;
    public const uint WmPaint = 0x000F;
    public const uint WmEraseBackground = 0x0014;
    public const uint WmSettingChange = 0x001A;
    public const uint WmApp = 0x8000;
    public const uint WsOverlappedWindow = 0x00CF0000;
    public const int SwHide = 0;
    public const int SwShowNoActivate = 4;
    public const uint PwRenderFullContent = 2;
    public const uint SmtoAbortIfHung = 0x0002;
    public const int SmXVirtualScreen = 76;
    public const int SmYVirtualScreen = 77;

    [LibraryImport("user32", EntryPoint = "RegisterClassExW", SetLastError = true)]
    public static partial ushort RegisterClassEx(WindowClass* windowClass);

    [LibraryImport("user32", EntryPoint = "CreateWindowExW", SetLastError = true)]
    public static partial nint CreateWindowEx(uint exStyle, char* className, char* windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32", EntryPoint = "DefWindowProcW")]
    public static partial nint DefWindowProc(nint window, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint window, int command);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint window);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UpdateWindow(nint window);

    [LibraryImport("user32", EntryPoint = "GetMessageW")]
    public static partial int GetMessage(Message* message, nint window, uint min, uint max);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(Message* message);

    [LibraryImport("user32", EntryPoint = "DispatchMessageW")]
    public static partial nint DispatchMessage(Message* message);

    [LibraryImport("user32", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessage(nint window, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32", EntryPoint = "SendMessageTimeoutW")]
    public static partial nint SendMessageTimeout(nint window, uint message, nuint wParam, nint lParam, uint flags, uint timeout, nuint* result);

    [LibraryImport("user32")]
    public static partial void PostQuitMessage(int exitCode);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint window);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetClientRect(nint window, out Rect rect);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(nint window, out Rect rect);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ClientToScreen(nint window, ref Point point);

    [LibraryImport("user32")]
    public static partial int FillRect(nint dc, in Rect rect, nint brush);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool InvalidateRect(nint window, Rect* rect, [MarshalAs(UnmanagedType.Bool)] bool erase);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PrintWindow(nint window, nint dc, uint flags);

    [LibraryImport("user32")]
    public static partial nint GetDC(nint window);

    [LibraryImport("user32")]
    public static partial int ReleaseDC(nint window, nint dc);

    [LibraryImport("user32", EntryPoint = "LoadCursorW")]
    public static partial nint LoadCursor(nint instance, nint name);

    [LibraryImport("user32")]
    public static partial int GetSystemMetrics(int index);

    [LibraryImport("user32")]
    public static partial nint GetForegroundWindow();
}

internal static unsafe partial class Gdi32
{
    [LibraryImport("gdi32")]
    public static partial nint CreateSolidBrush(uint colorRef);

    [LibraryImport("gdi32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(nint handle);

    [LibraryImport("gdi32")]
    public static partial nint CreateCompatibleDC(nint dc);

    [LibraryImport("gdi32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteDC(nint dc);

    [LibraryImport("gdi32")]
    public static partial nint SelectObject(nint dc, nint handle);

    [LibraryImport("gdi32")]
    public static partial nint CreateDIBSection(nint dc, BitmapInfoHeader* info, uint usage, void** bits, nint section, uint offset);
}

internal static unsafe partial class Dwm
{
    public const uint UseImmersiveDarkMode = 20;
    public const uint Cloak = 13;
    public const uint Cloaked = 14;

    [LibraryImport("dwmapi")]
    public static partial int DwmSetWindowAttribute(nint window, uint attribute, void* value, uint size);

    [LibraryImport("dwmapi")]
    public static partial int DwmGetWindowAttribute(nint window, uint attribute, void* value, uint size);

    public static int SetBool(nint window, uint attribute, bool value)
    {
        var flag = value ? 1 : 0;
        return DwmSetWindowAttribute(window, attribute, &flag, sizeof(int));
    }
}

internal static unsafe partial class Kernel32
{
    [LibraryImport("kernel32", EntryPoint = "GetModuleHandleW")]
    public static partial nint GetModuleHandle(char* name);
}
