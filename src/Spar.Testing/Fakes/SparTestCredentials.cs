using System.Collections.Concurrent;

namespace Spar.Testing;

/// <summary>
/// The credential store of a test app (<c>app.Credentials</c>): secrets kept in memory for the test's life, so a test
/// never writes to the machine's Credential Manager or keyring (each entry there is a per-user registration). It applies
/// the real stores' rules: the name's checks and the 2,560-byte limit. <see cref="Unavailable"/> makes every call fail,
/// as on a Linux desktop without a Secret Service.
/// </summary>
public sealed class SparTestCredentials : ICredentialStore
{
    private readonly ConcurrentDictionary<string, string> _secrets = new(StringComparer.Ordinal);
    private volatile string? _unavailable;

    internal SparTestCredentials()
    {
    }

    /// <summary>The secrets stored, by name.</summary>
    public IReadOnlyDictionary<string, string> Stored => new Dictionary<string, string>(_secrets, StringComparer.Ordinal);

    /// <summary>Stores a secret as if the app had, before the test calls it.</summary>
    public SparTestCredentials Store(string name, string secret)
    {
        CredentialNames.Check(name);
        _secrets[name] = secret;
        return this;
    }

    /// <summary>Every call fails with <see cref="CredentialError.Unavailable"/> and this reason; null makes the store work again.</summary>
    public SparTestCredentials Unavailable(string? reason)
    {
        _unavailable = reason;
        return this;
    }

    Task<Result<CredentialError>> ICredentialStore.SetAsync(string name, string secret, CancellationToken cancellationToken)
    {
        CredentialNames.Check(name);
        ArgumentNullException.ThrowIfNull(secret);
        if (_unavailable is { } reason)
        {
            return Task.FromResult<Result<CredentialError>>(new CredentialError.Unavailable(reason));
        }

        if (CredentialNames.TooLarge(secret))
        {
            return Task.FromResult<Result<CredentialError>>(new CredentialError.TooLarge(ICredentialStore.MaxSecretBytes));
        }

        _secrets[name] = secret;
        return Task.FromResult<Result<CredentialError>>(Result.Success);
    }

    Task<Result<string, CredentialError>> ICredentialStore.GetAsync(string name, CancellationToken cancellationToken)
    {
        CredentialNames.Check(name);
        if (_unavailable is { } reason)
        {
            return Task.FromResult<Result<string, CredentialError>>(new CredentialError.Unavailable(reason));
        }

        return Task.FromResult<Result<string, CredentialError>>(
            _secrets.TryGetValue(name, out var secret) ? secret : new CredentialError.NotFound());
    }

    Task<Result<bool, CredentialError>> ICredentialStore.ExistsAsync(string name, CancellationToken cancellationToken)
    {
        CredentialNames.Check(name);
        if (_unavailable is { } reason)
        {
            return Task.FromResult<Result<bool, CredentialError>>(new CredentialError.Unavailable(reason));
        }

        return Task.FromResult<Result<bool, CredentialError>>(_secrets.ContainsKey(name));
    }

    Task<Result<CredentialError>> ICredentialStore.DeleteAsync(string name, CancellationToken cancellationToken)
    {
        CredentialNames.Check(name);
        if (_unavailable is { } reason)
        {
            return Task.FromResult<Result<CredentialError>>(new CredentialError.Unavailable(reason));
        }

        _secrets.TryRemove(name, out _);
        return Task.FromResult<Result<CredentialError>>(Result.Success);
    }
}
