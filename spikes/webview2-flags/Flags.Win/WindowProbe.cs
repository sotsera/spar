using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace WebView2Flags;

/// <summary>Lists the windows on the child's desktop (top-level and their children) and captures the host window.</summary>
internal static unsafe class WindowProbe
{
    /// <summary>Every window on this thread's desktop: handle, owner process, class, title, visible, rectangle, depth.</summary>
    public static JsonArray Snapshot()
    {
        var list = new List<(nint Hwnd, int Depth)>();
        var top = new List<nint>();
        var handle = GCHandle.Alloc(top);
        try
        {
            Native.EnumDesktopWindows(Native.GetThreadDesktop(Native.GetCurrentThreadId()), &Collect, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }

        foreach (var hwnd in top)
        {
            list.Add((hwnd, 0));
            var children = new List<nint>();
            var h = GCHandle.Alloc(children);
            try
            {
                Native.EnumChildWindows(hwnd, &Collect, GCHandle.ToIntPtr(h));
            }
            finally
            {
                h.Free();
            }

            list.AddRange(children.Select(c => (c, 1)));
        }

        var array = new JsonArray();
        var cls = stackalloc char[256];
        var title = stackalloc char[256];
        foreach (var (hwnd, depth) in list)
        {
            Native.GetWindowThreadProcessId(hwnd, out var pid);
            Native.GetWindowRect(hwnd, out var r);
            var cl = Native.GetClassNameW(hwnd, cls, 256);
            var tl = Native.GetWindowTextW(hwnd, title, 256);
            array.Add(new JsonObject
            {
                ["hwnd"] = $"0x{hwnd:X}",
                ["depth"] = depth,
                ["pid"] = pid,
                ["class"] = new string(cls, 0, cl),
                ["title"] = new string(title, 0, tl),
                ["visible"] = Native.IsWindowVisible(hwnd),
                ["rect"] = $"{r.Left},{r.Top},{r.Right},{r.Bottom}",
            });
        }

        return array;
    }

    /// <summary>The visible windows of <paramref name="after"/> that <paramref name="before"/> lacked or had hidden.</summary>
    public static JsonArray NewlyVisible(JsonArray before, JsonArray after)
    {
        var shown = before.Where(w => (bool)w!["visible"]!).Select(w => (string)w!["hwnd"]!).ToHashSet();
        var result = new JsonArray();
        foreach (var w in after.Where(w => (bool)w!["visible"]! && !shown.Contains((string)w!["hwnd"]!)))
        {
            result.Add(w!.DeepClone());
        }

        return result;
    }

    /// <summary>Captures <paramref name="hwnd"/> with PrintWindow (full content) into a 32-bit bottom-up BMP.</summary>
    public static bool Capture(nint hwnd, string path)
    {
        Native.GetWindowRect(hwnd, out var r);
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        var screen = Native.GetDC(0);
        var mem = Native.CreateCompatibleDC(screen);
        var header = new byte[40];
        BitConverter.TryWriteBytes(header.AsSpan(0), 40);
        BitConverter.TryWriteBytes(header.AsSpan(4), w);
        BitConverter.TryWriteBytes(header.AsSpan(8), h);
        BitConverter.TryWriteBytes(header.AsSpan(12), (short)1);
        BitConverter.TryWriteBytes(header.AsSpan(14), (short)32);
        nint bitmap;
        nint bits;
        fixed (byte* bmi = header)
        {
            bitmap = Native.CreateDIBSection(mem, bmi, 0, out bits, 0, 0);
        }

        var old = Native.SelectObject(mem, bitmap);
        var ok = Native.PrintWindow(hwnd, mem, Native.PW_RENDERFULLCONTENT);
        var size = w * h * 4;
        using (var file = File.Create(path))
        {
            var fileHeader = new byte[14];
            fileHeader[0] = (byte)'B';
            fileHeader[1] = (byte)'M';
            BitConverter.TryWriteBytes(fileHeader.AsSpan(2), 54 + size);
            BitConverter.TryWriteBytes(fileHeader.AsSpan(10), 54);
            file.Write(fileHeader);
            file.Write(header);
            file.Write(new ReadOnlySpan<byte>((void*)bits, size));
        }

        Native.SelectObject(mem, old);
        Native.DeleteObject(bitmap);
        Native.DeleteDC(mem);
        Native.ReleaseDC(0, screen);
        return ok;
    }

    [UnmanagedCallersOnly]
    private static int Collect(nint hwnd, nint param)
    {
        ((List<nint>)GCHandle.FromIntPtr(param).Target!).Add(hwnd);
        return 1;
    }
}
