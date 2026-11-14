using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Spar.ResourceUsage;

/// <summary>
/// Process memory and directory imports, declared from the Windows SDK 10.0.26100 headers (psapi.h, processthreadsapi.h,
/// fileapi.h, minwinbase.h, winbase.h). x64 only: SIZE_T and ULONG_PTR are 8 bytes.
/// </summary>
internal static unsafe partial class Win32
{
    public const uint ProcessQueryLimitedInformation = 0x1000;
    public const uint ProcessQueryInformation = 0x0400;
    public const uint ProcessVmRead = 0x0010;
    public const int ErrorBadLength = 24;
    public const int ErrorNoMoreFiles = 18;

    /// <summary>PROCESS_MEMORY_COUNTERS_EX2 (psapi.h): 96 bytes on x64, asserted at start-up.</summary>
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

    /// <summary>WIN32_FIND_DATAW (minwinbase.h): 592 bytes, 4-byte aligned (FILETIME is two DWORDs).</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct FindData
    {
        public uint FileAttributes;
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint Reserved0;
        public uint Reserved1;
        public fixed char FileName[260];
        public fixed char AlternateFileName[14];
    }

    public static void AssertLayouts()
    {
        if (sizeof(MemoryCountersEx2) != 96 || sizeof(FindData) != 592)
        {
            throw new InvalidOperationException($"Layout mismatch: EX2={sizeof(MemoryCountersEx2)} FindData={sizeof(FindData)}");
        }
    }

    [LibraryImport("kernel32", SetLastError = true)]
    public static partial nint OpenProcess(uint access, int inherit, uint processId);

    [LibraryImport("kernel32")]
    public static partial int CloseHandle(nint handle);

    [LibraryImport("kernel32")]
    public static partial nint GetCurrentProcess();

    [LibraryImport("kernel32", EntryPoint = "K32GetProcessMemoryInfo", SetLastError = true)]
    public static partial int GetProcessMemoryInfo(nint process, MemoryCountersEx2* counters, uint size);

    [LibraryImport("kernel32", EntryPoint = "K32QueryWorkingSet", SetLastError = true)]
    public static partial int QueryWorkingSet(nint process, void* buffer, uint size);

    [LibraryImport("ntdll")]
    public static partial int NtQueryInformationProcess(nint handle, int informationClass, void* information, uint length, out uint returned);

    [LibraryImport("kernel32", EntryPoint = "FindFirstFileExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint FindFirstFileEx(string fileName, int infoLevel, FindData* data, int searchOp, nint filter, uint flags);

    [LibraryImport("kernel32", EntryPoint = "FindNextFileW", SetLastError = true)]
    public static partial int FindNextFile(nint find, FindData* data);

    [LibraryImport("kernel32")]
    public static partial int FindClose(nint find);

    [LibraryImport("kernel32", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateFile(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32", SetLastError = true)]
    public static partial int GetFileInformationByHandleEx(nint file, int infoClass, void* buffer, uint size);

    /// <summary>EX2 counters, or null when the process cannot be read (exited, or access denied).</summary>
    public static MemoryCountersEx2? Counters(nint handle)
    {
        MemoryCountersEx2 counters = default;
        counters.Cb = (uint)sizeof(MemoryCountersEx2);
        return GetProcessMemoryInfo(handle, &counters, counters.Cb) != 0 ? counters : null;
    }

    public static MemoryCountersEx2 Self() => Counters(GetCurrentProcess()) ?? throw new InvalidOperationException("GetProcessMemoryInfo(self) failed.");

    /// <summary>Reads the counters of one process id: open, read, close.</summary>
    public static (MemoryCountersEx2? Counters, int Error) CountersOf(int pid, uint access = ProcessQueryLimitedInformation)
    {
        var handle = OpenProcess(access, 0, (uint)pid);
        if (handle == 0)
        {
            return (null, Marshal.GetLastPInvokeError());
        }

        try
        {
            var counters = Counters(handle);
            return (counters, counters is null ? Marshal.GetLastPInvokeError() : 0);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>Working-set page statistics from QueryWorkingSet: private, shared, shared by one process, proportional.</summary>
    public readonly record struct PageStats(long Pages, long PrivatePages, long SharedPages, long SharedOncePages, double ProportionalPages, int Error, double Ms);

    private static ulong[] s_workingSet = new ulong[1 << 16];

    public static PageStats WorkingSetPages(int pid)
    {
        var started = Stopwatch.GetTimestamp();
        var handle = OpenProcess(ProcessQueryInformation | ProcessVmRead, 0, (uint)pid);
        if (handle == 0)
        {
            return new PageStats(0, 0, 0, 0, 0, Marshal.GetLastPInvokeError(), Log.Ms(started));
        }

        try
        {
            while (true)
            {
                fixed (ulong* buffer = s_workingSet)
                {
                    if (QueryWorkingSet(handle, buffer, (uint)(s_workingSet.Length * 8)) == 0)
                    {
                        var error = Marshal.GetLastPInvokeError();
                        if (error != ErrorBadLength)
                        {
                            return new PageStats(0, 0, 0, 0, 0, error, Log.Ms(started));
                        }

                        s_workingSet = new ulong[(int)buffer[0] + 4096];
                        continue;
                    }

                    long count = (long)buffer[0], privatePages = 0, shared = 0, once = 0;
                    double proportional = 0;
                    for (long i = 1; i <= count; i++)
                    {
                        // PSAPI_WORKING_SET_BLOCK: Protection:5, ShareCount:3, Shared:1. ShareCount saturates at 7.
                        var flags = buffer[i];
                        var shareCount = (int)((flags >> 5) & 7);
                        if (((flags >> 8) & 1) == 0)
                        {
                            privatePages++;
                            proportional += 1;
                        }
                        else
                        {
                            shared++;
                            if (shareCount <= 1)
                            {
                                once++;
                            }

                            proportional += 1.0 / Math.Max(1, shareCount);
                        }
                    }

                    return new PageStats(count, privatePages, shared, once, proportional, 0, Log.Ms(started));
                }
            }
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>The process's command line (ProcessCommandLineInformation, class 60), or null.</summary>
    public static string? CommandLine(int pid)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, 0, (uint)pid);
        if (handle == 0)
        {
            return null;
        }

        try
        {
            var buffer = new byte[16384];
            fixed (byte* data = buffer)
            {
                if (NtQueryInformationProcess(handle, 60, data, (uint)buffer.Length, out _) < 0)
                {
                    return null;
                }

                // UNICODE_STRING { USHORT Length; USHORT MaximumLength; PWSTR Buffer; } followed by the characters.
                return new string(*(char**)(data + 8), 0, *(ushort*)data / 2);
            }
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>Recursive walk with FindFirstFileExW (basic info, large fetch). Logical sizes only.</summary>
    public static WalkResult FindWalk(string root)
    {
        var started = Stopwatch.GetTimestamp();
        long files = 0, directories = 0, bytes = 0, errors = 0;
        var pending = new Stack<string>();
        pending.Push(root);
        FindData data;
        while (pending.TryPop(out var directory))
        {
            // FindExInfoBasic = 1, FindExSearchNameMatch = 0, FIND_FIRST_EX_LARGE_FETCH = 2.
            var find = FindFirstFileEx(directory + "\\*", 1, &data, 0, 0, 2);
            if (find == -1)
            {
                errors++;
                continue;
            }

            do
            {
                var name = new ReadOnlySpan<char>(data.FileName, 260);
                name = name[..name.IndexOf('\0')];
                if (name is "." or "..")
                {
                    continue;
                }

                if ((data.FileAttributes & (uint)FileAttributes.Directory) != 0)
                {
                    directories++;
                    if ((data.FileAttributes & (uint)FileAttributes.ReparsePoint) == 0)
                    {
                        pending.Push(string.Concat(directory, "\\", name));
                    }
                }
                else
                {
                    files++;
                    bytes += ((long)data.FileSizeHigh << 32) | data.FileSizeLow;
                }
            }
            while (FindNextFile(find, &data) != 0);

            FindClose(find);
        }

        return new WalkResult(files, directories, bytes, -1, errors, Log.Ms(started));
    }

    private static readonly byte[] s_directoryBuffer = new byte[64 * 1024];

    /// <summary>
    /// Recursive walk with GetFileInformationByHandleEx(FileFullDirectoryInfo) on each directory handle: logical size
    /// (EndOfFile) and allocated size (AllocationSize) in bulk, without opening any file.
    /// </summary>
    public static WalkResult DirectoryInfoWalk(string root)
    {
        var started = Stopwatch.GetTimestamp();
        long files = 0, directories = 0, bytes = 0, allocated = 0, errors = 0;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            // FILE_LIST_DIRECTORY, share read|write|delete, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS.
            var handle = CreateFile(directory, 0x1, 0x7, 0, 3, 0x02000000, 0);
            if (handle == -1)
            {
                errors++;
                continue;
            }

            try
            {
                fixed (byte* buffer = s_directoryBuffer)
                {
                    // FileFullDirectoryInfo = 14.
                    while (GetFileInformationByHandleEx(handle, 14, buffer, (uint)s_directoryBuffer.Length) != 0)
                    {
                        var entry = buffer;
                        while (true)
                        {
                            // FILE_FULL_DIR_INFO: NextEntryOffset 0, EndOfFile 40, AllocationSize 48, FileAttributes 56,
                            // FileNameLength 60 (bytes), FileName 68.
                            var next = *(uint*)entry;
                            var attributes = *(uint*)(entry + 56);
                            var name = new ReadOnlySpan<char>(entry + 68, (int)(*(uint*)(entry + 60) / 2));
                            if (name is not "." and not "..")
                            {
                                if ((attributes & (uint)FileAttributes.Directory) != 0)
                                {
                                    directories++;
                                    if ((attributes & (uint)FileAttributes.ReparsePoint) == 0)
                                    {
                                        pending.Push(string.Concat(directory, "\\", name));
                                    }
                                }
                                else
                                {
                                    files++;
                                    bytes += *(long*)(entry + 40);
                                    allocated += *(long*)(entry + 48);
                                }
                            }

                            if (next == 0)
                            {
                                break;
                            }

                            entry += next;
                        }
                    }

                    if (Marshal.GetLastPInvokeError() != ErrorNoMoreFiles)
                    {
                        errors++;
                    }
                }
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        return new WalkResult(files, directories, bytes, allocated, errors, Log.Ms(started));
    }
}
