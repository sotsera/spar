using Pantry.Notes;
using Spar.Testing;

namespace Pantry.Tests;

/// <summary>The evaluation's hidden check of notes.add.</summary>
public sealed class EvalNotesTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_note_is_added_without_its_spaces()
    {
        await using var app = await SparTestApp.StartAsync<Program>(cancellationToken: Token);

        var added = await new NotesClient(app).AddAsync(new AddNote("  oat milk "), Token);

        Assert.Equal("oat milk", added.Value.Text);
        Assert.False(string.IsNullOrWhiteSpace(added.Value.Id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_note_is_a_declared_error(string text)
    {
        await using var app = await SparTestApp.StartAsync<Program>(cancellationToken: Token);

        var added = await new NotesClient(app).AddAsync(new AddNote(text), Token);

        Assert.IsType<NoteError.Empty>(added.Error);
    }
}
