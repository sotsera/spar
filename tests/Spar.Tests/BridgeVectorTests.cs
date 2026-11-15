using System.Text;
using System.Text.Json;
using Spar.Bridge;

namespace Spar.Tests;

/// <summary>The C# side of the shared conformance vectors (<c>tests/conformance/bridge-messages.json</c>).</summary>
public sealed class BridgeVectorTests
{
    private static readonly Lazy<JsonDocument> Vectors = new(Load);

    public static TheoryData<string> Canonical => Names("canonical");

    public static TheoryData<string> Decode => Names("decode");

    public static TheoryData<string> Malformed => Names("malformed");

    [Theory]
    [MemberData(nameof(Canonical))]
    public void The_encoder_writes_the_canonical_bytes(string name)
    {
        var vector = Find("canonical", name);

        var wire = BridgeCodec.EncodeToString(ToMessage(vector.GetProperty("message")));

        Assert.Equal(vector.GetProperty("wire").GetString(), wire);
    }

    [Theory]
    [MemberData(nameof(Canonical))]
    public void The_decoder_reads_the_canonical_bytes(string name)
    {
        var vector = Find("canonical", name);

        AssertDecodes(vector.GetProperty("wire").GetString()!, vector.GetProperty("message"));
    }

    [Theory]
    [MemberData(nameof(Decode))]
    public void The_decoder_reads_other_valid_forms(string name)
    {
        var vector = Find("decode", name);

        AssertDecodes(vector.GetProperty("wire").GetString()!, vector.GetProperty("message"));
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public void The_decoder_refuses_malformed_messages(string name)
    {
        var vector = Find("malformed", name);
        var expectedId = vector.GetProperty("id") is { ValueKind: JsonValueKind.Number } id ? id.GetInt64() : (long?)null;

        var decoded = BridgeCodec.TryDecode(vector.GetProperty("wire").GetString()!, out var message, out var error);

        Assert.False(decoded, $"decoded as {message}");
        Assert.Equal(expectedId, error!.Id);
    }

    private static void AssertDecodes(string wire, JsonElement expected)
    {
        var decoded = BridgeCodec.TryDecode(wire, out var message, out var error);

        Assert.True(decoded, error?.Reason);
        Assert.Equal(Describe(ToMessage(expected)), Describe(message!));
    }

    /// <summary>A comparable text of every field, since the records hold a list and a payload.</summary>
    private static string Describe(BridgeMessage message) => string.Join(
        "|",
        message.Type,
        message.Type == BridgeMessageType.Unknown ? message.TypeName : "",
        message.Id,
        message.Protocol,
        message.Types is null ? "" : string.Join(",", message.Types),
        message.Contracts,
        message.Doc,
        message.Contract,
        message.Command,
        message.TraceParent,
        message.TraceState,
        message.Credits,
        message.Ok,
        message.Dropped,
        message.Kind,
        message.Code,
        message.Execution,
        message.ItemsSent,
        message.Name,
        Encoding.UTF8.GetString(message.Payload.Span));

    private static BridgeMessage ToMessage(JsonElement json)
    {
        var typeName = json.GetProperty("type").GetString()!;
        var type = typeName == "unknown" ? BridgeMessageType.Unknown : BridgeMessage.TypeOf(typeName);
        return new BridgeMessage(type)
        {
            TypeName = type == BridgeMessageType.Unknown ? String(json, "typeName") : typeName,
            Id = Long(json, "id"),
            Protocol = (int?)Long(json, "protocol"),
            Types = json.TryGetProperty("types", out var types) ? types.EnumerateArray().Select(t => t.GetString()!).ToList() : null,
            Contracts = String(json, "contracts"),
            Doc = String(json, "doc"),
            Contract = String(json, "contract"),
            Command = String(json, "command"),
            TraceParent = String(json, "traceparent"),
            TraceState = String(json, "tracestate"),
            Credits = (int?)Long(json, "credits"),
            Ok = json.TryGetProperty("ok", out var ok) ? ok.GetBoolean() : null,
            Dropped = Long(json, "dropped"),
            Kind = String(json, "kind"),
            Code = String(json, "code"),
            Execution = String(json, "execution"),
            ItemsSent = Long(json, "itemsSent"),
            Name = String(json, "name"),
            Payload = Encoding.UTF8.GetBytes(json.GetProperty("payload").GetString()!),
        };
    }

    private static string? String(JsonElement json, string name) => json.TryGetProperty(name, out var value) ? value.GetString() : null;

    private static long? Long(JsonElement json, string name) => json.TryGetProperty(name, out var value) ? value.GetInt64() : null;

    private static JsonElement Find(string group, string name) =>
        Vectors.Value.RootElement.GetProperty(group).EnumerateArray().Single(v => v.GetProperty("name").GetString() == name);

    private static TheoryData<string> Names(string group) =>
        [.. Vectors.Value.RootElement.GetProperty(group).EnumerateArray().Select(v => v.GetProperty("name").GetString()!)];

    private static JsonDocument Load() => JsonDocument.Parse(File.ReadAllBytes(RepositoryFile("tests/conformance/bridge-messages.json")));

    internal static string RepositoryFile(string relative)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")))
            {
                return Path.Combine(directory.FullName, relative);
            }
        }

        throw new FileNotFoundException("No repository root (global.json) above " + AppContext.BaseDirectory);
    }
}
