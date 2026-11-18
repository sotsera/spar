using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RestartWait.Interop;

namespace RestartWait;

/// <summary>
/// The Win32 calls of the probe, declared by hand: one window class, offscreen never-activated windows, a message pump
/// on the STA, and the browser processes of one user-data folder (found by command line, as spikes/window-automation
/// does) with their exit times taken from a process handle.
/// </summary>
internal static unsafe partial class Native
{
    private const string ClassName = "SparRestartWaitProbe";

    public static readonly Stopwatch Clock = Stopwatch.StartNew();

    public static double Now => Clock.Elapsed.TotalMilliseconds;

    [StructLayout(LayoutKind.Sequential)]
    public struct Msg
    {
        public nint Window;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint> Procedure;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public char* MenuName;
        public char* ClassName;
        public nint SmallIcon;
    }

    public static void RegisterWindowClass()
    {
        fixed (char* name = ClassName)
        {
            var windowClass = new WindowClass
            {
                Size = (uint)sizeof(WindowClass),
                Procedure = &WindowProcedure,
                Instance = GetModuleHandleW(null),
                ClassName = name,
            };
            RegisterClassExW(windowClass);
        }
    }

    /// <summary>A top-level window at (-32000, -32000), no taskbar button, never activated; "hidden" never shows it.</summary>
    public static nint CreateTestWindow(bool shown)
    {
        const uint WsExNoActivate = 0x08000000;
        const uint WsExToolWindow = 0x00000080;
        const uint WsOverlappedWindow = 0x00CF0000;
        const int SwShowNoActivate = 4;
        var window = CreateWindowExW(WsExNoActivate | WsExToolWindow, ClassName, "Restart wait probe", WsOverlappedWindow,
            -32000, -32000, 1024, 768, 0, 0, GetModuleHandleW(null), 0);
        if (window == 0)
        {
            throw new InvalidOperationException("CreateWindowEx failed: " + Marshal.GetLastPInvokeError());
        }

        if (shown)
        {
            ShowWindow(window, SwShowNoActivate);
        }

        return window;
    }

    /// <summary>Pumps the STA's messages until <paramref name="done"/> or the timeout; returns <paramref name="done"/>'s value.</summary>
    public static bool Pump(Func<bool> done, double timeoutMilliseconds)
    {
        var end = Now + timeoutMilliseconds;
        while (!done())
        {
            var left = end - Now;
            if (left <= 0)
            {
                return false;
            }

            while (PeekMessageW(out var message, 0, 0, 0, 1) != 0)
            {
                TranslateMessage(message);
                DispatchMessageW(message);
            }

            MsgWaitForMultipleObjectsEx(0, null, (uint)Math.Clamp(left, 0, 5), 0x04FF, 0x0004);
        }

        return true;
    }

    public static T Wait<T>(Task<T> task, double timeoutMilliseconds) =>
        Pump(() => task.IsCompleted, timeoutMilliseconds) ? task.GetAwaiter().GetResult() : throw new TimeoutException("WebView2 did not complete in time.");

    /// <summary>Every msedgewebview2 process whose command line contains <paramref name="marker"/>, with its kind.</summary>
    public static List<(int Id, string Kind)> BrowserProcesses(string marker)
    {
        var found = new List<(int, string)>();
        foreach (var process in Process.GetProcessesByName("msedgewebview2"))
        {
            using (process)
            {
                var handle = OpenProcess(0x1000, false, (uint)process.Id);
                if (handle == 0)
                {
                    continue;
                }

                var commandLine = CommandLine(handle);
                CloseHandle(handle);
                if (commandLine is not null && commandLine.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    var index = commandLine.IndexOf("--type=", StringComparison.Ordinal);
                    var kind = index < 0 ? "browser" : commandLine[(index + 7)..].Split(' ')[0].Trim('"');
                    if (kind == "utility" && commandLine.Contains("--utility-sub-type=", StringComparison.Ordinal))
                    {
                        var sub = commandLine.IndexOf("--utility-sub-type=", StringComparison.Ordinal);
                        kind = commandLine[(sub + 19)..].Split(' ')[0].Split('.')[0];
                    }

                    found.Add((process.Id, kind));
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Waits for <paramref name="processId"/> to exit on a thread of its own and calls <paramref name="exited"/> with
    /// the probe's clock at that moment; false when the process is already gone or cannot be opened.
    /// </summary>
    public static bool WatchExit(uint processId, Action<double> exited)
    {
        const uint Synchronize = 0x00100000;
        const uint QueryLimited = 0x1000;
        var handle = OpenProcess(Synchronize | QueryLimited, false, processId);
        if (handle == 0)
        {
            return false;
        }

        new Thread(() =>
        {
            WaitForSingleObject(handle, uint.MaxValue);
            var at = Now;
            CloseHandle(handle);
            exited(at);
        }) { IsBackground = true, Name = "exit " + processId }.Start();
        return true;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProcedure(nint window, uint message, nint wParam, nint lParam) =>
        message == 0x0010 ? 0 : DefWindowProcW(window, message, wParam, lParam);

    private static string? CommandLine(nint handle)
    {
        var buffer = new byte[32768];
        fixed (byte* data = buffer)
        {
            if (NtQueryInformationProcess(handle, 60, (nint)data, (uint)buffer.Length, out _) < 0)
            {
                return null;
            }

            // UNICODE_STRING { USHORT Length; USHORT MaximumLength; PWSTR Buffer; } followed by the characters.
            return new string(*(char**)(data + 8), 0, *(ushort*)data / 2);
        }
    }

    [LibraryImport("user32")]
    private static partial ushort RegisterClassExW(in WindowClass windowClass);

    [LibraryImport("user32", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowExW(uint exStyle, string className, string? title, uint style, int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint parameter);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint window, int command);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint window);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetClientRect(nint window, out Rect rect);

    [LibraryImport("user32")]
    private static partial int PeekMessageW(out Msg message, nint window, uint min, uint max, uint remove);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(in Msg message);

    [LibraryImport("user32")]
    private static partial nint DispatchMessageW(in Msg message);

    [LibraryImport("user32")]
    private static partial uint MsgWaitForMultipleObjectsEx(uint count, nint* handles, uint milliseconds, uint wake, uint flags);

    [LibraryImport("user32")]
    private static partial nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("kernel32")]
    private static partial nint GetModuleHandleW(char* name);

    [LibraryImport("kernel32")]
    private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [LibraryImport("kernel32")]
    private static partial uint WaitForSingleObject(nint handle, uint milliseconds);

    [LibraryImport("kernel32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("ntdll")]
    private static partial int NtQueryInformationProcess(nint handle, int informationClass, nint information, uint length, out uint returned);
}
