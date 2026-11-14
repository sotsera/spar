using System.Runtime.InteropServices;

namespace Spar.PageHeartbeat;

/// <summary>
/// Imports CsWin32 does not cover as needed here, declared from the Windows SDK 10.0.26100 headers (psapi.h,
/// processthreadsapi.h, winuser.h, commctrl.h). x64 only: SIZE_T and ULONG_PTR are 8 bytes. Layouts are asserted at
/// start-up.
/// </summary>
internal static unsafe partial class Win32
{
    public const uint ProcessQueryLimitedInformation = 0x1000;
    public const uint ProcessTerminate = 0x0001;
    public const uint InputMouse = 0, InputKeyboard = 1;
    public const uint MouseMove = 0x0001, MouseLeftDown = 0x0002, MouseLeftUp = 0x0004, MouseAbsolute = 0x8000, MouseVirtualDesk = 0x4000;
    public const uint KeyUp = 0x0002;

    /// <summary>PROCESS_MEMORY_COUNTERS_EX2 (psapi.h): 96 bytes on x64 (copied from spikes/resource-usage).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MemoryCountersEx2
    {
        public uint Cb;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
        public nuint PrivateUsage;
        public nuint PrivateWorkingSetSize;
        public ulong SharedCommitUsage;
    }

    /// <summary>INPUT (winuser.h) with the MOUSEINPUT arm; KEYBDINPUT (24 bytes) fits in the same 32-byte union. 40 bytes.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    public struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public int Dx;            // MOUSEINPUT.dx       | KEYBDINPUT.wVk (ushort) + wScan (ushort)
        [FieldOffset(12)] public int Dy;           // MOUSEINPUT.dy       | KEYBDINPUT.dwFlags
        [FieldOffset(16)] public uint MouseData;   // MOUSEINPUT.mouseData | KEYBDINPUT.time
        [FieldOffset(20)] public uint MouseFlags;  // MOUSEINPUT.dwFlags
        [FieldOffset(24)] public uint Time;        // MOUSEINPUT.time
        [FieldOffset(32)] public nuint ExtraInfo;  // MOUSEINPUT.dwExtraInfo
    }

    // TASKDIALOGCONFIG and TASKDIALOG_BUTTON are declared under pshpack1.h (copied from spikes/startup-prerequisites).
    public const int TdfAllowDialogCancellation = 0x8, TdfPositionRelativeToWindow = 0x1000;
    public const uint TdnCreated = 0, TdnButtonClicked = 2, TdnDestroyed = 5, TdnTimer = 4;
    public const int TdfCallbackTimer = 0x800;
    public const uint TdmClickButton = 0x0400 + 102; // WM_USER + 102
    public const nint TdWarningIcon = 0xFFFF;         // MAKEINTRESOURCEW(-1)

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

    public static void AssertLayouts()
    {
        if (sizeof(MemoryCountersEx2) != 96 || sizeof(Input) != 40 || sizeof(TaskDialogConfig) != 160)
        {
            throw new InvalidOperationException($"Layout mismatch: EX2={sizeof(MemoryCountersEx2)} INPUT={sizeof(Input)} TASKDIALOGCONFIG={sizeof(TaskDialogConfig)}");
        }
    }

    [LibraryImport("kernel32", SetLastError = true)]
    public static partial nint OpenProcess(uint access, int inherit, uint processId);

    [LibraryImport("kernel32")]
    public static partial int CloseHandle(nint handle);

    [LibraryImport("kernel32", SetLastError = true)]
    public static partial int TerminateProcess(nint process, uint exitCode);

    [LibraryImport("kernel32")]
    public static partial nint GetCurrentProcess();

    [LibraryImport("kernel32", EntryPoint = "K32GetProcessMemoryInfo", SetLastError = true)]
    public static partial int GetProcessMemoryInfo(nint process, MemoryCountersEx2* counters, uint size);

    [LibraryImport("user32", SetLastError = true)]
    public static partial uint SendInput(uint count, Input* inputs, int size);

    [LibraryImport("user32")]
    public static partial int GetSystemMetrics(int index);

    [LibraryImport("comctl32")]
    public static partial int TaskDialogIndirect(TaskDialogConfig* config, int* button, int* radioButton, int* verification);

    /// <summary>Private working set (what Task Manager shows) in kB, or -1 when unreadable.</summary>
    public static long PrivateKb(int pid)
    {
        var handle = pid == 0 ? GetCurrentProcess() : OpenProcess(ProcessQueryLimitedInformation, 0, (uint)pid);
        if (handle == 0)
        {
            return -1;
        }

        try
        {
            MemoryCountersEx2 counters = default;
            counters.Cb = (uint)sizeof(MemoryCountersEx2);
            return GetProcessMemoryInfo(handle, &counters, counters.Cb) != 0 ? (long)(counters.PrivateWorkingSetSize / 1024) : -1;
        }
        finally
        {
            if (pid != 0)
            {
                CloseHandle(handle);
            }
        }
    }

    /// <summary>Terminates a process this host's own browser started (a renderer); returns the Win32 error, 0 on success.</summary>
    public static int Terminate(int pid)
    {
        var handle = OpenProcess(ProcessTerminate, 0, (uint)pid);
        if (handle == 0)
        {
            return Marshal.GetLastPInvokeError();
        }

        try
        {
            return TerminateProcess(handle, 1) != 0 ? 0 : Marshal.GetLastPInvokeError();
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>A real left click at a screen point (absolute, normalised to the primary screen). Windows Sandbox only.</summary>
    public static uint Click(int x, int y)
    {
        var width = GetSystemMetrics(0);
        var height = GetSystemMetrics(1);
        var nx = (int)((x * 65535L + width / 2) / Math.Max(1, width - 1));
        var ny = (int)((y * 65535L + height / 2) / Math.Max(1, height - 1));
        var inputs = stackalloc Input[3];
        inputs[0] = new Input { Type = InputMouse, Dx = nx, Dy = ny, MouseFlags = MouseMove | MouseAbsolute };
        inputs[1] = new Input { Type = InputMouse, Dx = nx, Dy = ny, MouseFlags = MouseLeftDown | MouseAbsolute };
        inputs[2] = new Input { Type = InputMouse, Dx = nx, Dy = ny, MouseFlags = MouseLeftUp | MouseAbsolute };
        return SendInput(3, inputs, sizeof(Input));
    }

    /// <summary>A real key press and release (virtual-key code). Windows Sandbox only.</summary>
    public static uint Key(ushort virtualKey)
    {
        var inputs = stackalloc Input[2];
        inputs[0] = new Input { Type = InputKeyboard, Dx = virtualKey };
        inputs[1] = new Input { Type = InputKeyboard, Dx = virtualKey, Dy = (int)KeyUp };
        return SendInput(2, inputs, sizeof(Input));
    }
}
