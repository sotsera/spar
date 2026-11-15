using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Spar.Windows;

// Hand-written from WinUser.h / TlHelp32.h (x64). Only what the spike's window and message loop use.

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WndClassEx
{
    public uint Size;
    public uint Style;
    public delegate* unmanaged<nint, uint, nint, nint, nint> WindowProcedure;
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
internal struct Msg
{
    public nint Window;
    public uint Message;
    public nint WParam;
    public nint LParam;
    public uint Time;
    public int X;
    public int Y;
    public uint Private;
}

[SupportedOSPlatform("windows")]
internal static unsafe partial class Win32
{
    public const uint WmDestroy = 0x0002;
    public const uint WmSize = 0x0005;
    public const uint WmClose = 0x0010;
    public const uint WmQuit = 0x0012;
    public const uint WmUser = 0x0400;
    public const uint WmApp = 0x8000;
    public const uint WsOverlappedWindow = 0x00CF0000;
    public const uint WsExToolWindow = 0x00000080;
    public const uint WsExNoActivate = 0x08000000;
    public const int SwShowNormal = 1;
    public const int SwShowNoActivate = 4;
    public const int GwlpUserData = -21;
    public const uint PmNoRemove = 0;

    [LibraryImport("user32", SetLastError = true)]
    public static partial ushort RegisterClassExW(WndClassEx* windowClass);

    [LibraryImport("user32", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWindowExW(
        uint exStyle, string className, string title, uint style, int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint parameter);

    [LibraryImport("user32")]
    public static partial nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("user32")]
    public static partial int ShowWindow(nint window, int command);

    [LibraryImport("user32", SetLastError = true)]
    public static partial int DestroyWindow(nint window);

    [LibraryImport("user32")]
    public static partial int GetMessageW(out Msg message, nint window, uint min, uint max);

    [LibraryImport("user32")]
    public static partial int PeekMessageW(out Msg message, nint window, uint min, uint max, uint remove);

    [LibraryImport("user32")]
    public static partial int TranslateMessage(in Msg message);

    [LibraryImport("user32")]
    public static partial nint DispatchMessageW(in Msg message);

    [LibraryImport("user32", SetLastError = true)]
    public static partial int PostThreadMessageW(uint threadId, uint message, nint wParam, nint lParam);

    [LibraryImport("user32")]
    public static partial int PostMessageW(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("user32")]
    public static partial int GetClientRect(nint window, out React.Desktop.Interop.Rect rect);

    [LibraryImport("user32")]
    public static partial nint SetWindowLongPtrW(nint window, int index, nint value);

    [LibraryImport("user32")]
    public static partial nint GetWindowLongPtrW(nint window, int index);

    [LibraryImport("kernel32")]
    public static partial uint GetCurrentThreadId();

    [LibraryImport("kernel32")]
    public static partial nint GetModuleHandleW(nint name);
}
