using System.Runtime.InteropServices;
using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("windows")]

namespace Startup.Win;

// Win32 imports, checked against the Windows SDK 10.0.26100 headers (winuser.h, CommCtrl.h, consoleapi.h,
// processenv.h, shellapi.h, winbase.h, wdm.h for RTL_OSVERSIONINFOEXW) and WebView2.h 1.0.4314.35.
// comctl32 is not in NativeAOT's direct-call list (WindowsAPIs.txt), so TaskDialogIndirect is bound lazily at its first
// call: without a Common Controls 6 manifest the call throws EntryPointNotFoundException instead of failing the load.
internal static unsafe partial class Native
{
    public const int StdErrorHandle = -12;
    public const uint AttachParentProcess = 0xFFFFFFFF;
    public const uint FileTypeUnknown = 0, FileTypeDisk = 1, FileTypeChar = 2, FileTypePipe = 3;

    [LibraryImport("kernel32", SetLastError = true)]
    public static partial nint GetStdHandle(int which);

    [LibraryImport("kernel32")]
    public static partial uint GetFileType(nint handle);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AttachConsole(uint processId);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WriteFile(nint file, byte* buffer, int count, out int written, nint overlapped);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WriteConsoleW(nint console, char* buffer, int count, out int written, nint reserved);

    [LibraryImport("kernel32", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateFile(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32")]
    public static partial nint GetCurrentProcess();

    [LibraryImport("kernel32")]
    public static partial uint GetCurrentProcessId();

    [LibraryImport("kernel32")]
    public static partial uint GetCurrentThreadId();

    [LibraryImport("kernel32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetProcessTimes(nint process, out long creation, out long exit, out long kernel, out long user);

    [LibraryImport("kernel32")]
    public static partial void GetSystemTimePreciseAsFileTime(out long time);

    [LibraryImport("kernel32", SetLastError = true)]
    public static partial nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [LibraryImport("kernel32", EntryPoint = "Process32FirstW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Process32First(nint snapshot, ProcessEntry* entry);

    [LibraryImport("kernel32", EntryPoint = "Process32NextW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Process32Next(nint snapshot, ProcessEntry* entry);

    [LibraryImport("kernel32", SetLastError = true)]
    public static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TerminateProcess(nint process, uint exitCode);

    // PROCESSENTRY32W (tlhelp32.h), 568 bytes on x64.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct ProcessEntry
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public nint DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;
        public fixed char ExeFile[260];
    }

    // RTL_OSVERSIONINFOEXW (wdm.h). RtlGetVersion reports the real version whatever the manifest says.
    [StructLayout(LayoutKind.Sequential)]
    public struct OsVersionInfoEx
    {
        public uint Size;
        public uint Major;
        public uint Minor;
        public uint Build;
        public uint PlatformId;
        public fixed char CsdVersion[128];
        public ushort ServicePackMajor;
        public ushort ServicePackMinor;
        public ushort SuiteMask;
        public byte ProductType;
        public byte Reserved;
    }

    [LibraryImport("ntdll")]
    public static partial int RtlGetVersion(OsVersionInfoEx* info);

    // GetVersionExW reports what the manifest's supportedOS allows (6.2 without one); logged for comparison.
    [StructLayout(LayoutKind.Sequential)]
    public struct OsVersionInfo
    {
        public uint Size;
        public uint Major;
        public uint Minor;
        public uint Build;
        public uint PlatformId;
        public fixed char CsdVersion[128];
    }

    [LibraryImport("kernel32", EntryPoint = "GetVersionExW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetVersionEx(OsVersionInfo* info);

    // --- user32
    public const uint MbOk = 0x0, MbYesNo = 0x4, MbIconError = 0x10, MbSetForeground = 0x10000, MbDefButton1 = 0;
    public const int IdYes = 6;

    [LibraryImport("user32", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int MessageBox(nint owner, string text, string caption, uint type);

    public const int WhCbt = 5, HcbtActivate = 5;

    [LibraryImport("user32", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    public static partial nint SetWindowsHookEx(int id, delegate* unmanaged<int, nint, nint, nint> proc, nint module, uint threadId);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWindowsHookEx(nint hook);

    [LibraryImport("user32")]
    public static partial nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [LibraryImport("user32")]
    public static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32")]
    public static partial nint GetWindowDpiAwarenessContext(nint hwnd);

    [LibraryImport("user32")]
    public static partial int GetAwarenessFromDpiAwarenessContext(nint context);

    [LibraryImport("user32")]
    public static partial nint GetThreadDpiAwarenessContext();

    [LibraryImport("user32", EntryPoint = "GetClassNameW")]
    public static partial int GetClassName(nint hwnd, char* buffer, int count);

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;

        public override readonly string ToString() => $"[{Left},{Top},{Right},{Bottom}]";
    }

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(nint hwnd, out Rect rect);

    [StructLayout(LayoutKind.Sequential)]
    public struct Msg
    {
        public nint Hwnd;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int X, Y;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WndClassEx
    {
        public uint Size;
        public uint Style;
        public delegate* unmanaged<nint, uint, nint, nint, nint> WndProc;
        public int ClsExtra;
        public int WndExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public char* MenuName;
        public char* ClassName;
        public nint IconSmall;
    }

    [LibraryImport("user32", EntryPoint = "RegisterClassExW", SetLastError = true)]
    public static partial ushort RegisterClassEx(WndClassEx* wc);

    [LibraryImport("user32", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWindowEx(uint exStyle, string className, string title, uint style, int x, int y, int w, int h,
        nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32", EntryPoint = "DefWindowProcW")]
    public static partial nint DefWindowProc(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint hwnd, int cmd);

    [LibraryImport("user32", EntryPoint = "GetMessageW")]
    public static partial int GetMessage(out Msg msg, nint hwnd, uint min, uint max);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(in Msg msg);

    [LibraryImport("user32", EntryPoint = "DispatchMessageW")]
    public static partial nint DispatchMessage(in Msg msg);

    [LibraryImport("user32", EntryPoint = "PostThreadMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostThreadMessage(uint threadId, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32")]
    public static partial nuint SetTimer(nint hwnd, nuint id, uint elapse, nint proc);

    [LibraryImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool KillTimer(nint hwnd, nuint id);

    [LibraryImport("user32", EntryPoint = "LoadCursorW")]
    public static partial nint LoadCursor(nint instance, nint name);

    [LibraryImport("kernel32", EntryPoint = "GetModuleHandleW")]
    public static partial nint GetModuleHandle(nint name);

    // --- comctl32 v6 (task dialog). TASKDIALOGCONFIG and TASKDIALOG_BUTTON are declared under pshpack1.h.
    public const int TdfEnableHyperlinks = 0x1, TdfAllowDialogCancellation = 0x8, TdfExpandFooterArea = 0x40,
        TdfPositionRelativeToWindow = 0x1000, TdfSizeToContent = 0x01000000;
    public const int TdcbfOk = 0x1, TdcbfClose = 0x20;
    public const uint TdnCreated = 0, TdnButtonClicked = 2, TdnHyperlinkClicked = 3, TdnDestroyed = 5, TdnDialogConstructed = 7;
    public const nint TdErrorIcon = 0xFFFE; // MAKEINTRESOURCEW(-2)
    public const int IdClose = 8, IdOk = 1;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TaskDialogButton
    {
        public int Id;
        public char* Text;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TaskDialogConfig
    {
        public uint Size;
        public nint Owner;
        public nint Instance;
        public int Flags;
        public int CommonButtons;
        public char* WindowTitle;
        public nint MainIcon;
        public char* MainInstruction;
        public char* Content;
        public uint ButtonCount;
        public TaskDialogButton* Buttons;
        public int DefaultButton;
        public uint RadioButtonCount;
        public TaskDialogButton* RadioButtons;
        public int DefaultRadioButton;
        public char* VerificationText;
        public char* ExpandedInformation;
        public char* ExpandedControlText;
        public char* CollapsedControlText;
        public nint FooterIcon;
        public char* Footer;
        public delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint, int> Callback;
        public nint CallbackData;
        public uint Width;
    }

    [LibraryImport("comctl32")]
    public static partial int TaskDialogIndirect(TaskDialogConfig* config, int* button, int* radioButton, int* verification);

    // --- shell32
    [LibraryImport("shell32", EntryPoint = "ShellExecuteW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint ShellExecute(nint hwnd, string? verb, string file, string? parameters, string? directory, int show);

    // --- advapi32 (event log). An unregistered source is accepted; the viewer then lacks a message file.
    [LibraryImport("advapi32", EntryPoint = "RegisterEventSourceW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint RegisterEventSource(string? server, string source);

    [LibraryImport("advapi32", EntryPoint = "ReportEventW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReportEvent(nint log, ushort type, ushort category, uint eventId, nint sid, ushort strings, uint dataSize,
        char** texts, nint data);

    [LibraryImport("advapi32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeregisterEventSource(nint log);

    // --- ole32
    [LibraryImport("ole32")]
    public static partial int CoInitializeEx(nint reserved, uint mode);

    [LibraryImport("ole32")]
    public static partial void CoTaskMemFree(nint p);
}
