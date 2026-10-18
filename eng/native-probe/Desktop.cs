using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

// Test-only Win32 desktop inspection. No dependency on Spar internals or a browser debug port.
public static class NativeProbeDesktop
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ClrUsed, ClrImportant;
    }

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr window, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    private static extern bool PostMessage(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines,
        byte[] bits, ref BitmapInfo info, uint usage);

    public static void Close(IntPtr window)
    {
        if (!PostMessage(window, 0x10, UIntPtr.Zero, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    // Returns the number of the sample's green accent pixels. A navigation event cannot satisfy this check.
    public static int Capture(IntPtr window, string path, int maximumAccentLeft)
    {
        return CaptureColor(window, path, maximumAccentLeft, 112, 227, 189);
    }

    public static int CaptureColor(IntPtr window, string path, int maximumAccentLeft, byte red, byte green, byte blue)
    {
        Rect rect;
        if (!GetWindowRect(window, out rect))
        {
            throw new InvalidOperationException("No native window to capture.");
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        var dc = GetWindowDC(window);
        var memory = CreateCompatibleDC(dc);
        var bitmap = CreateCompatibleBitmap(dc, width, height);
        var previous = SelectObject(memory, bitmap);
        try
        {
            if (!PrintWindow(window, memory, 2))
            {
                throw new InvalidOperationException("PrintWindow could not capture the actual desktop window.");
            }

            SelectObject(memory, previous);
            var stride = (width * 3 + 3) & ~3;
            var pixels = new byte[stride * height];
            var info = new BitmapInfo { Size = 40, Width = width, Height = height, Planes = 1, BitCount = 24 };
            if (GetDIBits(dc, bitmap, 0, (uint)height, pixels, ref info, 0) != height)
            {
                throw new InvalidOperationException("Could not read rendered window pixels.");
            }

            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write((ushort)0x4D42);
                writer.Write(54 + pixels.Length);
                writer.Write(0);
                writer.Write(54);
                writer.Write(40);
                writer.Write(width);
                writer.Write(height);
                writer.Write((ushort)1);
                writer.Write((ushort)24);
                writer.Write(0);
                writer.Write(pixels.Length);
                writer.Write(new byte[16]);
                writer.Write(pixels);
            }

            var matching = 0;
            var accentLeft = width;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var offset = y * stride + x * 3;
                    if (pixels[offset] == blue && pixels[offset + 1] == green && pixels[offset + 2] == red)
                    {
                        matching++;
                        accentLeft = Math.Min(accentLeft, x);
                    }
                }
            }

            // The smaller layout moves the logo/button left. Reject a clipped frame from the old viewport.
            return accentLeft <= maximumAccentLeft ? matching : 0;
        }
        finally
        {
            SelectObject(memory, previous);
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(window, dc);
        }
    }
}
