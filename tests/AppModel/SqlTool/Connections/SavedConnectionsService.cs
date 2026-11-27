using Spar;

namespace SqlTool.Connections;

/// <summary>A connection the user saves: the password goes to the OS's store, the rest to the tool's own settings.</summary>
public sealed record SaveConnection(string Name, string Server, string User, string Password);

/// <summary>Why a connection could not be saved or used.</summary>
public closed record SavedConnectionError
{
    public sealed record NoPasswordStore(string Reason) : SavedConnectionError;

    public sealed record PasswordTooLong(int Limit) : SavedConnectionError;

    public sealed record NotSaved : SavedConnectionError;
}

/// <summary>Saved connections. Wire name: savedConnections.</summary>
[SparContract]
public sealed class SavedConnectionsService(ICredentialStore credentials)
{
    /// <summary>Saves the password under the connection's name; the page never reads it back.</summary>
    [SparCommand]
    public async Task<Result<SavedConnectionError>> SaveAsync(SaveConnection request, CancellationToken cancellationToken)
    {
        var stored = await credentials.SetAsync($"connection/{request.Name}", request.Password, cancellationToken);
        return stored switch
        {
            Success => Result.Success,
            CredentialError.TooLarge e => new SavedConnectionError.PasswordTooLong(e.Limit),
            CredentialError.Unavailable e => new SavedConnectionError.NoPasswordStore(e.Reason),
            CredentialError.NotFound => new SavedConnectionError.NotSaved(),
        };
    }

    /// <summary>Whether a password is saved for the connection, so the page can offer to connect without asking.</summary>
    [SparCommand]
    public async Task<bool> HasPasswordAsync(string name, CancellationToken cancellationToken)
    {
        var exists = await credentials.ExistsAsync($"connection/{name}", cancellationToken);
        return exists switch
        {
            bool saved => saved,
            CredentialError => false,
        };
    }

    /// <summary>Forgets the password; one that was never saved is no error.</summary>
    [SparCommand]
    public async Task<Result<SavedConnectionError>> ForgetAsync(string name, CancellationToken cancellationToken)
    {
        var deleted = await credentials.DeleteAsync($"connection/{name}", cancellationToken);
        return deleted switch
        {
            Success => Result.Success,
            CredentialError.Unavailable e => new SavedConnectionError.NoPasswordStore(e.Reason),
            CredentialError.TooLarge or CredentialError.NotFound => Result.Success,
        };
    }
}
