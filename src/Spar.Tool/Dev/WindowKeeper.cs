using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Spar.Tool.Dev;

/// <summary>
/// The running app's window, read before a restart (decision "M2b's decide-first questions", item 4): its place and
/// size in physical pixels, for <c>SPAR_DEV_WINDOW</c> (<see cref="DevSupervisor.KeptWindowVariable"/>), which a
/// Development build's Windows host takes for its first window, with <c>,maximized</c> and the normal place when it is
/// maximized. Only a visible, unowned, not minimized top-level window of the process counts; none gives null.
/// </summary>
[SupportedOSPlatform("windows10.0.17763")]
internal static unsafe class WindowKeeper
{
    public static string? Read(int processId)
    {
        var search = new Search(processId);
        var handle = GCHandle.Alloc(search);
        try
        {
            PInvoke.EnumWindows(&Visit, (LPARAM)GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }

        if (search.Found.IsNull)
        {
            return null;
        }

        if (PInvoke.IsZoomed(search.Found))
        {
            return Maximized(search.Found);
        }

        RECT bounds;
        if (!PInvoke.GetWindowRect(search.Found, &bounds))
        {
            return null;
        }

        return Text(bounds, "");
    }

    /// <summary>
    /// A maximized window: its normal place (the placement's, in workspace coordinates, moved to the screen's by the
    /// work area's corner) and <c>maximized</c>, which the host shows maximized without the focus.
    /// </summary>
    private static string? Maximized(HWND window)
    {
        var placement = new WINDOWPLACEMENT { length = (uint)sizeof(WINDOWPLACEMENT) };
        var monitor = PInvoke.MonitorFromWindow(window, Windows.Win32.Graphics.Gdi.MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var info = new Windows.Win32.Graphics.Gdi.MONITORINFO { cbSize = (uint)sizeof(Windows.Win32.Graphics.Gdi.MONITORINFO) };
        if (!PInvoke.GetWindowPlacement(window, &placement) || !PInvoke.GetMonitorInfo(monitor, &info))
        {
            return null;
        }

        var normal = placement.rcNormalPosition;
        var offsetX = info.rcWork.left - info.rcMonitor.left;
        var offsetY = info.rcWork.top - info.rcMonitor.top;
        var screen = new RECT { left = normal.left + offsetX, top = normal.top + offsetY, right = normal.right + offsetX, bottom = normal.bottom + offsetY };
        return Text(screen, ",maximized");
    }

    private static string Text(RECT bounds, string suffix)
    {
        var width = bounds.right - bounds.left;
        var height = bounds.bottom - bounds.top;
        return string.Create(CultureInfo.InvariantCulture, $"{bounds.left},{bounds.top},{width},{height}{suffix}");
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static BOOL Visit(HWND window, LPARAM state)
    {
        var search = (Search)GCHandle.FromIntPtr(state.Value).Target!;
        uint owner;
        _ = PInvoke.GetWindowThreadProcessId(window, &owner);
        if (owner != search.ProcessId || !PInvoke.IsWindowVisible(window) || PInvoke.IsIconic(window)
            || !PInvoke.GetWindow(window, GET_WINDOW_CMD.GW_OWNER).IsNull)
        {
            return true;
        }

        search.Found = window;
        return false;
    }

    private sealed class Search(int processId)
    {
        public uint ProcessId { get; } = (uint)processId;

        public HWND Found { get; set; }
    }
}
