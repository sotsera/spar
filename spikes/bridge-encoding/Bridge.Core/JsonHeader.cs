using System.Buffers;
using System.Text.Json;

namespace Bridge;

/// <summary>Shared JSON writing and reading of message headers for encodings B and C.</summary>
internal static class JsonHeader
{
    private static readonly JsonEncodedText s_type = JsonEncodedText.Encode("type");
    private static readonly JsonEncodedText s_id = JsonEncodedText.Encode("id");
    private static readonly JsonEncodedText s_doc = JsonEncodedText.Encode("doc");
    private static readonly JsonEncodedText s_protocol = JsonEncodedText.Encode("protocol");
    private static readonly JsonEncodedText s_contract = JsonEncodedText.Encode("contract");
    private static readonly JsonEncodedText s_command = JsonEncodedText.Encode("command");
    private static readonly JsonEncodedText s_fingerprint = JsonEncodedText.Encode("fingerprint");
    private static readonly JsonEncodedText s_traceparent = JsonEncodedText.Encode("traceparent");
    private static readonly JsonEncodedText s_tracestate = JsonEncodedText.Encode("tracestate");
    private static readonly JsonEncodedText s_batch = JsonEncodedText.Encode("batch");
    private static readonly JsonEncodedText s_credits = JsonEncodedText.Encode("credits");
    private static readonly JsonEncodedText s_dropped = JsonEncodedText.Encode("dropped");
    private static readonly JsonEncodedText s_code = JsonEncodedText.Encode("code");
    private static readonly JsonEncodedText s_detail = JsonEncodedText.Encode("detail");

    private static readonly JsonEncodedText[] s_typeNames =
    [
        default,
        JsonEncodedText.Encode("hello"),
        JsonEncodedText.Encode("call"),
        JsonEncodedText.Encode("stream"),
        JsonEncodedText.Encode("credits"),
        JsonEncodedText.Encode("cancel"),
        JsonEncodedText.Encode("closing-done"),
        JsonEncodedText.Encode("open"),
        JsonEncodedText.Encode("result"),
        JsonEncodedText.Encode("items"),
        JsonEncodedText.Encode("end"),
        JsonEncodedText.Encode("cancelled"),
        JsonEncodedText.Encode("not-executed"),
        JsonEncodedText.Encode("outcome-unknown"),
        JsonEncodedText.Encode("closing"),
    ];

    [ThreadStatic]
    private static Utf8JsonWriter? t_writer;

    public static Utf8JsonWriter Writer(IBufferWriter<byte> output)
    {
        if (t_writer is null)
        {
            t_writer = new Utf8JsonWriter(output, new JsonWriterOptions { SkipValidation = true });
        }
        else
        {
            t_writer.Reset(output);
        }

        return t_writer;
    }

    /// <summary>Writes <c>{"type":…,"id":…</c> and the kind's header fields, leaving the object open.</summary>
    public static void WriteFields(Utf8JsonWriter writer, in Frame frame)
    {
        writer.WriteStartObject();
        writer.WriteString(s_type, s_typeNames[(int)frame.Kind]);
        if (frame.Id >= 0)
        {
            writer.WriteNumber(s_id, frame.Id);
        }

        switch (frame.Kind)
        {
            case Kind.Hello:
            case Kind.Open:
                writer.WriteString(s_doc, frame.Doc);
                writer.WriteNumber(s_protocol, frame.Protocol);
                break;
            case Kind.Call:
            case Kind.Stream:
                writer.WriteString(s_contract, frame.Contract);
                writer.WriteString(s_command, frame.Command);
                writer.WriteString(s_fingerprint, frame.Fingerprint);
                if (frame.TraceParent is not null)
                {
                    writer.WriteString(s_traceparent, frame.TraceParent);
                }

                if (frame.TraceState is not null)
                {
                    writer.WriteString(s_tracestate, frame.TraceState);
                }

                if (frame.Kind == Kind.Stream)
                {
                    writer.WriteNumber(s_batch, frame.Batch);
                    writer.WriteNumber(s_credits, frame.Credits);
                }

                break;
            case Kind.Credits:
                writer.WriteNumber(s_credits, frame.Credits);
                break;
            case Kind.Items:
                writer.WriteNumber(s_dropped, frame.Dropped);
                break;
            case Kind.NotExecuted:
            case Kind.OutcomeUnknown:
                writer.WriteString(s_code, frame.Code);
                if (frame.Detail is not null)
                {
                    writer.WriteString(s_detail, frame.Detail);
                }

                break;
        }
    }

    /// <summary>
    /// Reads a header object. With <paramref name="envelope"/> (encoding B), the value of <c>arg</c> or <c>items</c> is
    /// skipped over and returned as a slice of <paramref name="data"/>.
    /// </summary>
    public static DecodeResult Read(ReadOnlyMemory<byte> data, bool envelope, out Frame frame)
    {
        frame = default;
        frame.Id = -1;
        var span = data.Span;
        var reader = new Utf8JsonReader(span, new JsonReaderOptions { MaxDepth = 64 });
        var payloadStart = -1;
        var payloadEnd = -1;
        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return DecodeResult.Fail("not a JSON object");
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("type"u8))
                {
                    reader.Read();
                    if (reader.TokenType != JsonTokenType.String)
                    {
                        return DecodeResult.Fail("type is not a string");
                    }

                    frame.Kind = KindOf(ref reader);
                    if (frame.Kind == Kind.Unrecognized)
                    {
                        frame.UnrecognizedKind = reader.GetString();
                    }
                }
                else if (reader.ValueTextEquals("id"u8))
                {
                    reader.Read();
                    if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt64(out var id) || id < 0 || id > Codecs.MaxId)
                    {
                        return DecodeResult.Fail("bad id");
                    }

                    frame.Id = id;
                }
                else if (envelope && (reader.ValueTextEquals("arg"u8) || reader.ValueTextEquals("items"u8)))
                {
                    reader.Read();
                    payloadStart = (int)reader.TokenStartIndex;
                    reader.Skip();
                    payloadEnd = (int)reader.BytesConsumed;
                }
                else if (reader.ValueTextEquals("contract"u8))
                {
                    frame.Contract = ReadString(ref reader);
                }
                else if (reader.ValueTextEquals("command"u8))
                {
                    frame.Command = ReadString(ref reader);
                }
                else if (reader.ValueTextEquals("fingerprint"u8))
                {
                    frame.Fingerprint = ReadString(ref reader);
                }
                else if (reader.ValueTextEquals("traceparent"u8))
                {
                    frame.TraceParent = ReadString(ref reader);
                }
                else if (reader.ValueTextEquals("tracestate"u8))
                {
                    frame.TraceState = ReadString(ref reader);
                }
                else if (reader.ValueTextEquals("credits"u8))
                {
                    frame.Credits = ReadInt(ref reader);
                }
                else if (reader.ValueTextEquals("batch"u8))
                {
                    frame.Batch = ReadInt(ref reader);
                }
                else if (reader.ValueTextEquals("dropped"u8))
                {
                    frame.Dropped = ReadInt(ref reader);
                }
                else if (reader.ValueTextEquals("doc"u8))
                {
                    frame.Doc = ReadString(ref reader);
                }
                else if (reader.ValueTextEquals("protocol"u8))
                {
                    frame.Protocol = ReadInt(ref reader);
                }
                else if (reader.ValueTextEquals("code"u8))
                {
                    frame.Code = ReadString(ref reader);
                }
                else if (reader.ValueTextEquals("detail"u8))
                {
                    frame.Detail = ReadString(ref reader);
                }
                else
                {
                    // Unknown property (a newer peer): skipped, not decoded.
                    reader.Read();
                    reader.Skip();
                }
            }

            if (reader.TokenType != JsonTokenType.EndObject)
            {
                return DecodeResult.Fail("unexpected token " + reader.TokenType);
            }

            // Trailing content after the object (two frames concatenated) makes the reader throw here.
            if (reader.Read())
            {
                return DecodeResult.Fail("trailing content after the message");
            }
        }
        catch (JsonException exception)
        {
            return DecodeResult.Fail("JsonException: " + FirstLine(exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            // GetString on an unpaired surrogate escape, or a value of the wrong token type.
            return DecodeResult.Fail("InvalidOperationException: " + FirstLine(exception.Message));
        }
        catch (FormatException exception)
        {
            return DecodeResult.Fail("FormatException: " + FirstLine(exception.Message));
        }

        if (frame.Kind == Kind.Invalid)
        {
            return DecodeResult.Fail("missing type");
        }

        if (payloadStart >= 0)
        {
            frame.Payload = data[payloadStart..payloadEnd];
        }
        else if (envelope && frame.Kind == Kind.Result)
        {
            // The result's ok, value, error and failure keys sit in the envelope itself.
            frame.Payload = data;
        }

        return Validate(frame, envelope);
    }

    private static DecodeResult Validate(in Frame frame, bool envelope)
    {
        var needsId = frame.Kind is not (Kind.Hello or Kind.Open or Kind.Closing or Kind.ClosingDone or Kind.Unrecognized);
        if (needsId && frame.Id < 0)
        {
            return DecodeResult.Fail("missing id");
        }

        return frame.Kind switch
        {
            Kind.Call or Kind.Stream when frame.Contract is null || frame.Command is null || frame.Fingerprint is null =>
                DecodeResult.Fail("missing contract, command or fingerprint"),
            Kind.Items when envelope && frame.Payload.IsEmpty => DecodeResult.Fail("missing items"),
            Kind.NotExecuted or Kind.OutcomeUnknown when frame.Code is null => DecodeResult.Fail("missing code"),
            Kind.Hello or Kind.Open when frame.Doc is null => DecodeResult.Fail("missing doc"),
            _ => DecodeResult.Success,
        };
    }

    private static Kind KindOf(ref Utf8JsonReader reader)
    {
        for (var kind = 1; kind < s_typeNames.Length; kind++)
        {
            if (reader.ValueTextEquals(s_typeNames[kind].EncodedUtf8Bytes))
            {
                return (Kind)kind;
            }
        }

        return Kind.Unrecognized;
    }

    private static string? ReadString(ref Utf8JsonReader reader)
    {
        reader.Read();
        return reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
    }

    private static int ReadInt(ref Utf8JsonReader reader)
    {
        reader.Read();
        return reader.GetInt32();
    }

    private static string FirstLine(string message)
    {
        var end = message.IndexOf('\n', StringComparison.Ordinal);
        return end < 0 ? message : message[..end];
    }
}
