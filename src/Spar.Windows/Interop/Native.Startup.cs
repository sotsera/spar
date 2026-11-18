using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Spar.Windows.Interop;

// The imports of the start-up checks and of single instance (plan section 3, "Missing prerequisites" and "Native
// services": single instance), declared from the Windows SDK headers (winternl.h, fileapi.h, namedpipeapi.h,
// sddl.h, processthreadsapi.h). They take and return SafeHandles, which CsWin32 does not generate with marshalling
// off. Every path passed to them is already prefixed for long paths by the caller.
internal static partial class Native
{
    /// <summary><c>SDDL_REVISION_1</c>.</summary>
    public const uint SddlRevision1 = 1;

    /// <summary><c>PIPE_ACCESS_DUPLEX | FILE_FLAG_FIRST_PIPE_INSTANCE | FILE_FLAG_OVERLAPPED</c>.</summary>
    public const uint PipeServerOpenMode = 0x00000003 | 0x00080000 | 0x40000000;

    /// <summary><c>PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS</c>.</summary>
    public const uint PipeServerMode = 0x00000000 | 0x00000000 | 0x00000000 | 0x00000008;

    /// <summary><c>FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE</c>.</summary>
    public const uint ShareAll = 0x1 | 0x2 | 0x4;

    /// <summary><c>OPEN_EXISTING</c>.</summary>
    public const uint OpenExisting = 3;

    /// <summary><c>GENERIC_READ</c>.</summary>
    public const uint GenericRead = 0x80000000;

    /// <summary><c>GENERIC_WRITE</c>.</summary>
    public const uint GenericWrite = 0x40000000;

    /// <summary><c>FILE_FLAG_OVERLAPPED</c>: the handle serves asynchronous reads and writes.</summary>
    public const uint FileFlagOverlapped = 0x40000000;

    /// <summary><c>ERROR_ACCESS_DENIED</c>.</summary>
    public const int ErrorAccessDenied = 5;

    /// <summary><c>ERROR_PIPE_BUSY</c>.</summary>
    public const int ErrorPipeBusy = 231;

    /// <summary><c>PROCESS_QUERY_LIMITED_INFORMATION</c>.</summary>
    public const uint ProcessQueryLimitedInformation = 0x1000;

    /// <summary><c>TOKEN_QUERY</c>.</summary>
    public const uint TokenQuery = 0x0008;

    /// <summary><c>TokenUser</c> of <c>TOKEN_INFORMATION_CLASS</c>: a <c>TOKEN_USER</c>, whose first field points at the user's SID.</summary>
    public const int TokenUser = 1;

    /// <summary>The real version of Windows, whatever the manifest says (<c>RTL_OSVERSIONINFOW</c>). 0 is success.</summary>
    [LibraryImport("ntdll")]
    public static unsafe partial int RtlGetVersion(OsVersionInfo* versionInformation);

    /// <summary><c>CreateFileW</c>: with no access rights it reads a file's identity while another process holds it; also <c>CONOUT$</c>.</summary>
    [LibraryImport("kernel32", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, nint securityAttributes, uint creationDisposition, uint flagsAndAttributes, nint templateFile);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetFileInformationByHandle(SafeFileHandle file, out FileIdentity information);

    /// <summary><c>CreateNamedPipeW</c>; an invalid handle on failure, with the last error set.</summary>
    [LibraryImport("kernel32", EntryPoint = "CreateNamedPipeW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial SafePipeHandle CreateNamedPipe(string name, uint openMode, uint pipeMode, uint maxInstances, uint outBufferSize, uint inBufferSize, uint defaultTimeout, in SecurityAttributes securityAttributes);

    /// <summary>A security descriptor from SDDL, which the caller frees with <see cref="LocalFree"/>.</summary>
    [LibraryImport("advapi32", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ConvertStringSecurityDescriptorToSecurityDescriptor(string descriptor, uint revision, out nint securityDescriptor, nint size);

    [LibraryImport("kernel32")]
    public static partial nint LocalFree(nint memory);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetNamedPipeServerProcessId(SafeFileHandle pipe, out uint serverProcessId);

    /// <summary>Waits for a busy pipe instance; false on timeout or when the pipe does not exist.</summary>
    [LibraryImport("kernel32", EntryPoint = "WaitNamedPipeW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WaitNamedPipe(string name, uint timeout);

    [LibraryImport("kernel32", SetLastError = true)]
    public static partial nint OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [LibraryImport("advapi32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenProcessToken(nint process, uint desiredAccess, out nint token);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);

    [LibraryImport("advapi32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool GetTokenInformation(nint token, int informationClass, void* information, uint length, out uint returnLength);

    /// <summary>A SID as text (<c>S-1-5-…</c>), in a buffer the caller frees with <see cref="LocalFree"/>.</summary>
    [LibraryImport("advapi32", EntryPoint = "ConvertSidToStringSidW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ConvertSidToStringSid(nint sid, out nint stringSid);

    [LibraryImport("kernel32")]
    public static partial nint GetCurrentProcess();
}

/// <summary><c>RTL_OSVERSIONINFOW</c>: 276 bytes; the size field must be set before the call.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct OsVersionInfo
{
    public uint Size;
    public uint Major;
    public uint Minor;
    public uint Build;
    public uint PlatformId;
    public ServicePack ServicePackName;

    /// <summary><c>szCSDVersion</c>, 128 UTF-16 units.</summary>
    [InlineArray(128)]
    public struct ServicePack
    {
        private char _first;
    }
}

/// <summary>
/// <c>BY_HANDLE_FILE_INFORMATION</c>: 52 bytes, its <c>FILETIME</c>s 4-byte aligned (hence <c>Pack = 4</c>); the volume serial
/// number and file index identify the file.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct FileIdentity
{
    public uint FileAttributes;
    public long CreationTime;
    public long LastAccessTime;
    public long LastWriteTime;
    public uint VolumeSerialNumber;
    public uint FileSizeHigh;
    public uint FileSizeLow;
    public uint NumberOfLinks;
    public uint FileIndexHigh;
    public uint FileIndexLow;
}

/// <summary><c>SECURITY_ATTRIBUTES</c>: the descriptor is borrowed for the call.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SecurityAttributes
{
    public uint Length;
    public nint SecurityDescriptor;
    public int InheritHandle;
}
