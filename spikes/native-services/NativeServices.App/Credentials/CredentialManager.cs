#if PART_CREDMAN
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace NativeServices.App.Credentials;

// From the Windows SDK 10.0.26100.0 um/wincred.h. x64 layout: 80 bytes (checked at run time).
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeCredential
{
    public uint Flags;
    public uint Type;
    public char* TargetName;
    public char* Comment;
    public long LastWritten; // FILETIME, two DWORDs; 4-byte aligned in the header, at offset 24 on x64 either way
    public uint CredentialBlobSize;
    public byte* CredentialBlob;
    public uint Persist;
    public uint AttributeCount;
    public nint Attributes;
    public char* TargetAlias;
    public char* UserName;
}

internal enum CredentialPersist : uint
{
    Session = 1,      // CRED_PERSIST_SESSION: this logon session only
    LocalMachine = 2, // CRED_PERSIST_LOCAL_MACHINE: this user on this machine, survives logoff and reboot
    Enterprise = 3,   // CRED_PERSIST_ENTERPRISE: roams with the profile
}

internal sealed record StoredCredential(string Target, string? UserName, byte[] Secret, CredentialPersist Persist,
    DateTime LastWrittenUtc);

internal static unsafe partial class CredentialManager
{
    public const uint TypeGeneric = 1;                // CRED_TYPE_GENERIC
    public const int MaxBlobBytes = 5 * 512;          // CRED_MAX_CREDENTIAL_BLOB_SIZE
    public const int ErrorNotFound = 1168;            // ERROR_NOT_FOUND

    public static void Write(string target, string? userName, ReadOnlySpan<byte> secret, CredentialPersist persist)
    {
        fixed (char* targetPointer = target)
        fixed (char* userPointer = userName)
        fixed (byte* secretPointer = secret)
        {
            var credential = new NativeCredential
            {
                Type = TypeGeneric,
                TargetName = targetPointer,
                UserName = userPointer,
                CredentialBlobSize = (uint)secret.Length,
                CredentialBlob = secretPointer,
                Persist = (uint)persist,
            };
            if (!CredWriteW(&credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), $"CredWriteW({target})");
            }
        }
    }

    public static StoredCredential? Read(string target)
    {
        if (!CredReadW(target, TypeGeneric, 0, out var credential))
        {
            var error = Marshal.GetLastPInvokeError();
            return error == ErrorNotFound ? null : throw new Win32Exception(error, $"CredReadW({target})");
        }

        try
        {
            // The blob is owned by the CredReadW allocation; copy it out, then wipe and free.
            var secret = new Span<byte>(credential->CredentialBlob, (int)credential->CredentialBlobSize);
            var copy = secret.ToArray();
            secret.Clear();
            return new StoredCredential(
                new string(credential->TargetName),
                credential->UserName is null ? null : new string(credential->UserName),
                copy,
                (CredentialPersist)credential->Persist,
                DateTime.FromFileTimeUtc(credential->LastWritten));
        }
        finally
        {
            CredFree(credential);
        }
    }

    public static bool Delete(string target)
    {
        if (CredDeleteW(target, TypeGeneric, 0))
        {
            return true;
        }

        var error = Marshal.GetLastPInvokeError();
        return error == ErrorNotFound ? false : throw new Win32Exception(error, $"CredDeleteW({target})");
    }

    /// <summary>Target names matching <paramref name="filter"/> (a trailing * is the only wildcard).</summary>
    public static List<string> Enumerate(string filter)
    {
        var targets = new List<string>();
        if (!CredEnumerateW(filter, 0, out var count, out var credentials))
        {
            var error = Marshal.GetLastPInvokeError();
            return error == ErrorNotFound ? targets : throw new Win32Exception(error, $"CredEnumerateW({filter})");
        }

        try
        {
            for (var i = 0; i < count; i++)
            {
                targets.Add(new string(credentials[i]->TargetName));
            }
        }
        finally
        {
            CredFree(credentials);
        }

        return targets;
    }

    // BOOL CredWriteW(PCREDENTIALW Credential, DWORD Flags)
    [LibraryImport("advapi32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredWriteW(NativeCredential* credential, uint flags);

    // BOOL CredReadW(LPCWSTR TargetName, DWORD Type, DWORD Flags, PCREDENTIALW* Credential)
    [LibraryImport("advapi32", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredReadW(string target, uint type, uint flags, out NativeCredential* credential);

    // BOOL CredDeleteW(LPCWSTR TargetName, DWORD Type, DWORD Flags)
    [LibraryImport("advapi32", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredDeleteW(string target, uint type, uint flags);

    // BOOL CredEnumerateW(LPCWSTR Filter, DWORD Flags, DWORD* Count, PCREDENTIALW** Credential)
    [LibraryImport("advapi32", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredEnumerateW(string? filter, uint flags, out uint count,
        out NativeCredential** credentials);

    // VOID CredFree(PVOID Buffer)
    [LibraryImport("advapi32")]
    private static partial void CredFree(void* buffer);
}
#endif
