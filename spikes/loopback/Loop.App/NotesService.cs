using Spike;

namespace Loop.App;

public sealed record Note(string Text, IReadOnlyList<string> Tags, long? Version);

[SparContract("notes")]
public sealed class NotesService
{
    private readonly List<Note> _notes = [];

    [SparCommand]
    public Task<Note> SaveAsync(Note note, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var saved = note with { Version = _notes.Count + 1 };
        _notes.Add(saved);
        return Task.FromResult(saved);
    }

    [SparCommand]
    public Task<Note[]> ListAsync() => Task.FromResult(_notes.ToArray());
}
