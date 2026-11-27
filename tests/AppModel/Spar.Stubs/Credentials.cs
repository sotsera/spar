namespace Spar;

/// <summary>Why a credential could not be stored or read.</summary>
public closed record CredentialError
{
    public sealed record NotFound : CredentialError;

    /// <summary>The secret is larger than the store takes, in UTF-8 bytes (2,560 on every OS).</summary>
    public sealed record TooLarge(int Limit) : CredentialError;

    /// <summary>No secure store answers (on Linux no Secret Service, or a locked keyring).</summary>
    public sealed record Unavailable(string Reason) : CredentialError;
}

/// <summary>Secrets the app keeps for the user, in the OS's own store; C# only. Injectable.</summary>
public interface ICredentialStore
{
    Task<Result<CredentialError>> SetAsync(string name, string secret, CancellationToken cancellationToken);

    Task<Result<string, CredentialError>> GetAsync(string name, CancellationToken cancellationToken);

    Task<Result<bool, CredentialError>> ExistsAsync(string name, CancellationToken cancellationToken);

    Task<Result<CredentialError>> DeleteAsync(string name, CancellationToken cancellationToken);
}
