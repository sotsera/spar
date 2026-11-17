using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SandboxRunner;

/// <summary>
/// The Windows Sandbox client windows on the host. The runner only touches windows that appeared after its own
/// <c>wsb connect</c> and belong to the Sandbox client process: it minimizes them (no input is sent) and, after the stop,
/// posts <c>WM_CLOSE</c> to them.
/// </summary>
internal static partial class ClientWindows
{
    private const string ClientProcess = "WindowsSandboxRemoteSession";
    private const int SwMinimize = 6;
    private const uint WmClose = 0x0010;

    public readonly record struct Window(nint Handle, int ProcessId, string Title, bool Visible, bool Minimized, bool Foreground);

    /// <summary>Top-level windows of Sandbox client processes, visible or not.</summary>
    public static unsafe List<Window> List()
    {
        var handles = new List<nint>();
        var gc = GCHandle.Alloc(handles);
        try
        {
            EnumWindows(&Collect, GCHandle.ToIntPtr(gc));
        }
        finally
        {
            gc.Free();
        }

        var foreground = GetForegroundWindow();
        var buffer = new char[256];
        var names = new Dictionary<int, string>();
        var windows = new List<Window>();
        foreach (var handle in handles)
        {
            _ = GetWindowThreadProcessId(handle, out var pid);
            if (!names.TryGetValue((int)pid, out var name))
            {
                try
                {
                    using var process = Process.GetProcessById((int)pid);
                    name = process.ProcessName;
                }
                catch (ArgumentException)
                {
                    name = "";
                }

                names[(int)pid] = name;
            }

            if (!name.Equals(ClientProcess, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int length;
            fixed (char* text = buffer)
            {
                length = GetWindowTextW(handle, text, buffer.Length);
            }

            windows.Add(new Window(handle, (int)pid, new string(buffer, 0, Math.Max(0, length)), IsWindowVisible(handle), IsIconic(handle), handle == foreground));
        }

        return windows;
    }

    public static bool Minimize(nint handle) => ShowWindowAsync(handle, SwMinimize);

    public static bool Close(nint handle) => PostMessageW(handle, WmClose, 0, 0);

    public static bool Exists(nint handle) => IsWindow(handle);

    [UnmanagedCallersOnly]
    private static int Collect(nint handle, nint state)
    {
        ((List<nint>)GCHandle.FromIntPtr(state).Target!).Add(handle);
        return 1;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool EnumWindows(delegate* unmanaged<nint, nint, int> callback, nint state);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
    private static unsafe partial int GetWindowTextW(nint window, char* text, int count);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(nint window);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindowAsync(nint window, int command);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(nint window, uint message, nint wParam, nint lParam);
}
