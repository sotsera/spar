using System.Runtime.InteropServices;
using System.Text;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security.Credentials;

namespace Spar.Windows;

/// <summary>
/// Credentials on Windows (decision "M3c's decide-first questions", item 6): Credential Manager's generic credentials,
/// target <c>&lt;app id&gt;/&lt;name&gt;</c>, the secret as UTF-8, at most 2,560 bytes (<c>CRED_MAX_CREDENTIAL_BLOB_SIZE</c>).
/// Kept for this machine (<c>CRED_PERSIST_LOCAL_MACHINE</c>) unless the app chose <c>Roaming()</c>
/// (<c>CRED_PERSIST_ENTERPRISE</c>, which travels with a roaming profile). Each call runs on the thread pool; the
/// secret's bytes are cleared once written or read.
/// </summary>
internal sealed class WindowsCredentials(string appId, bool roaming) : ICredentialStore
{
    private const uint ErrorNotFound = 1168;

    public Task<Result<CredentialError>> SetAsync(string name, string secret, CancellationToken cancellationToken)
    {
        CredentialNames.Check(name);
        ArgumentNullException.ThrowIfNull(secret);
        if (CredentialNames.TooLarge(secret))
        {
            return Task.FromResult<Result<CredentialError>>(new CredentialError.TooLarge(ICredentialStore.MaxSecretBytes));
        }

        return Task.Run(() => Write(Target(name), secret), cancellationToken);
    }

    public Task<Result<string, CredentialError>> GetAsync(string name, CancellationToken cancellationToken)
    {
        CredentialNames.Check(name);
        return Task.Run(() => Read(Target(name)), cancellationToken);
    }

    public async Task<Result<bool, CredentialError>> ExistsAsync(string name, CancellationToken cancellationToken)
    {
        var read = await GetAsync(name, cancellationToken);
        return read.TryGetError(out var error) ? error is CredentialError.NotFound ? false : error : true;
    }

    public Task<Result<CredentialError>> DeleteAsync(string name, CancellationToken cancellationToken)
    {
        CredentialNames.Check(name);
        return Task.Run(() => Delete(Target(name)), cancellationToken);
    }

    private string Target(string name) => $"{appId}/{name}";

    private unsafe Result<CredentialError> Write(string target, string secret)
    {
        var bytes = Encoding.UTF8.GetBytes(secret);
        try
        {
            fixed (char* targetName = target)
            fixed (char* userName = appId)
            fixed (byte* blob = bytes)
            {
                var credential = new CREDENTIALW
                {
                    Type = CRED_TYPE.CRED_TYPE_GENERIC,
                    TargetName = targetName,
                    UserName = userName,
                    CredentialBlobSize = (uint)bytes.Length,
                    CredentialBlob = blob,
                    Persist = roaming ? CRED_PERSIST.CRED_PERSIST_ENTERPRISE : CRED_PERSIST.CRED_PERSIST_LOCAL_MACHINE,
                };
                return PInvoke.CredWrite(&credential, 0) ? Result.Success : Failure("CredWrite");
            }
        }
        finally
        {
            Array.Clear(bytes);
        }
    }

    private static unsafe Result<string, CredentialError> Read(string target)
    {
        CREDENTIALW* credential;
        fixed (char* targetName = target)
        {
            if (!PInvoke.CredRead(new PCWSTR(targetName), CRED_TYPE.CRED_TYPE_GENERIC, 0, &credential))
            {
                return Marshal.GetLastPInvokeError() == ErrorNotFound ? new CredentialError.NotFound() : Failure("CredRead");
            }
        }

        try
        {
            var blob = new Span<byte>(credential->CredentialBlob, (int)credential->CredentialBlobSize);
            var secret = Encoding.UTF8.GetString(blob);
            blob.Clear();
            return secret;
        }
        finally
        {
            PInvoke.CredFree(credential);
        }
    }

    private static Result<CredentialError> Delete(string target)
    {
        if (PInvoke.CredDelete(target, CRED_TYPE.CRED_TYPE_GENERIC))
        {
            return Result.Success;
        }

        // Deleting one that is not there is no error.
        return Marshal.GetLastPInvokeError() == ErrorNotFound ? Result.Success : Failure("CredDelete");
    }

    private static CredentialError.Unavailable Failure(string call) =>
        new($"Credential Manager's {call} failed with error {Marshal.GetLastPInvokeError()}.");
}
