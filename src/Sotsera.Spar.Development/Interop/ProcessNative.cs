using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Sotsera.Spar.Development.Interop;

internal static unsafe partial class ProcessNative
{
    [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true)]
    internal static partial SafeJobHandle CreateJobObject(nint attributes, nint name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetInformationJobObject(SafeJobHandle job, int type, in ExtendedLimits limits, uint length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool QueryInformationJobObject(SafeJobHandle job, int type, void* value, uint length, out uint returned);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TerminateJobObject(SafeJobHandle job, uint exitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsProcessInJob(nint process, SafeJobHandle job, out int result);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool InitializeProcThreadAttributeList(nint list, int count, uint flags, ref nuint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UpdateProcThreadAttribute(nint list, uint flags, nuint attribute, nint value,
        nuint size, nint previous, nint returned);

    [LibraryImport("kernel32.dll")]
    internal static partial void DeleteProcThreadAttributeList(nint list);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CreateProcess(string application, char* command, nint processAttributes, nint threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags, char* environment, string directory,
        in StartupInfoEx startup, out ProcessInformation information);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetHandleInformation(nint handle, uint mask, uint flags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial uint ResumeThread(SafeWaitHandle thread);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TerminateProcess(SafeProcessHandle process, uint exitCode);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint handle);
}

internal sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeJobHandle() : base(true) { }

    protected override bool ReleaseHandle() => ProcessNative.CloseHandle(handle);
}

[StructLayout(LayoutKind.Sequential)]
internal struct BasicLimits
{
    internal long ProcessTime;
    internal long JobTime;
    internal uint Flags;
    internal nuint MinimumWorkingSet;
    internal nuint MaximumWorkingSet;
    internal uint ActiveProcessLimit;
    internal nuint Affinity;
    internal uint Priority;
    internal uint Scheduling;
}

[StructLayout(LayoutKind.Sequential)]
internal struct IoCounters
{
    internal ulong ReadOperations;
    internal ulong WriteOperations;
    internal ulong OtherOperations;
    internal ulong ReadBytes;
    internal ulong WriteBytes;
    internal ulong OtherBytes;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ExtendedLimits
{
    internal BasicLimits Basic;
    internal IoCounters Io;
    internal nuint ProcessMemory;
    internal nuint JobMemory;
    internal nuint PeakProcessMemory;
    internal nuint PeakJobMemory;
}

[StructLayout(LayoutKind.Sequential)]
internal struct StartupInfo
{
    internal uint Size;
    internal nint Reserved;
    internal nint Desktop;
    internal nint Title;
    internal uint X;
    internal uint Y;
    internal uint Width;
    internal uint Height;
    internal uint CharacterWidth;
    internal uint CharacterHeight;
    internal uint FillAttribute;
    internal uint Flags;
    internal ushort ShowWindow;
    internal ushort ReservedSize;
    internal nint ReservedBytes;
    internal nint Input;
    internal nint Output;
    internal nint Error;
}

[StructLayout(LayoutKind.Sequential)]
internal struct StartupInfoEx
{
    internal StartupInfo Startup;
    internal nint Attributes;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ProcessInformation
{
    internal nint Process;
    internal nint Thread;
    internal uint ProcessId;
    internal uint ThreadId;
}
