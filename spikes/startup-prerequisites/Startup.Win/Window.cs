using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Startup.Win;

// A plain top-level window for the controller stage (shown at once: a controller under a hidden parent never renders).
internal static unsafe class Window
{
    private const string ClassName = "SparStartupSpike";

    public static nint Create(string title)
    {
        var instance = Native.GetModuleHandle(0);
        fixed (char* name = ClassName)
        {
            var wc = new Native.WndClassEx
            {
                Size = (uint)sizeof(Native.WndClassEx),
                WndProc = &WndProc,
                Instance = instance,
                Cursor = Native.LoadCursor(0, 32512), // IDC_ARROW
                Background = 6, // COLOR_WINDOW + 1
                ClassName = name,
            };
            Native.RegisterClassEx(&wc);
        }

        const uint WsOverlappedWindow = 0x00CF0000;
        var hwnd = Native.CreateWindowEx(0, ClassName, title, WsOverlappedWindow, 120, 120, 800, 500, 0, 0, instance, 0);
        Native.ShowWindow(hwnd, 5); // SW_SHOW
        return hwnd;
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam) => Native.DefWindowProc(hwnd, msg, wParam, lParam);
}
