using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Spar.WindowAutomation;

/// <summary>
/// The few Win32 calls the spike needs, declared by hand: the UI thread's window class and message loop, top-level
/// window enumeration (to spot a native dialog without touching it) and the browser processes of one user-data
/// folder, found by command line (adapted from spikes/window-tests).
/// </summary>
internal static unsafe partial class Native
{
    public const uint WmClose = 0x0010;
    public const uint WmApp = 0x8000;

    [ThreadStatic]
    private static List<TopWindow>? t_found;

    public sealed record TopWindow(nint Handle, int ProcessId, string ClassName, string Title, int OwnerProcessId);

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
    public struct WindowClass
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

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>Visible top-level windows that belong to, or are owned by a window of, one of <paramref name="processIds"/>.</summary>
    public static List<TopWindow> VisibleWindowsOf(IReadOnlySet<int> processIds)
    {
        t_found = [];
        EnumWindows(&Collect, 0);
        var all = t_found;
        t_found = null;
        return all.Where(w => processIds.Contains(w.ProcessId) || processIds.Contains(w.OwnerProcessId)).ToList();
    }

    /// <summary>Every msedgewebview2 process whose command line contains <paramref name="marker"/>.</summary>
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
                    found.Add((process.Id, kind));
                }
            }
        }

        return found;
    }

    /// <summary>Private bytes of every browser process of the folder, summed.</summary>
    public static long BrowserPrivateBytes(string marker)
    {
        long total = 0;
        foreach (var (id, _) in BrowserProcesses(marker))
        {
            try
            {
                using var process = Process.GetProcessById(id);
                total += process.PrivateMemorySize64;
            }
            catch (ArgumentException)
            {
            }
        }

        return total;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Collect(nint window, nint parameter)
    {
        if (!IsWindowVisible(window))
        {
            return 1;
        }

        GetWindowThreadProcessId(window, out var processId);
        var className = stackalloc char[256];
        var classLength = GetClassNameW(window, className, 256);
        var title = stackalloc char[256];
        var titleLength = GetWindowTextW(window, title, 256);
        var owner = GetWindow(window, 4);
        uint ownerProcessId = 0;
        if (owner != 0)
        {
            GetWindowThreadProcessId(owner, out ownerProcessId);
        }

        t_found!.Add(new TopWindow(window, (int)processId, new string(className, 0, classLength), new string(title, 0, titleLength), (int)ownerProcessId));
        return 1;
    }

    private static string? CommandLine(nint handle)
    {
        var buffer = new byte[16384];
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
    public static partial ushort RegisterClassExW(in WindowClass windowClass);

    [LibraryImport("user32", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWindowExW(uint exStyle, string className, string? title, uint style, int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint parameter);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint window, int command);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint window);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetClientRect(nint window, out Rect rect);

    [LibraryImport("user32")]
    public static partial int GetMessageW(out Msg message, nint window, uint min, uint max);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(in Msg message);

    [LibraryImport("user32")]
    public static partial nint DispatchMessageW(in Msg message);

    [LibraryImport("user32")]
    public static partial nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessageW(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("user32")]
    public static partial void PostQuitMessage(int exitCode);

    [LibraryImport("user32")]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(nint window, out Rect rect);

    [LibraryImport("kernel32")]
    public static partial nint GetModuleHandleW(char* name);

    [LibraryImport("user32")]
    private static partial int EnumWindows(delegate* unmanaged[Stdcall]<nint, nint, int> callback, nint parameter);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint window);

    [LibraryImport("user32")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);

    [LibraryImport("user32")]
    private static partial int GetClassNameW(nint window, char* name, int count);

    [LibraryImport("user32")]
    private static partial int GetWindowTextW(nint window, char* text, int count);

    [LibraryImport("user32")]
    private static partial nint GetWindow(nint window, uint command);

    [LibraryImport("kernel32")]
    private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [LibraryImport("kernel32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("ntdll")]
    private static partial int NtQueryInformationProcess(nint handle, int informationClass, nint information, uint length, out uint returned);
}
