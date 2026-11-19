// The guest helper of docs/evidence/m1b-host-details.md, compiled by Windows PowerShell's Add-Type (C# 5) inside
// Windows Sandbox only: real keyboard and mouse input through SendInput, window lookups, and screenshots. Nothing here
// runs on the host.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

public static class Guest
{
    [StructLayout(LayoutKind.Sequential)]
    struct Input { public uint Type; public InputUnion U; }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyInput Key;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MouseInput { public int X; public int Y; public uint Data; public uint Flags; public uint Time; public IntPtr Extra; }

    [StructLayout(LayoutKind.Sequential)]
    struct KeyInput { public ushort Vk; public ushort Scan; public uint Flags; public uint Time; public IntPtr Extra; }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct Point { public int X; public int Y; }

    delegate bool EnumProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")] static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr parameter);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr window, StringBuilder name, int length);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr window, StringBuilder name, int length);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr window, ref Point point);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    const uint KeyUp = 0x2;
    const uint Unicode = 0x4;

    public static void Key(ushort vk, bool up)
    {
        var input = new Input { Type = 1 };
        input.U.Key = new KeyInput { Vk = vk, Flags = up ? KeyUp : 0 };
        SendInput(1, new[] { input }, Marshal.SizeOf(typeof(Input)));
        System.Threading.Thread.Sleep(30);
    }

    public static void Tap(ushort vk) { Key(vk, false); Key(vk, true); }

    public static void Combo(ushort modifier, ushort vk) { Key(modifier, false); Tap(vk); Key(modifier, true); }

    public static void Type(string text)
    {
        foreach (var c in text)
        {
            var down = new Input { Type = 1 };
            down.U.Key = new KeyInput { Scan = c, Flags = Unicode };
            var up = new Input { Type = 1 };
            up.U.Key = new KeyInput { Scan = c, Flags = Unicode | KeyUp };
            SendInput(2, new[] { down, up }, Marshal.SizeOf(typeof(Input)));
            System.Threading.Thread.Sleep(40);
        }
    }

    static void Mouse(int x, int y, uint flags)
    {
        var input = new Input { Type = 0 };
        var width = GetSystemMetrics(0) - 1;
        var height = GetSystemMetrics(1) - 1;
        input.U.Mouse = new MouseInput { X = x * 65535 / width, Y = y * 65535 / height, Flags = flags | 0x8000 | 0x1 };
        SendInput(1, new[] { input }, Marshal.SizeOf(typeof(Input)));
        System.Threading.Thread.Sleep(20);
    }

    public static void Click(int x, int y)
    {
        Mouse(x, y, 0);
        Mouse(x, y, 0x2);
        Mouse(x, y, 0x4);
    }

    public static void Drag(int x1, int y1, int x2, int y2)
    {
        Mouse(x1, y1, 0);
        Mouse(x1, y1, 0x2);
        for (var step = 1; step <= 20; step++)
        {
            Mouse(x1 + ((x2 - x1) * step / 20), y1 + ((y2 - y1) * step / 20), 0);
        }

        Mouse(x2, y2, 0x4);
    }

    /// <summary>The visible, unowned top-level window of a process.</summary>
    public static IntPtr MainWindow(int process)
    {
        var found = IntPtr.Zero;
        EnumWindows(delegate (IntPtr window, IntPtr parameter)
        {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner == process && IsWindowVisible(window) && GetWindow(window, 4) == IntPtr.Zero && ClassOf(window) != "#32770")
            {
                found = window;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>A visible dialog (#32770) of a process, the hung-page dialog.</summary>
    public static IntPtr Dialog(int process)
    {
        var found = IntPtr.Zero;
        EnumWindows(delegate (IntPtr window, IntPtr parameter)
        {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner == process && IsWindowVisible(window) && ClassOf(window) == "#32770")
            {
                found = window;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>Visible top-level windows of the WebView2 processes (popups such as a select's list), with their rectangles.</summary>
    public static string Popups()
    {
        var rows = new List<string>();
        EnumWindows(delegate (IntPtr window, IntPtr parameter)
        {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (!IsWindowVisible(window))
            {
                return true;
            }

            try
            {
                using (var process = Process.GetProcessById((int)owner))
                {
                    if (process.ProcessName == "msedgewebview2")
                    {
                        Rect rect;
                        GetWindowRect(window, out rect);
                        rows.Add(string.Format("{0} {1},{2} {3}x{4}", ClassOf(window), rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top));
                    }
                }
            }
            catch (ArgumentException)
            {
            }

            return true;
        }, IntPtr.Zero);
        return rows.Count == 0 ? "none" : string.Join("; ", rows.ToArray());
    }

    public static string Describe(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            return "none";
        }

        uint owner;
        GetWindowThreadProcessId(window, out owner);
        var text = new StringBuilder(256);
        GetWindowText(window, text, 256);
        return string.Format("0x{0:X} {1} '{2}' pid {3}", window.ToInt64(), ClassOf(window), text, owner);
    }

    public static string ClassOf(IntPtr window)
    {
        var name = new StringBuilder(256);
        GetClassName(window, name, 256);
        return name.ToString();
    }

    public static Rect WindowRect(IntPtr window)
    {
        Rect rect;
        GetWindowRect(window, out rect);
        return rect;
    }

    public static int[] ClientOrigin(IntPtr window)
    {
        var point = new Point();
        ClientToScreen(window, ref point);
        return new[] { point.X, point.Y };
    }

    public static void Close(IntPtr window) { PostMessage(window, 0x10, IntPtr.Zero, IntPtr.Zero); }

    /// <summary>A system command, as the caption buttons and the taskbar send it (SC_MINIMIZE 0xF020).</summary>
    public static void SystemCommand(IntPtr window, int command) { PostMessage(window, 0x112, new IntPtr(command), IntPtr.Zero); }

    public static void Shot(string path)
    {
        var width = GetSystemMetrics(0);
        var height = GetSystemMetrics(1);
        using (var bitmap = new Bitmap(width, height))
        {
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(0, 0, 0, 0, new Size(width, height));
            }

            bitmap.Save(path, ImageFormat.Png);
        }
    }
}
