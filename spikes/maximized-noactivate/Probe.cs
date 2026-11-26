#:property AllowUnsafeBlocks=true
#:property PublishAot=false

// Spike (M2b follow-up): can a window come back maximized without being activated? Each mode creates one top-level
// window on a desktop of this process's own (CreateDesktop, never switched to: nothing reaches the monitor), shows it,
// pumps messages for 600 ms, and reports whether it is maximized (IsZoomed), whether it was activated (WM_ACTIVATE with
// WA_ACTIVE / WA_CLICKACTIVE, and GetActiveWindow on its thread), and its rectangle against the monitor's work area.
//   dotnet run Probe.cs -- <mode>...
// Modes: control-showmaximized (SW_SHOWMAXIMIZED), control-noactivate (SW_SHOWNOACTIVATE, normal),
// created-maximized-noactivate (WS_MAXIMIZE at creation, then SW_SHOWNOACTIVATE), placement-maximized
// (SetWindowPlacement with SW_SHOWMAXIMIZED before the first show), noactivate-then-maximize (SW_SHOWNOACTIVATE, then
// ShowWindow(SW_MAXIMIZE)), noactivate-then-setwindowpos (SW_SHOWNOACTIVATE, then the work area via SetWindowPos with
// SWP_NOACTIVATE: the maximized look, not the state).
using System.Runtime.InteropServices;

var modes = args.Length > 0 ? args : ["control-showmaximized", "control-noactivate", "created-maximized-noactivate", "placement-maximized", "noactivate-then-maximize", "noactivate-then-setwindowpos", "style-maximized-showna", "placement-then-style-showna"];
var desktop = Native.CreateDesktopW("spar-spike-max-" + Environment.ProcessId, 0, 0, 0, 0x10000000, 0);
if (desktop == 0)
{
    Console.WriteLine($"CreateDesktop failed ({Marshal.GetLastPInvokeError()})");
    return 1;
}

foreach (var mode in modes)
{
    string result = "";
    var thread = new Thread(() => result = Probe.Run(desktop, mode));
    thread.Start();
    thread.Join();
    Console.WriteLine(result);
}

Native.CloseDesktop(desktop);
return 0;

static unsafe class Probe
{
    private static int s_activations;

    public static string Run(nint desktop, string mode)
    {
        if (!Native.SetThreadDesktop(desktop))
        {
            return $"{mode}: SetThreadDesktop failed ({Marshal.GetLastPInvokeError()})";
        }

        s_activations = 0;
        var name = "SparSpikeMax" + mode.GetHashCode().ToString("x8");
        fixed (char* className = name)
        {
            var wc = new Native.WNDCLASSEXW
            {
                cbSize = (uint)sizeof(Native.WNDCLASSEXW),
                lpfnWndProc = &WndProc,
                hInstance = Native.GetModuleHandleW(0),
                lpszClassName = className,
            };
            Native.RegisterClassExW(&wc);

            const uint Overlapped = 0x00CF0000;
            const uint Maximize = 0x01000000;
            var style = mode == "created-maximized-noactivate" ? Overlapped | Maximize : Overlapped;
            const int UseDefault = unchecked((int)0x80000000);
            var window = Native.CreateWindowExW(0, className, className, style, 200, 150, 800, 600, 0, 0, wc.hInstance, 0);
            if (window == 0)
            {
                return $"{mode}: CreateWindowEx failed ({Marshal.GetLastPInvokeError()})";
            }

            _ = UseDefault;
            switch (mode)
            {
                case "control-showmaximized":
                    Native.ShowWindow(window, 3);
                    break;
                case "control-noactivate":
                case "created-maximized-noactivate":
                    Native.ShowWindow(window, 4);
                    break;
                case "placement-maximized":
                    var placement = new Native.WINDOWPLACEMENT { length = (uint)sizeof(Native.WINDOWPLACEMENT) };
                    Native.GetWindowPlacement(window, &placement);
                    placement.showCmd = 3;
                    Native.SetWindowPlacement(window, &placement);
                    break;
                case "noactivate-then-maximize":
                    Native.ShowWindow(window, 4);
                    Native.ShowWindow(window, 3);
                    break;
                case "noactivate-then-setwindowpos":
                    Native.ShowWindow(window, 4);
                    var work = WorkArea(window);
                    Native.SetWindowPos(window, 0, work.left, work.top, work.right - work.left, work.bottom - work.top, 0x0010 | 0x0004);
                    break;
                case "style-maximized-showna":
                case "placement-then-style-showna":
                    if (mode == "placement-then-style-showna")
                    {
                        // The normal place first, without showing (SW_HIDE), so Restore has it.
                        var normal = new Native.WINDOWPLACEMENT { length = (uint)sizeof(Native.WINDOWPLACEMENT) };
                        Native.GetWindowPlacement(window, &normal);
                        normal.showCmd = 0;
                        normal.rcNormalPosition = new Native.RECT { left = 200, top = 150, right = 1000, bottom = 750 };
                        Native.SetWindowPlacement(window, &normal);
                    }

                    // Marked maximized while hidden, sized to the work area plus the frame (as Windows maximizes),
                    // then shown in that size and place without activation (SW_SHOWNA).
                    const int StyleIndex = -16;
                    var current = (uint)Native.GetWindowLongW(window, StyleIndex);
                    Native.SetWindowLongW(window, StyleIndex, (int)(current | Maximize));
                    var area = WorkArea(window);
                    var frame = Native.GetSystemMetrics(32) + Native.GetSystemMetrics(92);
                    Native.SetWindowPos(window, 0, area.left - frame, area.top - frame, area.right - area.left + (2 * frame), area.bottom - area.top + (2 * frame), 0x0010 | 0x0004 | 0x0020);
                    Native.ShowWindow(window, 8);
                    break;
            }

            Pump(600);
            var zoomed = Native.IsZoomed(window);
            var active = Native.GetActiveWindow() == window;
            var activations = s_activations;
            Native.RECT rect;
            Native.GetWindowRect(window, &rect);
            var workArea = WorkArea(window);
            var restored = "";
            if (mode.EndsWith("showna", StringComparison.Ordinal))
            {
                // Restore as the title bar's button does (which activates, as a click would), then read the state and
                // the place again.
                const uint SysCommand = 0x0112;
                const nint Restore = 0xF120;
                Native.SendMessageW(window, SysCommand, Restore, 0);
                Pump(300);
                Native.RECT after;
                Native.GetWindowRect(window, &after);
                restored = $" restore: maximized={Native.IsZoomed(window)} window=({after.left},{after.top})-({after.right},{after.bottom})";
            }

            Native.DestroyWindow(window);
            Pump(50);
            return $"{mode}: maximized={zoomed} activated={activations > 0 || active} (WM_ACTIVATE {activations}, GetActiveWindow {active}) "
                + $"window=({rect.left},{rect.top})-({rect.right},{rect.bottom}) work=({workArea.left},{workArea.top})-({workArea.right},{workArea.bottom})" + restored;
        }
    }

    private static Native.RECT WorkArea(nint window)
    {
        var monitor = Native.MonitorFromWindow(window, 2);
        var info = new Native.MONITORINFO { cbSize = (uint)sizeof(Native.MONITORINFO) };
        Native.GetMonitorInfoW(monitor, &info);
        return info.rcWork;
    }

    private static void Pump(int milliseconds)
    {
        var end = Environment.TickCount64 + milliseconds;
        Native.MSG message;
        while (Environment.TickCount64 < end)
        {
            while (Native.PeekMessageW(&message, 0, 0, 0, 1))
            {
                Native.TranslateMessage(&message);
                Native.DispatchMessageW(&message);
            }

            Thread.Sleep(10);
        }
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint window, uint message, nint wParam, nint lParam)
    {
        const uint Activate = 0x0006;
        if (message == Activate && (wParam & 0xFFFF) != 0)
        {
            s_activations++;
        }

        return Native.DefWindowProcW(window, message, wParam, lParam);
    }
}

static unsafe partial class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public delegate* unmanaged<nint, uint, nint, nint, nint> lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public char* lpszMenuName;
        public char* lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPLACEMENT
    {
        public uint length;
        public uint flags;
        public uint showCmd;
        public POINT ptMinPosition;
        public POINT ptMaxPosition;
        public RECT rcNormalPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public POINT pt;
    }

    [LibraryImport("user32", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    public static partial nint CreateDesktopW(string name, nint device, nint mode, uint flags, uint access, nint attributes);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseDesktop(nint desktop);

    [LibraryImport("user32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetThreadDesktop(nint desktop);

    [LibraryImport("kernel32")]
    public static partial nint GetModuleHandleW(nint name);

    [LibraryImport("user32")]
    public static partial ushort RegisterClassExW(WNDCLASSEXW* wc);

    [LibraryImport("user32", SetLastError = true)]
    public static partial nint CreateWindowExW(uint exStyle, char* className, char* title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint window, int command);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowPlacement(nint window, WINDOWPLACEMENT* placement);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPlacement(nint window, WINDOWPLACEMENT* placement);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);

    [LibraryImport("user32")]
    public static partial int GetWindowLongW(nint window, int index);

    [LibraryImport("user32")]
    public static partial int SetWindowLongW(nint window, int index, int value);

    [LibraryImport("user32")]
    public static partial int GetSystemMetrics(int index);

    [LibraryImport("user32")]
    public static partial nint SendMessageW(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsZoomed(nint window);

    [LibraryImport("user32")]
    public static partial nint GetActiveWindow();

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(nint window, RECT* rect);

    [LibraryImport("user32")]
    public static partial nint MonitorFromWindow(nint window, uint flags);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfoW(nint monitor, MONITORINFO* info);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint window);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PeekMessageW(MSG* message, nint window, uint first, uint last, uint remove);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(MSG* message);

    [LibraryImport("user32")]
    public static partial nint DispatchMessageW(MSG* message);

    [LibraryImport("user32")]
    public static partial nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);
}
