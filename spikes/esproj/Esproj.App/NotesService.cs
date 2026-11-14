using Spike;

namespace Esproj.App;

public sealed record Note(string Id, string Text, IReadOnlyList<string> Tags, long Version, string CreatedAt);

public sealed record NewNote(string Text, IReadOnlyList<string> Tags);

public sealed record NoteQuery(string? Text, int? DelayMs);

public sealed record NoteId(string Id);

public sealed record Echo(string Data);

[SparContract("notes")]
public sealed class NotesService(TimeProvider time, INoteStore store)
{
    [SparCommand]
    public async Task<Note> SaveAsync(NewNote note, CancellationToken cancellationToken)
    {
        var createdAt = time.GetUtcNow().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        return await store.AddAsync(Guid.NewGuid().ToString("N")[..8], note.Text.Trim(), note.Tags, createdAt, cancellationToken);
    }

    /// <summary>Filters on the backend. DelayMs simulates a slow query so the UI's latest-wins handling can be tested.</summary>
    [SparCommand]
    public async Task<Note[]> SearchAsync(NoteQuery query, CancellationToken cancellationToken)
    {
        if (query.DelayMs is > 0)
        {
            await Task.Delay(query.DelayMs.Value, cancellationToken);
        }

        return await store.SearchAsync(string.IsNullOrWhiteSpace(query.Text) ? null : query.Text, cancellationToken);
    }

    [SparCommand]
    public Task<bool> RemoveAsync(NoteId id) => store.RemoveAsync(id.Id, CancellationToken.None);

    [SparCommand]
    public Task<int> CountAsync() => store.CountAsync(CancellationToken.None);

    /// <summary>Spike: echoes its payload, for the bridge transport benchmark.</summary>
    [SparCommand]
    public Task<Echo> EchoAsync(Echo echo) => Task.FromResult(echo);
}
