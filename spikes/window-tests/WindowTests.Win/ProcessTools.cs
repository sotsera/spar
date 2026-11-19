using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Spar.WindowTests;

/// <summary>
/// Finds the browser processes of one user-data folder by command line (every WebView2 process carries the folder in
/// <c>--user-data-dir=</c> or the crashpad <c>--database=</c>), so only this spike's own processes are ever watched.
/// </summary>
internal static partial class ProcessTools
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint Synchronize = 0x00100000;
    private const int ProcessCommandLineInformation = 60;

    public sealed record BrowserProcess(int Id, string Kind, nint Handle, long StartFileTime);

    public sealed record MemorySample(int Id, string Kind, long PrivateBytes, long WorkingSet);

    /// <summary>Every msedgewebview2 process whose command line contains <paramref name="marker"/> (case-insensitive).</summary>
    public static List<BrowserProcess> Find(string marker, bool keepHandles)
    {
        var found = new List<BrowserProcess>();
        foreach (var process in Process.GetProcessesByName("msedgewebview2"))
        {
            using (process)
            {
                var handle = OpenProcess(ProcessQueryLimitedInformation | Synchronize, false, (uint)process.Id);
                if (handle == 0)
                {
                    continue;
                }

                var commandLine = CommandLine(handle);
                if (commandLine is null || !commandLine.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    CloseHandle(handle);
                    continue;
                }

                GetProcessTimes(handle, out var created, out _, out _, out _);
                found.Add(new BrowserProcess(process.Id, KindOf(commandLine), keepHandles ? handle : 0, created));
                if (!keepHandles)
                {
                    CloseHandle(handle);
                }
            }
        }

        return found;
    }

    public static List<MemorySample> Memory(string marker)
    {
        var samples = new List<MemorySample>();
        foreach (var browser in Find(marker, keepHandles: false))
        {
            try
            {
                using var process = Process.GetProcessById(browser.Id);
                samples.Add(new MemorySample(browser.Id, browser.Kind, process.PrivateMemorySize64, process.WorkingSet64));
            }
            catch (ArgumentException)
            {
                // Exited between the scan and the sample.
            }
        }

        return samples;
    }

    /// <summary>Waits for the process and returns its exit time (FILETIME, UTC), or null if it is still running.</summary>
    public static long? WaitForExit(nint handle, int timeoutMilliseconds)
    {
        if (WaitForSingleObject(handle, (uint)timeoutMilliseconds) != 0)
        {
            return null;
        }

        GetProcessTimes(handle, out _, out var exited, out _, out _);
        return exited;
    }

    public static long? ExitTime(nint handle)
    {
        if (WaitForSingleObject(handle, 0) != 0)
        {
            return null;
        }

        GetProcessTimes(handle, out _, out var exited, out _, out _);
        return exited;
    }

    public static nint Open(int processId) => OpenProcess(ProcessQueryLimitedInformation | Synchronize, false, (uint)processId);

    public static void Close(nint handle)
    {
        if (handle != 0)
        {
            CloseHandle(handle);
        }
    }

    private static string KindOf(string commandLine)
    {
        var index = commandLine.IndexOf("--type=", StringComparison.Ordinal);
        if (index < 0)
        {
            return "browser";
        }

        var end = commandLine.IndexOf(' ', index);
        var kind = (end < 0 ? commandLine[(index + 7)..] : commandLine[(index + 7)..end]).Trim('"');
        if (kind == "utility")
        {
            var sub = commandLine.IndexOf("--utility-sub-type=", StringComparison.Ordinal);
            if (sub >= 0)
            {
                var subEnd = commandLine.IndexOf(' ', sub);
                var name = subEnd < 0 ? commandLine[(sub + 19)..] : commandLine[(sub + 19)..subEnd];
                return "utility:" + name.Split('.').Last().Trim('"');
            }
        }

        return kind;
    }

    private static unsafe string? CommandLine(nint handle)
    {
        var buffer = new byte[8192];
        fixed (byte* data = buffer)
        {
            var status = NtQueryInformationProcess(handle, ProcessCommandLineInformation, (nint)data, (uint)buffer.Length, out var needed);
            if (status < 0)
            {
                if (needed == 0 || needed > 1 << 20)
                {
                    return null;
                }

                buffer = new byte[needed];
                fixed (byte* retry = buffer)
                {
                    if (NtQueryInformationProcess(handle, ProcessCommandLineInformation, (nint)retry, needed, out _) < 0)
                    {
                        return null;
                    }

                    return ReadUnicodeString(retry);
                }
            }

            return ReadUnicodeString(data);
        }
    }

    // UNICODE_STRING { USHORT Length; USHORT MaximumLength; PWSTR Buffer; } followed by the characters.
    private static unsafe string ReadUnicodeString(byte* data)
    {
        var length = *(ushort*)data;
        var text = *(char**)(data + 8);
        return new string(text, 0, length / 2);
    }

    [LibraryImport("kernel32", SetLastError = true)]
    private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [LibraryImport("kernel32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32")]
    private static partial uint WaitForSingleObject(nint handle, uint milliseconds);

    [LibraryImport("kernel32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessTimes(nint handle, out long creation, out long exit, out long kernel, out long user);

    [LibraryImport("ntdll")]
    private static partial int NtQueryInformationProcess(nint handle, int informationClass, nint information, uint length, out uint returned);
}
