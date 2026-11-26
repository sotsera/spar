using Spar;

namespace Pantry.Notes;

public sealed record AddNote(string Text);

public sealed record Note(string Id, string Text);

public closed record NoteError
{
    public sealed record Empty : NoteError;
}

[SparContract]
public sealed class NotesService
{
    [SparCommand]
    public Task<Result<Note, NoteError>> AddAsync(AddNote request)
    {
        var text = request.Text.Trim();
        Result<Note, NoteError> result = text.Length == 0
            ? new NoteError.Empty()
            : new Note(Guid.NewGuid().ToString("N"), text);
        return Task.FromResult(result);
    }
}
