namespace React.App;

/// <summary>Where notes live: memory by default, SQL Server when the app is given a connection string.</summary>
public interface INoteStore
{
    Task<Note> AddAsync(string id, string text, IReadOnlyList<string> tags, string createdAt, CancellationToken cancellationToken);

    Task<Note[]> SearchAsync(string? text, CancellationToken cancellationToken);

    Task<bool> RemoveAsync(string id, CancellationToken cancellationToken);

    Task<int> CountAsync(CancellationToken cancellationToken);
}

public sealed class InMemoryNoteStore : INoteStore
{
    private readonly Lock _gate = new();
    private readonly List<Note> _notes = [];
    private long _version;

    public Task<Note> AddAsync(string id, string text, IReadOnlyList<string> tags, string createdAt, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var note = new Note(id, text, tags, ++_version, createdAt);
            _notes.Add(note);
            return Task.FromResult(note);
        }
    }

    public Task<Note[]> SearchAsync(string? text, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<Note[]>(text is null
                ? [.. _notes]
                : [.. _notes.Where(n => n.Text.Contains(text, StringComparison.OrdinalIgnoreCase)
                    || n.Tags.Any(t => t.Equals(text, StringComparison.OrdinalIgnoreCase)))]);
        }
    }

    public Task<bool> RemoveAsync(string id, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_notes.RemoveAll(n => n.Id == id) > 0);
        }
    }

    public Task<int> CountAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_notes.Count);
        }
    }
}

#if SPAR_SQL
/// <summary>Spike: SQL Server through plain ADO.NET (Microsoft.Data.SqlClient). Debug builds only until AOT is proven.</summary>
public sealed class SqlNoteStore(string connectionString) : INoteStore
{
    private int _schemaReady;

    public async Task<Note> AddAsync(string id, string text, IReadOnlyList<string> tags, string createdAt, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO dbo.Notes (Id, Text, Tags, CreatedAt) OUTPUT INSERTED.Version VALUES (@id, @text, @tags, @createdAt)";
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@text", text);
        command.Parameters.AddWithValue("@tags", string.Join(',', tags));
        command.Parameters.AddWithValue("@createdAt", createdAt);
        var version = (long)(await command.ExecuteScalarAsync(cancellationToken))!;
        return new Note(id, text, tags, version, createdAt);
    }

    public async Task<Note[]> SearchAsync(string? text, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Text, Tags, Version, CreatedAt FROM dbo.Notes
            WHERE @text IS NULL OR Text LIKE '%' + @text + '%' OR ',' + Tags + ',' LIKE '%,' + @text + ',%'
            ORDER BY Version
            """;
        command.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@text", System.Data.SqlDbType.NVarChar, 4000) { Value = (object?)text ?? DBNull.Value });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var notes = new List<Note>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var tags = reader.GetString(2);
            notes.Add(new Note(reader.GetString(0), reader.GetString(1), tags.Length == 0 ? [] : tags.Split(','), reader.GetInt64(3), reader.GetString(4)));
        }

        return [.. notes];
    }

    public async Task<bool> RemoveAsync(string id, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM dbo.Notes WHERE Id = @id";
        command.Parameters.AddWithValue("@id", id);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM dbo.Notes";
        return (int)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private async Task<Microsoft.Data.SqlClient.SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        if (Interlocked.CompareExchange(ref _schemaReady, 1, 0) == 0)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                IF OBJECT_ID('dbo.Notes') IS NULL
                CREATE TABLE dbo.Notes (
                    Version bigint IDENTITY(1, 1) PRIMARY KEY,
                    Id char(8) NOT NULL UNIQUE,
                    Text nvarchar(4000) NOT NULL,
                    Tags nvarchar(4000) NOT NULL,
                    CreatedAt char(20) NOT NULL)
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return connection;
    }
}
#endif
