using System.Buffers;

namespace Bridge;

/// <summary>
/// Encoding B, JSON envelopes: every message is one JSON object with a <c>type</c>. The request goes in <c>arg</c>,
/// stream items in <c>items</c>, and a unary result's <c>ok</c>/<c>value</c>/<c>error</c>/<c>failure</c> keys sit in the
/// envelope itself, for example <c>{"type":"result","id":7,"ok":true,"value":…}</c>.
/// </summary>
public sealed class EnvelopeCodec : ICodec
{
    public string Name => "B";

    public void Write(in Frame frame, IBufferWriter<byte> output)
    {
        var writer = JsonHeader.Writer(output);
        JsonHeader.WriteFields(writer, frame);
        switch (frame.Kind)
        {
            case Kind.Result:
                // The result body is an object written by the command's buffered writer: splice its keys in.
                writer.Flush();
                var body = frame.Payload.Span;
                if (body.Length > 2)
                {
                    output.Write(","u8);
                    output.Write(body[1..]);
                }
                else
                {
                    output.Write("}"u8);
                }

                return;
            case Kind.Items:
                writer.WritePropertyName("items"u8);
                writer.WriteRawValue(frame.Payload.Span, skipInputValidation: true);
                break;
            case Kind.Call:
            case Kind.Stream:
                if (!frame.Payload.IsEmpty)
                {
                    writer.WritePropertyName("arg"u8);
                    writer.WriteRawValue(frame.Payload.Span, skipInputValidation: true);
                }

                break;
        }

        writer.WriteEndObject();
        writer.Flush();
    }

    public DecodeResult TryRead(ReadOnlyMemory<byte> data, out Frame frame) => JsonHeader.Read(data, envelope: true, out frame);
}

/// <summary>
/// Encoding C, a JSON header object, a TAB, then the raw JSON payload: <c>{"type":"result","id":7}\t{"ok":true,…}</c>.
/// Compact JSON never contains a raw TAB or newline, so the first TAB ends the header and a newline can still frame
/// messages on a stream.
/// </summary>
public sealed class HeaderCodec : ICodec
{
    public string Name => "C";

    public void Write(in Frame frame, IBufferWriter<byte> output)
    {
        var writer = JsonHeader.Writer(output);
        JsonHeader.WriteFields(writer, frame);
        writer.WriteEndObject();
        writer.Flush();
        if (!frame.Payload.IsEmpty)
        {
            output.Write("\t"u8);
            output.Write(frame.Payload.Span);
        }
    }

    public DecodeResult TryRead(ReadOnlyMemory<byte> data, out Frame frame)
    {
        var tab = data.Span.IndexOf((byte)'\t');
        var result = JsonHeader.Read(tab < 0 ? data : data[..tab], envelope: false, out frame);
        if (tab >= 0 && result.Status == DecodeStatus.Ok)
        {
            frame.Payload = data[(tab + 1)..];
            if (frame.Payload.IsEmpty)
            {
                return DecodeResult.Fail("empty payload after the TAB");
            }
        }
        else if (tab >= 0)
        {
            frame.Payload = data[(tab + 1)..];
        }

        return result.Ok && frame.Kind is (Kind.Result or Kind.Items) && frame.Payload.IsEmpty
            ? DecodeResult.Fail("missing payload")
            : result;
    }
}
