using System.Collections.Concurrent;
using Spar;

namespace SampleApp.Notes;

public sealed record AddNote(string Text);

public sealed record Note(int Id, string Text, DateTimeOffset CreatedAt);

public sealed record NoteId(int Id);

public sealed record NoteError(string Code);

public interface INoteStore
{
    Note Add(string text, DateTimeOffset createdAt);

    IReadOnlyList<Note> All();

    Note? Find(int id);
}

public sealed class InMemoryNoteStore : INoteStore
{
    private readonly ConcurrentDictionary<int, Note> _notes = new();
    private int _nextId;

    public Note Add(string text, DateTimeOffset createdAt)
    {
        var note = new Note(Interlocked.Increment(ref _nextId), text, createdAt);
        _notes[note.Id] = note;
        return note;
    }

    public IReadOnlyList<Note> All() => [.. _notes.Values.OrderBy(n => n.Id)];

    public Note? Find(int id) => _notes.GetValueOrDefault(id);
}

/// <summary>A class contract: one scope per call.</summary>
public sealed class NotesApi(INoteStore store, TimeProvider time, IClipboard clipboard)
{
    public Task<Result<Note, NoteError>> AddAsync(AddNote request)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return Task.FromResult<Result<Note, NoteError>>(new NoteError("empty"));
        }

        return Task.FromResult<Result<Note, NoteError>>(store.Add(request.Text.Trim(), time.GetUtcNow()));
    }

    public Task<IReadOnlyList<Note>> ListAsync() => Task.FromResult(store.All());

    public async Task<Result<bool, NoteError>> CopyAsync(NoteId request, CancellationToken cancellationToken)
    {
        if (store.Find(request.Id) is not { } note)
        {
            return new NoteError("not-found");
        }

        await clipboard.SetTextAsync(note.Text, cancellationToken);
        return true;
    }
}
