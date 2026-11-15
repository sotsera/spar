using System.Text.Json;
using SampleApp.Notes;
using Spar;
using Spar.Testing;

namespace SampleApp.Tests;

/// <summary>A command's reply as the page sees it: status and the payload's JSON.</summary>
public sealed record Reply(string Status, string Payload)
{
    public static Reply Parse(string message)
    {
        var tab = message.IndexOf('\t', StringComparison.Ordinal);
        using var header = JsonDocument.Parse(message.AsMemory(0, tab));
        return new(header.RootElement.GetProperty("status").GetString()!, message[(tab + 1)..]);
    }

    public string FailureCode()
    {
        using var payload = JsonDocument.Parse(Payload);
        return payload.RootElement.GetProperty("code").GetString()!;
    }
}

/// <summary>
/// Stand-in for a generated typed test client: it writes the same message a page writes and reads the reply with the
/// app's own serializer, so a command test covers serialization and dispatch, not just the C# method.
/// </summary>
public sealed class NotesClient(SparTestApp app)
{
    private long _nextId;

    public async Task<Result<Note, NoteError>> AddAsync(string text)
    {
        var reply = await CallAsync("notes", "add", JsonSerializer.Serialize(new AddNote(text), SampleJson.Default.AddNote));
        return reply.Status == "ok"
            ? JsonSerializer.Deserialize(reply.Payload, SampleJson.Default.Note)!
            : JsonSerializer.Deserialize(reply.Payload, SampleJson.Default.NoteError)!;
    }

    public async Task<IReadOnlyList<Note>> ListAsync()
    {
        var reply = await CallAsync("notes", "list", "{}");
        return JsonSerializer.Deserialize(reply.Payload, SampleJson.Default.IReadOnlyListNote)!;
    }

    public async Task<Reply> CopyAsync(int id) =>
        await CallAsync("notes", "copy", JsonSerializer.Serialize(new NoteId(id), SampleJson.Default.NoteId));

    public Task<Reply> OrderCountAsync() => CallAsync("orders", "count", "{}");

    public async Task<Reply> CallAsync(string contract, string command, string payload)
    {
        var id = Interlocked.Increment(ref _nextId);
        var message = $$"""{"id":{{id}},"contract":"{{contract}}","command":"{{command}}"}""" + "\t" + payload;
        return Reply.Parse(await app.SendAsync(message));
    }
}
