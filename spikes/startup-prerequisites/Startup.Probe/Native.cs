using System.Runtime.InteropServices;
using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("windows")]

namespace Startup.Probe;

// Win32, GDI and DWM imports (winuser.h, wingdi.h, dwmapi.h; SDK 10.0.26100).
internal static unsafe partial class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;

        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;

        public override readonly string ToString() => $"[{Left},{Top},{Right},{Bottom}]";
    }

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumWindows(delegate* unmanaged<nint, nint, int> callback, nint data);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint hwnd);

    [LibraryImport("user32")]
    public static partial uint GetWindowThreadProcessId(nint hwnd, out uint pid);

    [LibraryImport("user32", EntryPoint = "GetClassNameW")]
    public static partial int GetClassName(nint hwnd, char* buffer, int count);

    [LibraryImport("user32", EntryPoint = "GetWindowTextW")]
    public static partial int GetWindowText(nint hwnd, char* buffer, int count);

    [LibraryImport("user32")]
    public static partial int GetSystemMetrics(int index);

    [LibraryImport("user32")]
    public static partial nint GetDC(nint hwnd);

    [LibraryImport("user32")]
    public static partial int ReleaseDC(nint hwnd, nint dc);

    [LibraryImport("user32")]
    public static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32", EntryPoint = "SendMessageTimeoutW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint SendMessageTimeout(nint hwnd, uint msg, nint wParam, string lParam, uint flags, uint timeout, out nint result);

    [LibraryImport("dwmapi")]
    public static partial int DwmGetWindowAttribute(nint hwnd, int attribute, Rect* value, int size);

    [LibraryImport("gdi32")]
    public static partial nint CreateCompatibleDC(nint dc);

    [LibraryImport("gdi32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteDC(nint dc);

    [LibraryImport("gdi32")]
    public static partial nint SelectObject(nint dc, nint obj);

    [LibraryImport("gdi32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(nint obj);

    [LibraryImport("gdi32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BitBlt(nint dest, int x, int y, int w, int h, nint src, int sx, int sy, uint rop);

    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapInfoHeader
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

    [LibraryImport("gdi32")]
    public static partial nint CreateDIBSection(nint dc, in BitmapInfoHeader header, uint usage, out nint bits, nint section, uint offset);

    [LibraryImport("ole32")]
    public static partial int CoInitializeEx(nint reserved, uint mode);

    [LibraryImport("ole32")]
    public static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint result);

    [LibraryImport("oleaut32")]
    public static partial void SysFreeString(nint bstr);
}
