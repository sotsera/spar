using System.Runtime.InteropServices;
using Spar.Linux.Interop;

namespace Spar.Linux;

/// <summary>
/// Credentials on Linux (decision "M3c's decide-first questions", item 6): the Secret Service through libsecret, in
/// the user's default keyring, with a schema named by the app id and one attribute, the credential's name. Without a
/// Secret Service on the session bus, or with a locked keyring that cannot ask the user to unlock it, a call is
/// <see cref="CredentialError.Unavailable"/> with the service's reason; Spar never falls back to a file. Each call
/// waits for the service, so it runs on the thread pool, never on the main loop.
/// </summary>
internal sealed class LinuxCredentials(string appId) : ICredentialStore
{
    private const string NameAttribute = "name";

    public Task<Result<CredentialError>> SetAsync(string name, string secret, CancellationToken cancellationToken)
    {
        CredentialNames.Check(name);
        ArgumentNullException.ThrowIfNull(secret);
        if (CredentialNames.TooLarge(secret))
        {
            return Task.FromResult<Result<CredentialError>>(new CredentialError.TooLarge(ICredentialStore.MaxSecretBytes));
        }

        return Task.Run(() => Store(name, secret), cancellationToken);
    }

    public Task<Result<string, CredentialError>> GetAsync(string name, CancellationToken cancellationToken)
    {
        CredentialNames.Check(name);
        return Task.Run(() => Lookup(name), cancellationToken);
    }

    public async Task<Result<bool, CredentialError>> ExistsAsync(string name, CancellationToken cancellationToken)
    {
        var read = await GetAsync(name, cancellationToken);
        return read.TryGetError(out var error) ? error is CredentialError.NotFound ? false : error : true;
    }

    public Task<Result<CredentialError>> DeleteAsync(string name, CancellationToken cancellationToken)
    {
        CredentialNames.Check(name);
        return Task.Run(() => Clear(name), cancellationToken);
    }

    private unsafe Result<CredentialError> Store(string name, string secret)
    {
        using var call = new SecretCall(appId, name);
        nint error = 0;
        var stored = Secret.secret_password_storev_sync(
            call.Schema, call.Attributes, Secret.DefaultCollection, $"{appId}: {name}", secret, 0, &error);
        return stored != 0 ? Result.Success : Failure(error, "store");
    }

    private unsafe Result<string, CredentialError> Lookup(string name)
    {
        using var call = new SecretCall(appId, name);
        nint error = 0;
        var password = Secret.secret_password_lookupv_sync(call.Schema, call.Attributes, 0, &error);
        if (password == 0)
        {
            return error == 0 ? new CredentialError.NotFound() : Failure(error, "lookup");
        }

        try
        {
            return Marshal.PtrToStringUTF8(password) ?? "";
        }
        finally
        {
            Secret.secret_password_free(password);
        }
    }

    private unsafe Result<CredentialError> Clear(string name)
    {
        using var call = new SecretCall(appId, name);
        nint error = 0;

        // False with no error: nothing matched, which is no error for a delete.
        var cleared = Secret.secret_password_clearv_sync(call.Schema, call.Attributes, 0, &error);
        return cleared != 0 || error == 0 ? Result.Success : Failure(error, "clear");
    }

    private static CredentialError.Unavailable Failure(nint error, string what)
    {
        var message = GLib.ErrorMessage(error);
        GLib.g_error_free(error);
        return new CredentialError.Unavailable($"The Secret Service could not {what} the credential: {message}");
    }

    /// <summary>One call's schema and attributes, and the UTF-8 strings they point to, released together.</summary>
    private sealed class SecretCall : IDisposable
    {
        private readonly List<nint> _strings = [];

        public SecretCall(string appId, string name)
        {
            var (hash, equal) = Secret.StringFunctions();
            var types = Secret.g_hash_table_new(hash, equal);
            try
            {
                // The one attribute, a string (type 0, stored as a null value).
                Secret.g_hash_table_insert(types, Text(NameAttribute), 0);
                Schema = Secret.secret_schema_newv(appId, 0, types);
            }
            finally
            {
                Secret.g_hash_table_unref(types);
            }

            Attributes = Secret.g_hash_table_new(hash, equal);
            Secret.g_hash_table_insert(Attributes, Text(NameAttribute), Text(name));
        }

        public nint Schema { get; }

        public nint Attributes { get; }

        public void Dispose()
        {
            Secret.g_hash_table_unref(Attributes);
            Secret.secret_schema_unref(Schema);
            _strings.ForEach(Marshal.FreeCoTaskMem);
        }

        private nint Text(string value)
        {
            var text = Marshal.StringToCoTaskMemUTF8(value);
            _strings.Add(text);
            return text;
        }
    }
}
