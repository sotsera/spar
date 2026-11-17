using System.Runtime.InteropServices;
using static Spar.SingleInstance.Native;

namespace Spar.SingleInstance;

/// <summary>A plain top-level window with its own message loop, for the primary and the launcher.</summary>
internal static unsafe class AppWindow
{
    private const string ClassName = "SparSingleInstanceProbe";
    private const nuint LifetimeTimer = 1;

    private static Func<nint, uint, nuint, nint, bool>? s_handler;

    public static nint Handle { get; private set; }

    /// <summary>Creates the window, shows it with <paramref name="show"/> (or not at all when hidden) and pumps.</summary>
    public static void Run(string title, string start, int lifetimeMs, Func<nint, uint, nuint, nint, bool> handler)
    {
        s_handler = handler;
        var instance = GetModuleHandle(null);
        fixed (char* className = ClassName)
        {
            var windowClass = new WindowClass
            {
                Size = (uint)sizeof(WindowClass),
                WindowProcedure = (nint)(delegate* unmanaged<nint, uint, nuint, nint, nint>)&Procedure,
                Instance = instance,
                Background = 6, // COLOR_WINDOW + 1
                ClassName = className,
            };
            RegisterClass(&windowClass);
        }

        Handle = CreateWindow(0, ClassName, title, WsOverlappedWindow, 200, 200, 480, 320, 0, 0, instance, 0);
        var show = start switch
        {
            "minimized" => SwShowMinimized,
            "hidden" => SwHide,
            _ => SwShowNormal,
        };
        if (show != SwHide)
        {
            ShowWindow(Handle, show);
        }

        SetTimer(Handle, LifetimeTimer, (uint)lifetimeMs, 0);
        Log.Mark($"window 0x{Handle:x} '{title}' start={start}; {ForegroundDescription()}");

        Message message;
        while (GetMessage(&message, 0, 0, 0) > 0)
        {
            TranslateMessage(&message);
            DispatchMessage(&message);
        }
    }

    [UnmanagedCallersOnly]
    private static nint Procedure(nint window, uint message, nuint wParam, nint lParam)
    {
        if (message == WmTimer && wParam == LifetimeTimer)
        {
            KillTimer(window, LifetimeTimer);
            DestroyWindow(window);
            return 0;
        }

        if (message == WmDestroy)
        {
            PostQuitMessage(0);
            return 0;
        }

        if (s_handler is not null && s_handler(window, message, wParam, lParam))
        {
            return 0;
        }

        return DefWindowProc(window, message, wParam, lParam);
    }
}
