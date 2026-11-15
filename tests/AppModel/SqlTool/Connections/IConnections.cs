using Spar;

namespace SqlTool.Connections;

public sealed record ConnectionInfo(string Id, string Server, string Database, bool IntegratedSecurity);

public closed record ConnectionError
{
    public sealed record NotFound(string ConnectionId) : ConnectionError;

    public sealed record LoginFailed(string Server, int SqlNumber) : ConnectionError;
}

/// <summary>Saved connections. Wire name: connections.</summary>
[SparContract]
public interface IConnections
{
    Task<IReadOnlyList<ConnectionInfo>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Opens and closes a connection to check that it works.</summary>
    Task<Result<ConnectionInfo, ConnectionError>> TestAsync(string connectionId, CancellationToken cancellationToken);

    [SparCommand("forget")]
    Task RemoveAsync(string connectionId);
}
