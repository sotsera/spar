#if PART_DPAPI
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace NativeServices.App.Credentials;

// From the Windows SDK 10.0.26100.0 um/dpapi.h (DATA_BLOB is CRYPT_INTEGER_BLOB in wincrypt.h).
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct DataBlob
{
    public uint Size;
    public byte* Data;
}

/// <summary>CryptProtectData / CryptUnprotectData, current-user scope, never prompting.</summary>
internal static unsafe partial class Dpapi
{
    public const uint UiForbidden = 0x1;   // CRYPTPROTECT_UI_FORBIDDEN
    public const uint LocalMachine = 0x4;  // CRYPTPROTECT_LOCAL_MACHINE (any user on the machine can decrypt)

    public static byte[] Protect(ReadOnlySpan<byte> plain, ReadOnlySpan<byte> entropy, string? description = null)
    {
        fixed (byte* plainPointer = plain)
        fixed (byte* entropyPointer = entropy)
        {
            var input = new DataBlob { Size = (uint)plain.Length, Data = plainPointer };
            var extra = new DataBlob { Size = (uint)entropy.Length, Data = entropyPointer };
            DataBlob output;
            if (!CryptProtectData(&input, description, entropy.IsEmpty ? null : &extra, 0, 0, UiForbidden, &output))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "CryptProtectData");
            }

            return TakeOutput(output, wipe: false);
        }
    }

    public static byte[] Unprotect(ReadOnlySpan<byte> sealedData, ReadOnlySpan<byte> entropy)
    {
        fixed (byte* sealedPointer = sealedData)
        fixed (byte* entropyPointer = entropy)
        {
            var input = new DataBlob { Size = (uint)sealedData.Length, Data = sealedPointer };
            var extra = new DataBlob { Size = (uint)entropy.Length, Data = entropyPointer };
            DataBlob output;
            if (!CryptUnprotectData(&input, 0, entropy.IsEmpty ? null : &extra, 0, 0, UiForbidden, &output))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "CryptUnprotectData");
            }

            return TakeOutput(output, wipe: true);
        }
    }

    // The output buffer is LocalAlloc'ed by DPAPI and owned by the caller: copy, optionally wipe, LocalFree.
    private static byte[] TakeOutput(DataBlob output, bool wipe)
    {
        try
        {
            var span = new Span<byte>(output.Data, (int)output.Size);
            var copy = span.ToArray();
            if (wipe)
            {
                span.Clear();
            }

            return copy;
        }
        finally
        {
            LocalFree(output.Data);
        }
    }

    // BOOL CryptProtectData(DATA_BLOB* pDataIn, LPCWSTR szDataDescr, DATA_BLOB* pOptionalEntropy, PVOID pvReserved,
    //     CRYPTPROTECT_PROMPTSTRUCT* pPromptStruct, DWORD dwFlags, DATA_BLOB* pDataOut)
    [LibraryImport("crypt32", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptProtectData(DataBlob* input, string? description, DataBlob* entropy,
        nint reserved, nint prompt, uint flags, DataBlob* output);

    // BOOL CryptUnprotectData(DATA_BLOB* pDataIn, LPWSTR* ppszDataDescr, DATA_BLOB* pOptionalEntropy, PVOID pvReserved,
    //     CRYPTPROTECT_PROMPTSTRUCT* pPromptStruct, DWORD dwFlags, DATA_BLOB* pDataOut)
    [LibraryImport("crypt32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptUnprotectData(DataBlob* input, nint description, DataBlob* entropy,
        nint reserved, nint prompt, uint flags, DataBlob* output);

    // HLOCAL LocalFree(HLOCAL hMem)
    [LibraryImport("kernel32")]
    private static partial nint LocalFree(void* memory);
}
#endif
