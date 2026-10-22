using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

// Two hidden windows in the caller's process; only one is a Spar application window.
public sealed class WindowSelectionFixture : IDisposable
{
    private readonly WindowProcedure _procedure = DefWindowProc;
    private readonly IntPtr _instance = GetModuleHandle(null);
    private readonly string _applicationClass = "Sotsera.Spar." + Guid.NewGuid().ToString("N");
    private readonly string _decoyClass = "Spar.Test.Decoy." + Guid.NewGuid().ToString("N");

    public IntPtr Application { get; private set; }
    public IntPtr Decoy { get; private set; }

    public WindowSelectionFixture()
    {
        try
        {
            Application = Create(_applicationClass);
            Decoy = Create(_decoyClass);
            // Put the decoy first in enumeration, independently of creation timing.
            if (!SetWindowPos(Decoy, IntPtr.Zero, 0, 0, 0, 0, 0x13))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (Decoy != IntPtr.Zero)
        {
            if (!DestroyWindow(Decoy))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            Decoy = IntPtr.Zero;
        }

        if (Application != IntPtr.Zero)
        {
            if (!DestroyWindow(Application))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            Application = IntPtr.Zero;
        }

        UnregisterClass(_decoyClass, _instance);
        UnregisterClass(_applicationClass, _instance);
        GC.KeepAlive(_procedure);
    }

    private IntPtr Create(string className)
    {
        var definition = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            Procedure = Marshal.GetFunctionPointerForDelegate(_procedure),
            Instance = _instance,
            ClassName = className
        };
        if (RegisterClassEx(ref definition) == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var window = CreateWindowEx(0, className, className, 0x00CF0000, 0, 0, 100, 100,
            IntPtr.Zero, IntPtr.Zero, _instance, IntPtr.Zero);
        if (window == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return window;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size, Style;
        public IntPtr Procedure;
        public int ClassBytes, WindowBytes;
        public IntPtr Instance, Icon, Cursor, Background;
        public string MenuName, ClassName;
        public IntPtr SmallIcon;
    }

    private delegate IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WindowClass value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool UnregisterClass(string name, IntPtr instance);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
