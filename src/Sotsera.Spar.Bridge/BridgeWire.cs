using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sotsera.Spar.Bridge;

internal sealed record BridgeRequest(int Version, string Kind, int Id, string Contract, string Client,
    string? Document, string? Operation, JsonElement Payload, int? TimeoutMs);

internal sealed record BridgeReply(int Version, string Kind, int Id, string Contract, string Client,
    string Document, string Status, string? Code, JsonElement? Result);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, MaxDepth = 16,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(BridgeRequest))]
[JsonSerializable(typeof(BridgeReply))]
internal sealed partial class BridgeWireContext : JsonSerializerContext;
