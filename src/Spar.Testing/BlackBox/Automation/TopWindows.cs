using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Spar.Testing;

/// <summary>
/// The top-level windows of one process on the calling thread's desktop, found with <c>EnumWindows</c> and
/// <c>GetWindowThreadProcessId</c>, which send no message. A UI Automation search from the desktop's root asks every
/// top-level window there, so one hung app (measured: a suspended first instance) stalls it; asking only the app's own
/// windows, by handle, keeps the fixture away from every other window.
/// </summary>
[SupportedOSPlatform("windows10.0.17763")]
internal static unsafe class TopWindows
{
    /// <summary>The class of a dialog box, which task dialogs and message boxes both use.</summary>
    public const string DialogClass = "#32770";

    public static List<TopWindow> Of(int processId)
    {
        var state = new State(processId);
        var handle = GCHandle.Alloc(state);
        try
        {
            PInvoke.EnumWindows(&Collect, (nint)GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }

        return state.Found;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static BOOL Collect(HWND window, LPARAM parameter)
    {
        var state = (State)GCHandle.FromIntPtr(parameter.Value).Target!;
        uint owner;
        PInvoke.GetWindowThreadProcessId(window, &owner);
        if (owner != (uint)state.ProcessId || !PInvoke.IsWindowVisible(window))
        {
            return true;
        }

        var buffer = stackalloc char[256];
        var length = PInvoke.GetClassName(window, buffer, 256);
        var ownerWindow = PInvoke.GetWindow(window, GET_WINDOW_CMD.GW_OWNER);
        state.Found.Add(new TopWindow((nint)window.Value, new string(buffer, 0, length), (nint)ownerWindow.Value));
        return true;
    }

    private sealed class State(int processId)
    {
        public int ProcessId { get; } = processId;

        public List<TopWindow> Found { get; } = [];
    }
}

/// <summary>A visible top-level window: its handle, its class and the window that owns it (0 when none).</summary>
internal readonly record struct TopWindow(nint Handle, string ClassName, nint Owner)
{
    public bool IsDialog => ClassName == TopWindows.DialogClass;
}
