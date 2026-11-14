namespace Esproj.App;

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

