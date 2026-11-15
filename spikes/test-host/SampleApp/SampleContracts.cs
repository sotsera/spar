using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using SampleApp.Notes;
using SampleApp.Orders;
using Spar;

namespace SampleApp;

/// <summary>
/// Hand-written stand-in for what the contract generator emits: the dispatch table and the JSON metadata.
/// A real app never writes this file.
/// </summary>
public static class SampleContracts
{
    public static SparContractCatalog Catalog { get; } = new SparContractCatalog(typeof(SampleContracts).Assembly)
        .Add("notes", "add", async (services, payload, _) =>
        {
            var request = JsonSerializer.Deserialize(payload, SampleJson.Default.AddNote)!;
            var result = await services.GetRequiredService<NotesApi>().AddAsync(request);
            return result.IsSuccess
                ? SparReply.Ok(JsonSerializer.Serialize(result.Value!, SampleJson.Default.Note))
                : SparReply.Error(JsonSerializer.Serialize(result.Error!, SampleJson.Default.NoteError));
        })
        .Add("notes", "list", async (services, _, _) =>
        {
            var notes = await services.GetRequiredService<NotesApi>().ListAsync();
            return SparReply.Ok(JsonSerializer.Serialize(notes, SampleJson.Default.IReadOnlyListNote));
        })
        .Add("notes", "copy", async (services, payload, cancellationToken) =>
        {
            var request = JsonSerializer.Deserialize(payload, SampleJson.Default.NoteId)!;
            var result = await services.GetRequiredService<NotesApi>().CopyAsync(request, cancellationToken);
            return result.IsSuccess
                ? SparReply.Ok("true")
                : SparReply.Error(JsonSerializer.Serialize(result.Error!, SampleJson.Default.NoteError));
        })
        .Add("orders", "count", async (services, _, cancellationToken) =>
        {
            var count = await services.GetRequiredService<OrdersApi>().CountAsync(cancellationToken);
            return SparReply.Ok(JsonSerializer.Serialize(count, SampleJson.Default.Int32));
        });
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AddNote))]
[JsonSerializable(typeof(Note))]
[JsonSerializable(typeof(NoteId))]
[JsonSerializable(typeof(NoteError))]
[JsonSerializable(typeof(IReadOnlyList<Note>))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(string))]
public sealed partial class SampleJson : JsonSerializerContext;
