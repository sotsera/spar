using Spike;

namespace SparApp;

public sealed record Note(string Text, IReadOnlyList<string> Tags, Note? Parent, double Score, bool Pinned, long? Version);

[SparContract("notes")]
public sealed class NotesService
{
    private readonly List<Note> _notes = [];

    [SparCommand]
    public Task<Note> SaveAsync(Note note, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var saved = note with { Version = (note.Version ?? 0) + 1 };
        _notes.Add(saved);
        return Task.FromResult(saved);
    }

    [SparCommand]
    public Task<Note[]> ListAsync() => Task.FromResult(_notes.ToArray());

    [SparCommand]
    public ValueTask<int> CountAsync() => ValueTask.FromResult(_notes.Count);
}
