using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Spar.Testing;

/// <summary>
/// The Windows Sandbox client's windows on the host (process <c>WindowsSandboxRemoteSession</c>), which <c>wsb connect</c>
/// opens. The runner touches only windows that appeared after its own connect, and only without input: it minimizes them
/// (<c>ShowWindowAsync</c>) while they are shown, since the client restores and activates itself as the connection
/// completes, and asks them to close (<c>WM_CLOSE</c>) after the stop, since a stopped sandbox leaves them behind.
/// </summary>
[SupportedOSPlatform("windows10.0.17763")]
internal static unsafe class SandboxClientWindows
{
    public const string ProcessName = "WindowsSandboxRemoteSession";

    /// <summary>Every top-level window of the client's processes now, shown or not.</summary>
    public static List<ClientWindow> List()
    {
        var processes = Process.GetProcessesByName(ProcessName);
        var ids = new HashSet<uint>();
        foreach (var process in processes)
        {
            ids.Add((uint)process.Id);
            process.Dispose();
        }

        if (ids.Count == 0)
        {
            return [];
        }

        var state = new State(ids);
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

    /// <summary>Minimizes the window without activating anything; no input is sent.</summary>
    public static void Minimize(nint window) => PInvoke.ShowWindowAsync(new HWND((void*)window), SHOW_WINDOW_CMD.SW_MINIMIZE);

    /// <summary>Asks the window to close, as its close button does, without input.</summary>
    public static void Close(nint window) => PInvoke.PostMessage(new HWND((void*)window), PInvoke.WM_CLOSE, default, default);

    public static bool Exists(nint window) => PInvoke.IsWindow(new HWND((void*)window));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static BOOL Collect(HWND window, LPARAM parameter)
    {
        var state = (State)GCHandle.FromIntPtr(parameter.Value).Target!;
        uint owner;
        PInvoke.GetWindowThreadProcessId(window, &owner);
        if (state.ProcessIds.Contains(owner))
        {
            state.Found.Add(new ClientWindow(
                (nint)window.Value,
                PInvoke.IsWindowVisible(window),
                PInvoke.IsIconic(window),
                PInvoke.GetForegroundWindow() == window));
        }

        return true;
    }

    private sealed class State(HashSet<uint> processIds)
    {
        public HashSet<uint> ProcessIds { get; } = processIds;

        public List<ClientWindow> Found { get; } = [];
    }
}

/// <summary>A client window: its handle, and whether it is shown, minimized and in the foreground.</summary>
internal readonly record struct ClientWindow(nint Handle, bool Visible, bool Minimized, bool Foreground);
