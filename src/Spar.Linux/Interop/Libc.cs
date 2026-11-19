using System.Runtime.InteropServices;

namespace Spar.Linux.Interop;

/// <summary>
/// The C library calls the host makes itself (glibc 2.33 or later on x86-64 and arm64, where <c>stat</c> is a real
/// symbol): the process environment before GTK starts, the single-instance socket, and the child processes of the
/// start-up checks. No variadic function is imported (<c>open</c>, <c>fcntl</c> and <c>ioctl</c> are not): files are
/// opened through .NET. Calls that set <c>errno</c> are imported with <c>SetLastError</c>, read with
/// <see cref="Marshal.GetLastPInvokeError"/>.
/// </summary>
internal static unsafe partial class Libc
{
    public const int EIntr = 4;
    public const int EAgain = 11;
    public const int ENoEnt = 2;
    public const int EAccess = 13;
    public const int EConnRefused = 111;

    public const int AfUnix = 1;
    public const int SockStream = 1;

    /// <summary><c>SOCK_CLOEXEC</c>: no child process (WebKit's, the app's) inherits the socket.</summary>
    public const int SockCloexec = 0x80000;

    public const int SolSocket = 1;
    public const int SoPeerCred = 17;
    public const int SoReceiveTimeout = 20;
    public const int SoSendTimeout = 21;

    /// <summary><c>MSG_NOSIGNAL</c>: a peer that went away is an error, not <c>SIGPIPE</c>.</summary>
    public const int MsgNoSignal = 0x4000;

    public const int ShutReadWrite = 2;
    public const int PollIn = 1;
    public const int WNoHang = 1;
    public const int SigKill = 9;
    public const int LockExclusive = 2;
    public const int LockNonBlocking = 4;
    public const int OCloexec = 0x80000;

    /// <summary>Room for glibc's <c>posix_spawn_file_actions_t</c> (80 bytes on x86-64 and arm64).</summary>
    public const int FileActionsBytes = 256;

    /// <summary><c>CLOCK_BOOTTIME</c>, the clock of the process start times in <c>/proc</c>.</summary>
    public const int ClockBootTime = 7;

    /// <summary><c>CLOCK_MONOTONIC</c>, the same in every process, for the single-instance frames' send time.</summary>
    public const int ClockMonotonic = 1;

    private const string Library = "libc";

    /// <summary>
    /// Sets a variable that native code reads (<c>getenv</c>) and child processes inherit; with
    /// <paramref name="overwrite"/> 0 a value already set stays. Not thread-safe against <c>getenv</c>: only before GTK,
    /// WebKit or GDBus start threads.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "setenv", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    public static partial int SetEnv(string name, string value, int overwrite);

    /// <summary>The current value (borrowed), or 0; it sees what <see cref="SetEnv"/> set, unlike .NET's copy.</summary>
    [LibraryImport(Library, EntryPoint = "getenv", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint GetEnv(string name);

    [LibraryImport(Library, EntryPoint = "geteuid")]
    public static partial uint GetEffectiveUserId();

    [LibraryImport(Library, EntryPoint = "clock_gettime")]
    public static partial int ClockGetTime(int clock, Timespec* time);

    /// <summary><c>stat</c> into a buffer of at least 256 bytes: device then inode come first on x86-64 and arm64.</summary>
    [LibraryImport(Library, EntryPoint = "stat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    public static partial int Stat(string path, byte* buffer);

    [LibraryImport(Library, EntryPoint = "flock", SetLastError = true)]
    public static partial int Flock(int descriptor, int operation);

    [LibraryImport(Library, EntryPoint = "close", SetLastError = true)]
    public static partial int Close(int descriptor);

    [LibraryImport(Library, EntryPoint = "read", SetLastError = true)]
    public static partial nint Read(int descriptor, byte* buffer, nuint count);

    [LibraryImport(Library, EntryPoint = "pipe2", SetLastError = true)]
    public static partial int Pipe2(int* descriptors, int flags);

    [LibraryImport(Library, EntryPoint = "poll", SetLastError = true)]
    public static partial int Poll(PollDescriptor* descriptors, nuint count, int timeoutMilliseconds);

    [LibraryImport(Library, EntryPoint = "unlink", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    public static partial int Unlink(string path);

    [LibraryImport(Library, EntryPoint = "chmod", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    public static partial int Chmod(string path, uint mode);

    [LibraryImport(Library, EntryPoint = "socket", SetLastError = true)]
    public static partial int Socket(int domain, int type, int protocol);

    [LibraryImport(Library, EntryPoint = "bind", SetLastError = true)]
    public static partial int Bind(int socket, UnixAddress* address, uint length);

    [LibraryImport(Library, EntryPoint = "listen", SetLastError = true)]
    public static partial int Listen(int socket, int backlog);

    [LibraryImport(Library, EntryPoint = "accept4", SetLastError = true)]
    public static partial int Accept4(int socket, void* address, uint* length, int flags);

    [LibraryImport(Library, EntryPoint = "connect", SetLastError = true)]
    public static partial int Connect(int socket, UnixAddress* address, uint length);

    [LibraryImport(Library, EntryPoint = "shutdown", SetLastError = true)]
    public static partial int Shutdown(int socket, int how);

    [LibraryImport(Library, EntryPoint = "send", SetLastError = true)]
    public static partial nint Send(int socket, byte* buffer, nuint length, int flags);

    [LibraryImport(Library, EntryPoint = "recv", SetLastError = true)]
    public static partial nint Receive(int socket, byte* buffer, nuint length, int flags);

    [LibraryImport(Library, EntryPoint = "setsockopt", SetLastError = true)]
    public static partial int SetSocketOption(int socket, int level, int name, void* value, uint length);

    [LibraryImport(Library, EntryPoint = "getsockopt", SetLastError = true)]
    public static partial int GetSocketOption(int socket, int level, int name, void* value, uint* length);

    /// <summary>
    /// Starts <paramref name="file"/>, found on <c>PATH</c>, with <paramref name="arguments"/> and
    /// <paramref name="environment"/> (both null-terminated arrays); returns 0 or an error number, never sets errno.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "posix_spawnp", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int SpawnP(int* processId, string file, void* fileActions, void* attributes, nint* arguments, nint* environment);

    /// <summary>Prepares <see cref="FileActionsBytes"/> of memory as a <c>posix_spawn_file_actions_t</c>.</summary>
    [LibraryImport(Library, EntryPoint = "posix_spawn_file_actions_init")]
    public static partial int FileActionsInit(void* actions);

    [LibraryImport(Library, EntryPoint = "posix_spawn_file_actions_adddup2")]
    public static partial int FileActionsAddDup2(void* actions, int descriptor, int newDescriptor);

    [LibraryImport(Library, EntryPoint = "posix_spawn_file_actions_addopen", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int FileActionsAddOpen(void* actions, int descriptor, string path, int flags, uint mode);

    [LibraryImport(Library, EntryPoint = "posix_spawn_file_actions_destroy")]
    public static partial int FileActionsDestroy(void* actions);

    [LibraryImport(Library, EntryPoint = "waitpid", SetLastError = true)]
    public static partial int WaitPid(int processId, int* status, int options);

    [LibraryImport(Library, EntryPoint = "kill", SetLastError = true)]
    public static partial int Kill(int processId, int signal);

    /// <summary>The process's current environment (<c>environ</c>), as <c>posix_spawnp</c> wants it: what setenv changed included.</summary>
    public static nint* Environment()
    {
        var library = NativeLibrary.Load("libc.so.6");
        return *(nint**)NativeLibrary.GetExport(library, "environ");
    }

    /// <summary>The value of a variable in the process's environment as native code sees it, or null.</summary>
    public static string? Variable(string name) => Marshal.PtrToStringUTF8(GetEnv(name));

    /// <summary>A time of <see cref="ClockMonotonic"/> in milliseconds, comparable between processes.</summary>
    public static long MonotonicMilliseconds()
    {
        Timespec now;
        return ClockGetTime(ClockMonotonic, &now) == 0 ? (now.Seconds * 1_000) + (now.Nanoseconds / 1_000_000) : 0;
    }

    /// <summary>A <c>timespec</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Timespec
    {
        public long Seconds;
        public long Nanoseconds;
    }

    /// <summary>A <c>timeval</c>, for the socket timeouts.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Timeval
    {
        public long Seconds;
        public long Microseconds;
    }

    /// <summary>A <c>struct pollfd</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PollDescriptor
    {
        public int Descriptor;
        public short Events;
        public short ReturnedEvents;
    }

    /// <summary>A <c>struct ucred</c>: the peer's process, user and group, as the kernel saw them at connect.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PeerCredentials
    {
        public int ProcessId;
        public uint UserId;
        public uint GroupId;
    }

    /// <summary>
    /// A <c>struct sockaddr_un</c>: the family, then 108 bytes of path; an abstract name starts with a zero byte, and
    /// its length is the address's length (not a terminator).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UnixAddress
    {
        public const int PathBytes = 108;

        public ushort Family;
        public fixed byte Path[PathBytes];
    }
}
