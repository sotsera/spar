using System.Runtime.InteropServices;

namespace Spike.BlackBoxAtSpi.Native;

/// <summary>libc, as the process experiments use it (x86-64 glibc).</summary>
internal static unsafe partial class Libc
{
    public const int SigKill = 9;
    public const int SigTerm = 15;
    public const int SigCont = 18;
    public const int SigStop = 19;

    /// <summary><c>PR_SET_PDEATHSIG</c>.</summary>
    public const int PrSetParentDeathSignal = 1;

    /// <summary><c>PR_SET_CHILD_SUBREAPER</c>.</summary>
    public const int PrSetChildSubreaper = 36;

    /// <summary><c>PR_GET_CHILD_SUBREAPER</c>.</summary>
    public const int PrGetChildSubreaper = 37;

    /// <summary><c>SYS_pidfd_open</c> on x86-64.</summary>
    public const long SysPidfdOpen = 434;

    [LibraryImport("libc", SetLastError = true)]
    public static partial int kill(int pid, int signal);

    /// <summary>For variables native code reads (GIO's <c>DBUS_SESSION_BUS_ADDRESS</c>): .NET's own setter reaches no <c>getenv</c>.</summary>
    [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int setenv(string name, string value, int overwrite);

    [LibraryImport("libc", SetLastError = true)]
    public static partial int prctl(int option, nuint arg2, nuint arg3, nuint arg4, nuint arg5);

    [LibraryImport("libc")]
    public static partial int getpid();

    [LibraryImport("libc", SetLastError = true)]
    public static partial long syscall(long number, long arg1, long arg2);

    [LibraryImport("libc", SetLastError = true)]
    public static partial int poll(PollFd* fds, nuint count, int timeoutMilliseconds);

    [LibraryImport("libc", SetLastError = true)]
    public static partial int close(int fd);

    [LibraryImport("libc", SetLastError = true)]
    public static partial int waitpid(int pid, int* status, int options);

    [StructLayout(LayoutKind.Sequential)]
    public struct PollFd
    {
        public int Fd;
        public short Events;
        public short Returned;
    }
}
