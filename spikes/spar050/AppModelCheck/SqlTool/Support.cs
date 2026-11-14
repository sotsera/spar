// What docs/app-model.md leaves out: the connection store and the IConnections implementation.
// Not copied from the document; stand-ins so that its blocks compile.
using Spar;

namespace SqlTool.Connections;

/// <summary>Saved connections, as the copy jobs see them.</summary>
public interface IConnectionStore
{
    Task<bool> ExistsAsync(string connection, CancellationToken cancellationToken);

    /// <summary>The table's row count, or <see langword="null"/> when the table does not exist.</summary>
    Task<long?> CountRowsAsync(string connection, string table, CancellationToken cancellationToken);
}

/// <summary>Stand-in: an in-memory store in place of the file the real one would read.</summary>
public sealed class FileConnectionStore : IConnectionStore
{
    private readonly Dictionary<string, Dictionary<string, long>> _tables = new(StringComparer.OrdinalIgnoreCase)
    {
        ["src"] = new(StringComparer.OrdinalIgnoreCase) { ["dbo.Orders"] = 120_000 },
    };

    public Task<bool> ExistsAsync(string connection, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_tables.ContainsKey(connection));
    }

    public Task<long?> CountRowsAsync(string connection, string table, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        long? rows = null;
        if (_tables.TryGetValue(connection, out var tables) && tables.TryGetValue(table, out var count))
        {
            rows = count;
        }

        return Task.FromResult(rows);
    }
}

/// <summary>Stand-in implementation of the interface contract.</summary>
public sealed class ConnectionsApi(IConnectionStore store) : IConnections
{
    public Task<IReadOnlyList<ConnectionInfo>> ListAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ConnectionInfo> connections = [new ConnectionInfo("src", "localhost", "Sales", true)];
        return Task.FromResult(connections);
    }

    public async Task<Result<ConnectionInfo, ConnectionError>> TestAsync(
        string connectionId,
        CancellationToken cancellationToken)
    {
        if (!await store.ExistsAsync(connectionId, cancellationToken))
        {
            return new ConnectionError.NotFound(connectionId);
        }

        return new ConnectionInfo(connectionId, "localhost", "Sales", true);
    }

    public Task RemoveAsync(string connectionId) => Task.CompletedTask;
}
