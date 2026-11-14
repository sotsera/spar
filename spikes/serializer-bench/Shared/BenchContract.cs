namespace Bench;

/// <summary>Contract whose commands make the generator emit (A) codecs for <see cref="Note"/> and <see cref="Note"/>[].</summary>
[SparContract("bench")]
public sealed class BenchContract
{
    [SparCommand]
    public Task<Note> SaveAsync(Note note) => Task.FromResult(note);

    [SparCommand]
    public Task<Note[]> SaveManyAsync(Note[] notes) => Task.FromResult(notes);
}
