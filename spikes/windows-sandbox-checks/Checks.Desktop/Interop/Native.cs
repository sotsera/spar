using System.Runtime.InteropServices;

namespace Checks.Desktop.Interop;

// Hand-written Win32 declarations, checked against the Windows SDK 10.0.26100 headers (winuser.h, dwmapi.h,
// shellscalingapi.h, dcomp.h, uiautomationcoreapi.h). BOOL and LONG are 32-bit; every handle is pointer-sized.

[StructLayout(LayoutKind.Sequential)]
internal struct Rect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public readonly int Width => Right - Left;
    public readonly int Height => Bottom - Top;
    public readonly bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
    public override readonly string ToString() => $"[{Left},{Top},{Right},{Bottom}]";
}

// The page's rectangles in CSS pixels, kept unscaled.
internal readonly record struct CssRect(double Left, double Top, double Right, double Bottom)
{
    public bool Contains(double x, double y) => x >= Left && x < Right && y >= Top && y < Bottom;

    public Rect Scale(double s) => new()
    {
        Left = (int)Math.Floor(Left * s),
        Top = (int)Math.Floor(Top * s),
        Right = (int)Math.Ceiling(Right * s),
        Bottom = (int)Math.Ceiling(Bottom * s),
    };
}

// VARIANT on x64: vt (2) + 3 reserved words, then a 16-byte union; only VT_BOOL is used here.
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct Variant
{
    [FieldOffset(0)] public ushort Type;
    [FieldOffset(8)] public short Value;
}

[StructLayout(LayoutKind.Sequential)]
internal struct Point
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct Margins
{
    public int Left;
    public int Right;
    public int Top;
    public int Bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal struct Msg
{
    public nint Hwnd;
    public uint Message;
    public nint WParam;
    public nint LParam;
    public uint Time;
    public Point Pt;
    public uint Private;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WndClassEx
{
    public uint Size;
    public uint Style;
    public delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint> WndProc;
    public int ClsExtra;
    public int WndExtra;
    public nint Instance;
    public nint Icon;
    public nint Cursor;
    public nint Background;
    public char* MenuName;
    public char* ClassName;
    public nint IconSmall;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NcCalcSizeParams
{
    public Rect Rect0;
    public Rect Rect1;
    public Rect Rect2;
    public nint Pos;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WindowPlacement
{
    public uint Length;
    public uint Flags;
    public uint ShowCmd;
    public Point MinPosition;
    public Point MaxPosition;
    public Rect NormalPosition;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MonitorInfo
{
    public uint Size;
    public Rect Monitor;
    public Rect Work;
    public uint Flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PaintStruct
{
    public nint Hdc;
    public int Erase;
    public Rect Paint;
    public int Restore;
    public int IncUpdate;
    public long Reserved0;
    public long Reserved1;
    public long Reserved2;
    public long Reserved3;
}

[StructLayout(LayoutKind.Sequential)]
internal struct TrackMouseEventInfo
{
    public uint Size;
    public uint Flags;
    public nint Hwnd;
    public uint HoverTime;
}

// TITLEBARINFOEX: rgstate[CCHILDREN_TITLEBAR + 1] and rgrect[CCHILDREN_TITLEBAR + 1], CCHILDREN_TITLEBAR = 5.
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct TitleBarInfoEx
{
    public uint Size;
    public Rect TitleBar;
    public fixed uint State[6];
    public fixed int Rects[24];
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

// INPUT for SendInput on x64: type (4) + padding (4) + union (32: MOUSEINPUT is the largest member).
[StructLayout(LayoutKind.Explicit, Size = 40)]
internal struct Input
{
    [FieldOffset(0)] public uint Type;
    [FieldOffset(8)] public MouseInput Mouse;
    [FieldOffset(8)] public KeybdInput Keyboard;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MouseInput
{
    public int Dx;
    public int Dy;
    public uint MouseData;
    public uint Flags;
    public uint Time;
    public nint ExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct KeybdInput
{
    public ushort Vk;
    public ushort Scan;
    public uint Flags;
    public uint Time;
    public nint ExtraInfo;
}

internal static unsafe partial class Native
{
    public const uint WM_CREATE = 0x0001, WM_DESTROY = 0x0002, WM_MOVE = 0x0003, WM_SIZE = 0x0005, WM_ACTIVATE = 0x0006;
    public const uint WM_SETFOCUS = 0x0007, WM_PAINT = 0x000F, WM_CLOSE = 0x0010, WM_ERASEBKGND = 0x0014;
    public const uint WM_SETTINGCHANGE = 0x001A, WM_SETCURSOR = 0x0020, WM_GETMINMAXINFO = 0x0024;
    public const uint WM_WINDOWPOSCHANGED = 0x0047, WM_GETOBJECT = 0x003D, WM_NCCALCSIZE = 0x0083, WM_NCHITTEST = 0x0084;
    public const uint WM_NCACTIVATE = 0x0086, WM_NCMOUSEMOVE = 0x00A0, WM_NCLBUTTONDOWN = 0x00A1, WM_NCLBUTTONUP = 0x00A2;
    public const uint WM_NCLBUTTONDBLCLK = 0x00A3, WM_NCRBUTTONDOWN = 0x00A4, WM_NCRBUTTONUP = 0x00A5;
    public const uint WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104, WM_SYSCOMMAND = 0x0112, WM_TIMER = 0x0113;
    public const uint WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_MOUSEWHEEL = 0x020A, WM_MOUSEHWHEEL = 0x020E;
    public const uint WM_XBUTTONDBLCLK = 0x020D, WM_ENTERSIZEMOVE = 0x0231, WM_EXITSIZEMOVE = 0x0232;
    public const uint WM_NCMOUSELEAVE = 0x02A2, WM_MOUSELEAVE = 0x02A3, WM_DPICHANGED = 0x02E0, WM_GETTITLEBARINFOEX = 0x033F;
    public const uint WM_APP = 0x8000;

    public const uint WS_OVERLAPPEDWINDOW = 0x00CF0000, WS_CAPTION = 0x00C00000, WS_CHILD = 0x40000000;
    public const uint WS_VISIBLE = 0x10000000, WS_CLIPCHILDREN = 0x02000000, WS_CLIPSIBLINGS = 0x04000000;
    public const uint WS_SYSMENU = 0x00080000, WS_MINIMIZEBOX = 0x00020000, WS_MAXIMIZEBOX = 0x00010000;
    public const uint STATE_SYSTEM_INVISIBLE = 0x8000, STATE_SYSTEM_OFFSCREEN = 0x10000;
    public const int OBJID_TITLEBAR = -2, OBJID_CLIENT = -4;
    public const uint WS_EX_NOREDIRECTIONBITMAP = 0x00200000, WS_EX_LAYERED = 0x00080000, WS_EX_NOACTIVATE = 0x08000000;

    public const int HTERROR = -2, HTTRANSPARENT = -1, HTNOWHERE = 0, HTCLIENT = 1, HTCAPTION = 2, HTSYSMENU = 3;
    public const int HTMINBUTTON = 8, HTMAXBUTTON = 9, HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13;
    public const int HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17, HTCLOSE = 20;

    public const int SW_HIDE = 0, SW_SHOWNORMAL = 1, SW_SHOWMINIMIZED = 2, SW_MAXIMIZE = 3, SW_SHOWNOACTIVATE = 4;
    public const int SW_SHOW = 5, SW_MINIMIZE = 6, SW_RESTORE = 9;
    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
    public const uint SWP_FRAMECHANGED = 0x20, SWP_SHOWWINDOW = 0x40;
    public const uint SC_SIZE = 0xF000, SC_MOVE = 0xF010, SC_MINIMIZE = 0xF020, SC_MAXIMIZE = 0xF030;
    public const uint SC_CLOSE = 0xF060, SC_KEYMENU = 0xF100, SC_RESTORE = 0xF120;
    public const int SM_CXSIZEFRAME = 32, SM_CYSIZEFRAME = 33, SM_CXPADDEDBORDER = 92, SM_CYCAPTION = 4;
    public const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const uint DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_CAPTION_BUTTON_BOUNDS = 5, DWMWA_CLOAK = 13;
    public const uint MONITOR_DEFAULTTONEAREST = 2, TME_LEAVE = 0x2, TME_NONCLIENT = 0x10;
    public const uint CWP_SKIPINVISIBLE = 0x1, CWP_SKIPTRANSPARENT = 0x4, PW_RENDERFULLCONTENT = 0x2;
    public const uint TPM_RETURNCMD = 0x0100, TPM_RIGHTBUTTON = 0x2;
    public const nint IDC_ARROW = 32512;

    [LibraryImport("user32", EntryPoint = "RegisterClassExW")]
    public static partial ushort RegisterClassEx(in WndClassEx wc);

    [LibraryImport("user32", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWindowEx(uint exStyle, string className, string? windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32", EntryPoint = "DefWindowProcW")]
    public static partial nint DefWindowProc(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32", EntryPoint = "GetMessageW")]
    public static partial int GetMessage(out Msg msg, nint hwnd, uint min, uint max);

    [LibraryImport("user32")]
    public static partial int TranslateMessage(in Msg msg);

    [LibraryImport("user32", EntryPoint = "DispatchMessageW")]
    public static partial nint DispatchMessage(in Msg msg);

    [LibraryImport("user32")]
    public static partial void PostQuitMessage(int code);

    [LibraryImport("user32")]
    public static partial int ShowWindow(nint hwnd, int cmd);

    [LibraryImport("user32")]
    public static partial int SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32")]
    public static partial int GetWindowRect(nint hwnd, out Rect rect);

    [LibraryImport("user32")]
    public static partial int GetClientRect(nint hwnd, out Rect rect);

    [LibraryImport("user32")]
    public static partial int DestroyWindow(nint hwnd);

    [LibraryImport("user32", EntryPoint = "PostMessageW")]
    public static partial int PostMessage(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32", EntryPoint = "SendMessageW")]
    public static partial nint SendMessage(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32", EntryPoint = "SendMessageTimeoutW")]
    public static partial nint SendMessageTimeout(nint hwnd, uint msg, nint wParam, nint lParam, uint flags,
        uint timeout, out nint result);

    [LibraryImport("user32", EntryPoint = "LoadCursorW")]
    public static partial nint LoadCursor(nint instance, nint name);

    [LibraryImport("user32")]
    public static partial nint SetCursor(nint cursor);

    [LibraryImport("user32")]
    public static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32")]
    public static partial int GetSystemMetricsForDpi(int index, uint dpi);

    [LibraryImport("user32")]
    public static partial int IsZoomed(nint hwnd);

    [LibraryImport("user32")]
    public static partial int IsIconic(nint hwnd);

    [LibraryImport("user32")]
    public static partial int ScreenToClient(nint hwnd, ref Point point);

    [LibraryImport("user32")]
    public static partial int ClientToScreen(nint hwnd, ref Point point);

    [LibraryImport("user32")]
    public static partial nint ChildWindowFromPointEx(nint parent, Point point, uint flags);

    [LibraryImport("user32")]
    public static partial nint WindowFromPoint(Point point);

    [LibraryImport("user32")]
    public static partial int EnumChildWindows(nint parent, delegate* unmanaged[Stdcall]<nint, nint, int> proc, nint param);

    [LibraryImport("user32", EntryPoint = "GetClassNameW")]
    public static partial int GetClassName(nint hwnd, char* buffer, int max);

    [LibraryImport("user32", EntryPoint = "GetWindowTextW")]
    public static partial int GetWindowText(nint hwnd, char* buffer, int max);

    [LibraryImport("user32", EntryPoint = "SetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SetWindowText(nint hwnd, string text);

    [LibraryImport("user32")]
    public static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [LibraryImport("user32", EntryPoint = "GetWindowLongPtrW")]
    public static partial nint GetWindowLongPtr(nint hwnd, int index);

    [LibraryImport("user32")]
    public static partial nint GetParent(nint hwnd);

    [LibraryImport("user32")]
    public static partial int IsWindowVisible(nint hwnd);

    [LibraryImport("user32")]
    public static partial int TrackMouseEvent(ref TrackMouseEventInfo info);

    [LibraryImport("user32")]
    public static partial nint GetSystemMenu(nint hwnd, int revert);

    [LibraryImport("user32")]
    public static partial nint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint hwnd, nint rect);

    [LibraryImport("user32")]
    public static partial nuint SetTimer(nint hwnd, nuint id, uint elapse, nint proc);

    [LibraryImport("user32")]
    public static partial int KillTimer(nint hwnd, nuint id);

    [LibraryImport("user32")]
    public static partial nint MonitorFromWindow(nint hwnd, uint flags);

    [LibraryImport("user32")]
    public static partial nint MonitorFromPoint(Point point, uint flags);

    [LibraryImport("user32", EntryPoint = "GetMonitorInfoW")]
    public static partial int GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [LibraryImport("user32")]
    public static partial int EnumDisplayMonitors(nint hdc, nint clip, delegate* unmanaged[Stdcall]<nint, nint, Rect*, nint, int> proc, nint data);

    [LibraryImport("user32")]
    public static partial int GetWindowPlacement(nint hwnd, ref WindowPlacement placement);

    [LibraryImport("user32")]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32")]
    public static partial int SetForegroundWindow(nint hwnd);

    [LibraryImport("user32")]
    public static partial nint GetFocus();

    [LibraryImport("user32")]
    public static partial nint SetCapture(nint hwnd);

    [LibraryImport("user32")]
    public static partial int ReleaseCapture();

    [LibraryImport("user32")]
    public static partial nint GetCapture();

    [LibraryImport("user32")]
    public static partial int SetWindowRgn(nint hwnd, nint region, int redraw);

    [LibraryImport("gdi32")]
    public static partial nint CreateRectRgn(int left, int top, int right, int bottom);

    [LibraryImport("gdi32")]
    public static partial int CombineRgn(nint dest, nint src1, nint src2, int mode);

    [LibraryImport("user32")]
    public static partial int EnumWindows(delegate* unmanaged[Stdcall]<nint, nint, int> proc, nint param);

    [LibraryImport("user32")]
    public static partial uint SendInput(uint count, Input* inputs, int size);

    [LibraryImport("user32")]
    public static partial int GetCursorPos(out Point point);

    [LibraryImport("user32")]
    public static partial int SetCursorPos(int x, int y);

    [LibraryImport("user32")]
    public static partial nint BeginPaint(nint hwnd, out PaintStruct paint);

    [LibraryImport("user32")]
    public static partial int EndPaint(nint hwnd, in PaintStruct paint);

    [LibraryImport("user32")]
    public static partial int FillRect(nint hdc, in Rect rect, nint brush);

    [LibraryImport("user32")]
    public static partial int InvalidateRect(nint hwnd, nint rect, int erase);

    [LibraryImport("user32")]
    public static partial int UpdateWindow(nint hwnd);

    [LibraryImport("user32")]
    public static partial int PrintWindow(nint hwnd, nint hdc, uint flags);

    [LibraryImport("user32")]
    public static partial nint GetDC(nint hwnd);

    [LibraryImport("user32")]
    public static partial int ReleaseDC(nint hwnd, nint hdc);

    [LibraryImport("user32")]
    public static partial int GetDoubleClickTime();

    [LibraryImport("user32")]
    public static partial nint GetAncestor(nint hwnd, uint flags);

    [LibraryImport("user32")]
    public static partial int IsWindow(nint hwnd);

    [LibraryImport("gdi32")]
    public static partial nint CreateSolidBrush(uint colorRef);

    [LibraryImport("gdi32")]
    public static partial int DeleteObject(nint obj);

    [LibraryImport("gdi32")]
    public static partial nint CreateCompatibleDC(nint hdc);

    [LibraryImport("gdi32")]
    public static partial int DeleteDC(nint hdc);

    [LibraryImport("gdi32")]
    public static partial nint SelectObject(nint hdc, nint obj);

    [LibraryImport("gdi32")]
    public static partial nint CreateDIBSection(nint hdc, in BitmapInfoHeader header, uint usage, out nint bits,
        nint section, uint offset);

    [LibraryImport("gdi32")]
    public static partial int BitBlt(nint dest, int x, int y, int cx, int cy, nint src, int x1, int y1, uint rop);

    [LibraryImport("gdi32")]
    public static partial nint GetStockObject(int index);

    [LibraryImport("dwmapi")]
    public static partial int DwmExtendFrameIntoClientArea(nint hwnd, in Margins margins);

    [LibraryImport("dwmapi")]
    public static partial int DwmSetWindowAttribute(nint hwnd, uint attribute, void* value, uint size);

    [LibraryImport("dwmapi")]
    public static partial int DwmGetWindowAttribute(nint hwnd, uint attribute, void* value, uint size);

    [LibraryImport("dwmapi")]
    public static partial int DwmDefWindowProc(nint hwnd, uint msg, nint wParam, nint lParam, out nint result);

    [LibraryImport("dwmapi")]
    public static partial int DwmFlush();

    [LibraryImport("shcore")]
    public static partial int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);

    [LibraryImport("ole32")]
    public static partial int OleInitialize(nint reserved);

    [LibraryImport("ole32")]
    public static partial void OleUninitialize();

    [LibraryImport("kernel32", EntryPoint = "GetModuleHandleW")]
    public static partial nint GetModuleHandle(nint name);

    [LibraryImport("kernel32")]
    public static partial uint GetCurrentThreadId();

    [LibraryImport("kernel32")]
    public static partial uint GetCurrentProcessId();

    [LibraryImport("dcomp")]
    public static partial int DCompositionCreateDevice(nint dxgiDevice, in Guid iid, out nint device);

    [LibraryImport("uiautomationcore")]
    public static partial nint UiaReturnRawElementProvider(nint hwnd, nint wParam, nint lParam, nint provider);

    [LibraryImport("uiautomationcore")]
    public static partial int UiaHostProviderFromHwnd(nint hwnd, out nint provider);

    [LibraryImport("user32", EntryPoint = "SetWindowLongPtrW")]
    public static partial nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [LibraryImport("user32", EntryPoint = "CallWindowProcW")]
    public static partial nint CallWindowProc(nint previous, nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("ole32")]
    public static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint result);

    [LibraryImport("user32", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    public static partial int SystemParametersInfo(uint action, int param, nint value, uint flags);

    [LibraryImport("user32")]
    public static partial int GetSystemMetrics(int index);

    [LibraryImport("user32")]
    public static partial int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);

    [LibraryImport("user32")]
    public static partial int QueryDisplayConfig(uint flags, ref uint paths, byte* pathArray, ref uint modes, byte* modeArray, nint topology);

    [LibraryImport("user32")]
    public static partial int DisplayConfigGetDeviceInfo(byte* packet);

    [LibraryImport("user32")]
    public static partial int DisplayConfigSetDeviceInfo(byte* packet);

    [LibraryImport("shlwapi")]
    public static partial nint SHCreateMemStream(byte* initial, uint size);

    public static int LoWord(nint value) => (short)((long)value & 0xFFFF);
    public static int HiWord(nint value) => (short)(((long)value >> 16) & 0xFFFF);
    public static nint MakeLParam(int lo, int hi) => (nint)((hi << 16) | (lo & 0xFFFF));
    public static uint Rgb(int rgb) => (uint)(((rgb >> 16) & 0xFF) | (rgb & 0xFF00) | ((rgb & 0xFF) << 16));

    public static string ClassNameOf(nint hwnd)
    {
        var buffer = stackalloc char[256];
        var length = GetClassName(hwnd, buffer, 256);
        return new string(buffer, 0, length);
    }

    public static string TextOf(nint hwnd)
    {
        var buffer = stackalloc char[256];
        var length = GetWindowText(hwnd, buffer, 256);
        return new string(buffer, 0, length);
    }
}
