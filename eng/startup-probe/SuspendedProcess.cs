using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Spar.Integration;

// A real child whose loader never runs. It cannot execute commands or create descendants.
public sealed class SuspendedProcess : IDisposable
{
    private readonly SafeProcessHandle _handle;
    private readonly SafeFileHandle _thread;

    public Process Process { get; }

    public SuspendedProcess(string executable)
    {
        var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>() };
        const int CreateSuspended = 0x00000004;
        const int CreateNoWindow = 0x08000000;
        if (!CreateProcessW(executable, new StringBuilder($"\"{executable}\""), IntPtr.Zero, IntPtr.Zero,
            false, CreateSuspended | CreateNoWindow, IntPtr.Zero, null, ref startup, out var information))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        _handle = new SafeProcessHandle(information.Process, true);
        _thread = new SafeFileHandle(information.Thread, true);
        try
        {
            Process = Process.GetProcessById(information.ProcessId);
            _ = Process.Handle;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_handle.IsClosed)
        {
            return;
        }

        if (!TerminateProcess(_handle, 1))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        // A termination request is not proof of exit. Retain handles if cleanup cannot finish.
        if (WaitForSingleObject(_handle, 30000) != 0)
        {
            throw new InvalidOperationException("Suspended fixture did not exit after termination.");
        }

        Process?.Dispose();
        _thread.Dispose();
        _handle.Dispose();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(string application, StringBuilder commandLine,
        IntPtr processAttributes, IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        int creationFlags, IntPtr environment, string directory, ref StartupInfo startup,
        out ProcessInformation information);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(SafeProcessHandle process, int exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int WaitForSingleObject(SafeProcessHandle handle, int milliseconds);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string Reserved;
        public string Desktop;
        public string Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short ReservedSize;
        public IntPtr ReservedBytes;
        public IntPtr StandardInput;
        public IntPtr StandardOutput;
        public IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public int ProcessId;
        public int ThreadId;
    }
}
