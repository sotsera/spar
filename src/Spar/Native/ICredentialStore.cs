namespace Spar;

/// <summary>Why a credential could not be stored or read.</summary>
public closed record CredentialError
{
    /// <summary>No credential of that name is stored.</summary>
    public sealed record NotFound : CredentialError;

    /// <summary>The secret is larger than the store takes, in UTF-8 bytes (2,560 on every OS).</summary>
    public sealed record TooLarge(int Limit) : CredentialError;

    /// <summary>
    /// No secure store answers: on Linux no Secret Service on the session bus, or a keyring that is locked and cannot
    /// ask the user to unlock it. Spar never falls back to a file of its own.
    /// </summary>
    public sealed record Unavailable(string Reason) : CredentialError;
}

/// <summary>
/// Secrets the app keeps for the user, such as a database password (decision "M3c's decide-first questions", item 6):
/// the OS's own store, Credential Manager on Windows and the Secret Service on Linux, never plain text. C# only: the
/// page reaches a secret only through the app's own commands, which store it after their own checks and never send it
/// back. Each secret has a name of the app's choosing, scoped by the app id. Under the test host it is a fake that
/// keeps secrets in memory (<c>app.Credentials</c>).
/// </summary>
public interface ICredentialStore
{
    /// <summary>The largest secret, in UTF-8 bytes: Credential Manager's limit, applied on every OS so an app behaves the same.</summary>
    const int MaxSecretBytes = 2560;

    /// <summary>Stores <paramref name="secret"/> under <paramref name="name"/>, replacing one stored before.</summary>
    Task<Result<CredentialError>> SetAsync(string name, string secret, CancellationToken cancellationToken);

    /// <summary>The secret stored under <paramref name="name"/>.</summary>
    Task<Result<string, CredentialError>> GetAsync(string name, CancellationToken cancellationToken);

    /// <summary>Whether a secret is stored under <paramref name="name"/>, without reading it.</summary>
    Task<Result<bool, CredentialError>> ExistsAsync(string name, CancellationToken cancellationToken);

    /// <summary>Deletes the secret stored under <paramref name="name"/>; deleting one that is not there is no error.</summary>
    Task<Result<CredentialError>> DeleteAsync(string name, CancellationToken cancellationToken);
}

/// <summary>How the app's credentials are kept (<c>spar.Credentials(c => …)</c> in <c>Program.cs</c>).</summary>
public sealed class CredentialOptions
{
    /// <summary>Whether a credential travels with the user's roaming profile (Windows); false by default.</summary>
    public bool IsRoaming { get; private set; }

    /// <summary>
    /// On Windows, keeps each credential with <c>CRED_PERSIST_ENTERPRISE</c>, so it travels with a roaming profile: for
    /// a terminal-server farm whose users move between servers. Without it a credential stays on this machine. The
    /// secret then reaches every machine the profile does; choose it only where the profile is the user's own. No
    /// effect on Linux, where the Secret Service keeps the user's keyring.
    /// </summary>
    public CredentialOptions Roaming()
    {
        IsRoaming = true;
        return this;
    }
}

/// <summary>The store of a host that has none (a page in a browser on loopback): every call is unavailable.</summary>
internal sealed class NoCredentialStore : ICredentialStore
{
    private const string Reason = "This host has no credential store: run the app in its window.";

    public Task<Result<CredentialError>> SetAsync(string name, string secret, CancellationToken cancellationToken) =>
        Task.FromResult<Result<CredentialError>>(new CredentialError.Unavailable(Reason));

    public Task<Result<string, CredentialError>> GetAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult<Result<string, CredentialError>>(new CredentialError.Unavailable(Reason));

    public Task<Result<bool, CredentialError>> ExistsAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult<Result<bool, CredentialError>>(new CredentialError.Unavailable(Reason));

    public Task<Result<CredentialError>> DeleteAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult<Result<CredentialError>>(new CredentialError.Unavailable(Reason));
}

/// <summary>A credential's name as the stores take it: the app's own, checked the same way on every OS.</summary>
internal static class CredentialNames
{
    /// <summary>The longest name, in characters; the store's key adds the app id in front.</summary>
    public const int MaxLength = 256;

    public static void Check(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > MaxLength || name.Any(char.IsControl))
        {
            throw new ArgumentException($"A credential's name is at most {MaxLength} characters, without control characters.", nameof(name));
        }
    }

    /// <summary>Whether the secret is past <see cref="ICredentialStore.MaxSecretBytes"/>.</summary>
    public static bool TooLarge(string secret) => System.Text.Encoding.UTF8.GetByteCount(secret) > ICredentialStore.MaxSecretBytes;
}
