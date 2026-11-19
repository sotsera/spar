using System.Buffers;
using System.ComponentModel;
using System.Text;
using System.Text.Json;

namespace Spar.Bridge;

/// <summary>
/// Encoding C (plan section 2c): a JSON header, a TAB, then the payload's own JSON. The decoder reads the header and
/// only locates the payload. It enforces the rules every host and the SDK share: one TAB always present, duplicate
/// header keys malformed, unknown fields ignored, ids safe integers from 1, trace values dropped rather than refused.
/// The encoder writes each type's fields in one canonical order, so the shared conformance vectors can compare bytes.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class BridgeCodec
{
    public const int ProtocolVersion = 1;

    /// <summary>The largest id a page can count to exactly: <c>Number.MAX_SAFE_INTEGER</c>.</summary>
    public const long MaxId = (1L << 53) - 1;

    private const int MaxTraceParentLength = 128;
    private const int MaxTraceStateLength = 8 * 1024;
    private const byte Tab = (byte)'\t';

    private static readonly JsonReaderOptions HeaderOptions = new() { MaxDepth = 4 };

    public static bool TryDecode(string message, out BridgeMessage? decoded, out BridgeDecodeError? error) =>
        TryDecode(Encoding.UTF8.GetBytes(message), out decoded, out error);

    public static bool TryDecode(ReadOnlyMemory<byte> message, out BridgeMessage? decoded, out BridgeDecodeError? error)
    {
        decoded = null;
        var tab = message.Span.IndexOf(Tab);
        if (tab < 0)
        {
            error = new BridgeDecodeError("The message has no TAB after its header.", null);
            return false;
        }

        var header = new HeaderReader();
        try
        {
            header.Read(message.Span[..tab]);
        }
        catch (JsonException exception)
        {
            error = new BridgeDecodeError("The header is not one JSON object: " + exception.Message, header.ErrorId);
            return false;
        }
        catch (InvalidOperationException exception)
        {
            error = new BridgeDecodeError("The header is not one JSON object: " + exception.Message, header.ErrorId);
            return false;
        }

        if (header.Problem is { } problem)
        {
            error = new BridgeDecodeError(problem, header.ErrorId);
            return false;
        }

        decoded = header.ToMessage(message[(tab + 1)..]);
        var missing = MissingField(decoded);
        if (missing is not null)
        {
            error = new BridgeDecodeError($"A '{header.TypeName}' message needs '{missing}'.", header.ErrorId);
            decoded = null;
            return false;
        }

        error = null;
        return true;
    }

    public static string EncodeToString(BridgeMessage message) => Encoding.UTF8.GetString(Encode(message));

    public static byte[] Encode(BridgeMessage message)
    {
        if (message.Type == BridgeMessageType.Unknown)
        {
            throw new ArgumentException("An unknown message cannot be encoded.", nameof(message));
        }

        var buffer = new ArrayBufferWriter<byte>(128 + message.Payload.Length);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteHeader(writer, message);
        }

        buffer.Write([Tab]);
        buffer.Write(message.Payload.Span);
        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteHeader(Utf8JsonWriter writer, BridgeMessage message)
    {
        writer.WriteStartObject();
        writer.WriteString("type"u8, BridgeMessage.TypeNameOf(message.Type));
        switch (message.Type)
        {
            case BridgeMessageType.Hello:
                writer.WriteNumber("protocol"u8, message.Protocol ?? ProtocolVersion);
                writer.WriteStartArray("types"u8);
                foreach (var type in message.Types ?? [])
                {
                    writer.WriteStringValue(type);
                }

                writer.WriteEndArray();
                writer.WriteString("contracts"u8, message.Contracts);
                break;
            case BridgeMessageType.Call:
            case BridgeMessageType.Stream:
                WriteId(writer, message);
                writer.WriteString("contract"u8, message.Contract);
                writer.WriteString("command"u8, message.Command);
                if (message.Type == BridgeMessageType.Stream)
                {
                    writer.WriteNumber("credits"u8, message.Credits ?? 0);
                }

                WriteOptional(writer, "traceparent"u8, message.TraceParent);
                WriteOptional(writer, "tracestate"u8, message.TraceState);
                break;
            case BridgeMessageType.Credits:
                WriteId(writer, message);
                writer.WriteNumber("credits"u8, message.Credits ?? 0);
                break;
            case BridgeMessageType.Cancel:
                WriteId(writer, message);
                break;
            case BridgeMessageType.Open:
                writer.WriteNumber("protocol"u8, message.Protocol ?? ProtocolVersion);
                writer.WriteString("doc"u8, message.Doc);
                writer.WriteString("contracts"u8, message.Contracts);
                break;
            case BridgeMessageType.Result:
            case BridgeMessageType.End:
                WriteId(writer, message);
                writer.WriteBoolean("ok"u8, message.Ok ?? false);
                break;
            case BridgeMessageType.Items:
                WriteId(writer, message);
                if (message.Dropped is { } dropped)
                {
                    writer.WriteNumber("dropped"u8, dropped);
                }

                break;
            case BridgeMessageType.Failure:
                WriteId(writer, message);
                writer.WriteString("kind"u8, message.Kind);
                writer.WriteString("code"u8, message.Code);
                writer.WriteString("execution"u8, message.Execution);
                if (message.ItemsSent is { } itemsSent)
                {
                    writer.WriteNumber("itemsSent"u8, itemsSent);
                }

                break;
            case BridgeMessageType.Event:
                writer.WriteString("name"u8, message.Name);
                break;
            case BridgeMessageType.Ping:
            case BridgeMessageType.Pong:
            case BridgeMessageType.CloseRequest:
            case BridgeMessageType.CloseAnswer:
            case BridgeMessageType.CloseEnded:
                writer.WriteNumber("seq"u8, message.Seq ?? throw new ArgumentException($"A '{BridgeMessage.TypeNameOf(message.Type)}' message needs a seq."));
                break;
        }

        writer.WriteEndObject();
    }

    private static void WriteId(Utf8JsonWriter writer, BridgeMessage message) =>
        writer.WriteNumber("id"u8, message.Id ?? throw new ArgumentException($"A '{BridgeMessage.TypeNameOf(message.Type)}' message needs an id."));

    private static void WriteOptional(Utf8JsonWriter writer, ReadOnlySpan<byte> name, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(name, value);
        }
    }

    private static string? MissingField(BridgeMessage message) => message.Type switch
    {
        BridgeMessageType.Hello when message.Protocol is null => "protocol",
        BridgeMessageType.Hello when message.Contracts is null => "contracts",
        BridgeMessageType.Open when message.Protocol is null => "protocol",
        BridgeMessageType.Open when message.Doc is null => "doc",
        BridgeMessageType.Open when message.Contracts is null => "contracts",
        BridgeMessageType.Call or BridgeMessageType.Stream or BridgeMessageType.Credits or BridgeMessageType.Cancel
            or BridgeMessageType.Result or BridgeMessageType.Items or BridgeMessageType.End or BridgeMessageType.Failure
            when message.Id is null => "id",
        BridgeMessageType.Call or BridgeMessageType.Stream when message.Contract is null => "contract",
        BridgeMessageType.Call or BridgeMessageType.Stream when message.Command is null => "command",
        BridgeMessageType.Stream or BridgeMessageType.Credits when message.Credits is null => "credits",
        BridgeMessageType.Result or BridgeMessageType.End when message.Ok is null => "ok",
        BridgeMessageType.Failure when message.Kind is null => "kind",
        BridgeMessageType.Failure when message.Code is null => "code",
        BridgeMessageType.Failure when message.Execution is null => "execution",
        BridgeMessageType.Event when message.Name is null => "name",
        BridgeMessageType.Ping or BridgeMessageType.Pong or BridgeMessageType.CloseRequest or BridgeMessageType.CloseAnswer
            or BridgeMessageType.CloseEnded when message.Seq is null => "seq",
        _ => null,
    };

    /// <summary>W3C trace context: <c>00-&lt;32 hex&gt;-&lt;16 hex&gt;-&lt;2 hex&gt;</c>, lower case, ids not all zero.</summary>
    internal static bool IsValidTraceParent(string value)
    {
        if (value.Length != 55 || value[2] != '-' || value[35] != '-' || value[52] != '-')
        {
            return false;
        }

        var version = value.AsSpan(0, 2);
        var traceId = value.AsSpan(3, 32);
        var parentId = value.AsSpan(36, 16);
        var flags = value.AsSpan(53, 2);
        return IsLowerHex(version) && version is not "ff" && IsLowerHex(traceId) && IsLowerHex(parentId) && IsLowerHex(flags)
            && traceId.ContainsAnyExcept('0') && parentId.ContainsAnyExcept('0');
    }

    private static bool IsLowerHex(ReadOnlySpan<char> text) => !text.ContainsAnyExcept("0123456789abcdef");

    /// <summary>Reads a header object field by field; problems are kept, not thrown, so the id survives for the reply.</summary>
    private ref struct HeaderReader
    {
        private HashSet<string>? _seen;

        public long? Id;
        public string? TypeName;
        public string? Problem;
        private bool _idTwice;
        private int? _protocol;
        private List<string>? _types;
        private string? _contracts;
        private string? _doc;
        private string? _contract;
        private string? _command;
        private string? _traceParent;
        private string? _traceState;
        private int? _credits;
        private bool? _ok;
        private long? _dropped;
        private string? _kind;
        private string? _code;
        private string? _execution;
        private long? _itemsSent;
        private string? _name;
        private long? _seq;

        public void Read(ReadOnlySpan<byte> header)
        {
            var reader = new Utf8JsonReader(header, HeaderOptions);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                Problem = "The header is not a JSON object.";
                return;
            }

            _seen = new HashSet<string>(StringComparer.Ordinal);
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var name = reader.GetString()!;
                if (!_seen.Add(name))
                {
                    Problem ??= $"The header has '{name}' twice.";
                    _idTwice |= name == "id";
                }

                reader.Read();
                ReadField(ref reader, name);
            }

            if (reader.Read())
            {
                Problem ??= "The header has content after its object.";
            }

            if (TypeName is null)
            {
                Problem ??= "The header has no 'type'.";
            }
        }

        /// <summary>The id a refusal may be addressed to: none when the id itself was given twice.</summary>
        public readonly long? ErrorId => _idTwice ? null : Id;

        public readonly BridgeMessage ToMessage(ReadOnlyMemory<byte> payload) => new(BridgeMessage.TypeOf(TypeName!))
        {
            TypeName = TypeName,
            Id = Id,
            Protocol = _protocol,
            Types = _types,
            Contracts = _contracts,
            Doc = _doc,
            Contract = _contract,
            Command = _command,
            TraceParent = _traceParent,
            TraceState = _traceState,
            Credits = _credits,
            Ok = _ok,
            Dropped = _dropped,
            Kind = _kind,
            Code = _code,
            Execution = _execution,
            ItemsSent = _itemsSent,
            Name = _name,
            Seq = _seq,
            Payload = payload,
        };

        private void ReadField(ref Utf8JsonReader reader, string name)
        {
            switch (name)
            {
                case "type":
                    TypeName = String(ref reader, name);
                    break;
                case "id":
                    Id = ReadId(ref reader);
                    break;
                case "protocol":
                    _protocol = Integer(ref reader, name);
                    break;
                case "types":
                    _types = Strings(ref reader, name);
                    break;
                case "contracts":
                    _contracts = String(ref reader, name);
                    break;
                case "doc":
                    _doc = String(ref reader, name);
                    break;
                case "contract":
                    _contract = String(ref reader, name);
                    break;
                case "command":
                    _command = String(ref reader, name);
                    break;
                case "traceparent":
                    _traceParent = Trace(ref reader, MaxTraceParentLength) is { } parent && IsValidTraceParent(parent) ? parent : null;
                    break;
                case "tracestate":
                    _traceState = Trace(ref reader, MaxTraceStateLength);
                    break;
                case "credits":
                    _credits = Integer(ref reader, name) is > 0 and var credits ? credits : Fail<int?>($"'{name}' must be a positive integer.");
                    break;
                case "ok":
                    _ok = reader.TokenType switch
                    {
                        JsonTokenType.True => true,
                        JsonTokenType.False => false,
                        _ => Fail<bool?>("'ok' must be true or false."),
                    };
                    break;
                case "dropped":
                    _dropped = Count(ref reader, name);
                    break;
                case "kind":
                    _kind = String(ref reader, name);
                    break;
                case "code":
                    _code = String(ref reader, name);
                    break;
                case "execution":
                    _execution = String(ref reader, name);
                    break;
                case "itemsSent":
                    _itemsSent = Count(ref reader, name);
                    break;
                case "name":
                    _name = String(ref reader, name);
                    break;
                case "seq":
                    _seq = Sequence(ref reader);
                    break;
                default:
                    // Unknown fields are ignored: a newer peer may add them.
                    reader.Skip();
                    break;
            }
        }

        private long? ReadId(ref Utf8JsonReader reader)
        {
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var id) && id is >= 1 and <= MaxId)
            {
                return id;
            }

            reader.Skip();
            return Fail<long?>("'id' must be a safe integer from 1.");
        }

        private long? Sequence(ref Utf8JsonReader reader)
        {
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var seq) && seq is >= 1 and <= MaxId)
            {
                return seq;
            }

            reader.Skip();
            return Fail<long?>("'seq' must be a safe integer from 1.");
        }

        private string? String(ref Utf8JsonReader reader, string name)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                reader.Skip();
                return Fail<string?>($"'{name}' must be a string.");
            }

            return reader.GetString();
        }

        private int? Integer(ref Utf8JsonReader reader, string name) =>
            reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var value) ? value : Fail<int?>($"'{name}' must be an integer.");

        private long? Count(ref Utf8JsonReader reader, string name) =>
            reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var value) && value >= 0 ? value : Fail<long?>($"'{name}' must be a count.");

        private List<string>? Strings(ref Utf8JsonReader reader, string name)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                reader.Skip();
                return Fail<List<string>?>($"'{name}' must be an array of strings.");
            }

            var values = new List<string>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType != JsonTokenType.String)
                {
                    reader.Skip();
                    Problem ??= $"'{name}' must be an array of strings.";
                    continue;
                }

                values.Add(reader.GetString()!);
            }

            return values;
        }

        /// <summary>A trace value that is not a string, too long, or not valid UTF-16 is dropped, never refused.</summary>
        private static string? Trace(ref Utf8JsonReader reader, int maxLength)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                reader.Skip();
                return null;
            }

            try
            {
                var value = reader.GetString()!;
                return value.Length <= maxLength ? value : null;
            }
            catch (InvalidOperationException)
            {
                // A lone-surrogate escape cannot become a .NET string.
                return null;
            }
        }

        private T Fail<T>(string problem)
        {
            Problem ??= problem;
            return default!;
        }
    }
}
