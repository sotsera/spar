using System.Buffers;
using System.Text;

namespace Bridge;

/// <summary>
/// Encoding A, the spikes' letter-prefixed text: a kind letter, the call id, the kind's fields separated by spaces, then
/// compact JSON. Host to page: <c>o doc</c>, <c>r id json</c>, <c>i id dropped json</c>, <c>e id</c>, <c>c id</c>,
/// <c>n id code json</c>, <c>u id code json</c>, <c>z</c>. Page to host: <c>h doc</c>,
/// <c>q id contract command fingerprint trace json</c>, <c>s id contract command fingerprint trace batch credits json</c>,
/// <c>k id credits</c>, <c>x id</c>, <c>z</c>. The trace field is <c>-</c>, or a traceparent optionally followed by
/// <c>;</c> and the URI-escaped tracestate. Errors carry <c>{"detail":…}</c> as their JSON.
/// </summary>
public sealed class LetterCodec : ICodec
{
    public string Name => "A";

    public void Write(in Frame frame, IBufferWriter<byte> output)
    {
        var text = new TextWriter(output, 64 + Length(frame.Doc) + Length(frame.Contract) + Length(frame.Command)
            + Length(frame.Fingerprint) + Length(frame.TraceParent) + 3 * Length(frame.TraceState) + Length(frame.Code));
        switch (frame.Kind)
        {
            case Kind.Hello:
            case Kind.Open:
                text.Byte(frame.Kind == Kind.Hello ? (byte)'h' : (byte)'o');
                text.Space();
                text.Ascii(frame.Doc);
                text.Space();
                text.Number(frame.Protocol);
                text.Commit();
                return;
            case Kind.Closing:
            case Kind.ClosingDone:
                text.Byte((byte)'z');
                text.Commit();
                return;
            case Kind.Call:
            case Kind.Stream:
                text.Byte(frame.Kind == Kind.Call ? (byte)'q' : (byte)'s');
                text.Space();
                text.Number(frame.Id);
                text.Space();
                text.Ascii(frame.Contract);
                text.Space();
                text.Ascii(frame.Command);
                text.Space();
                text.Ascii(frame.Fingerprint);
                text.Space();
                WriteTrace(ref text, frame);
                if (frame.Kind == Kind.Stream)
                {
                    text.Space();
                    text.Number(frame.Batch);
                    text.Space();
                    text.Number(frame.Credits);
                }

                break;
            case Kind.Credits:
                text.Byte((byte)'k');
                text.Space();
                text.Number(frame.Id);
                text.Space();
                text.Number(frame.Credits);
                break;
            case Kind.Cancel:
                text.Byte((byte)'x');
                text.Space();
                text.Number(frame.Id);
                break;
            case Kind.Result:
                text.Byte((byte)'r');
                text.Space();
                text.Number(frame.Id);
                break;
            case Kind.Items:
                text.Byte((byte)'i');
                text.Space();
                text.Number(frame.Id);
                text.Space();
                text.Number(frame.Dropped);
                break;
            case Kind.End:
            case Kind.Cancelled:
                text.Byte(frame.Kind == Kind.End ? (byte)'e' : (byte)'c');
                text.Space();
                text.Number(frame.Id);
                break;
            case Kind.NotExecuted:
            case Kind.OutcomeUnknown:
                text.Byte(frame.Kind == Kind.NotExecuted ? (byte)'n' : (byte)'u');
                text.Space();
                text.Number(frame.Id);
                text.Space();
                text.Ascii(frame.Code);
                break;
            default:
                throw new ArgumentException("Cannot encode " + frame.Kind, nameof(frame));
        }

        if (!frame.Payload.IsEmpty)
        {
            text.Space();
            text.Commit();
            output.Write(frame.Payload.Span);
            return;
        }

        if (frame.Kind is Kind.NotExecuted or Kind.OutcomeUnknown && frame.Detail is not null)
        {
            text.Space();
            text.Commit();
            var writer = JsonHeader.Writer(output);
            writer.WriteStartObject();
            writer.WriteString("detail"u8, frame.Detail);
            writer.WriteEndObject();
            writer.Flush();
            return;
        }

        text.Commit();
    }

    public DecodeResult TryRead(ReadOnlyMemory<byte> data, out Frame frame)
    {
        frame = default;
        frame.Id = -1;
        var span = data.Span;
        if (span.IsEmpty)
        {
            return DecodeResult.Fail("empty frame");
        }

        if (span.Length > 1 && span[1] != (byte)' ')
        {
            return DecodeResult.Fail("the kind must be one letter followed by a space");
        }

        var reader = new TokenReader(span, 2);
        var letter = span[0];
        switch (letter)
        {
            case (byte)'h':
            case (byte)'o':
                frame.Kind = letter == 'h' ? Kind.Hello : Kind.Open;
                frame.Doc = reader.String();
                if (reader.More)
                {
                    if (!reader.Int(out frame.Protocol))
                    {
                        return DecodeResult.Fail("bad protocol version");
                    }
                }

                return reader.More ? DecodeResult.Fail("unexpected trailing data") : Check(frame.Doc is not null, "missing doc");
            case (byte)'z':
                frame.Kind = Kind.ClosingDone;
                return span.Length == 1 ? DecodeResult.Success : DecodeResult.Fail("unexpected trailing data");
            case (byte)'q':
            case (byte)'s':
                {
                    frame.Kind = letter == 'q' ? Kind.Call : Kind.Stream;
                    if (!reader.Id(out frame.Id))
                    {
                        return DecodeResult.Fail("bad id");
                    }

                    frame.Contract = reader.String();
                    frame.Command = reader.String();
                    frame.Fingerprint = reader.String();
                    var trace = reader.Token();
                    if (frame.Fingerprint is null || trace.IsEmpty)
                    {
                        return DecodeResult.Fail("missing header field");
                    }

                    if (!ReadTrace(trace, ref frame))
                    {
                        return DecodeResult.Fail("bad trace field");
                    }

                    if (frame.Kind == Kind.Stream && (!reader.Int(out frame.Batch) || !reader.Int(out frame.Credits)))
                    {
                        return DecodeResult.Fail("bad batch or credits");
                    }

                    frame.Payload = data[reader.Position..];
                    return DecodeResult.Success;
                }

            case (byte)'k':
                frame.Kind = Kind.Credits;
                if (!reader.Id(out frame.Id))
                {
                    return DecodeResult.Fail("bad id");
                }

                if (!reader.Int(out frame.Credits))
                {
                    return DecodeResult.Fail("bad credits");
                }

                return reader.More ? DecodeResult.Fail("unexpected trailing data") : DecodeResult.Success;
            case (byte)'x':
            case (byte)'e':
            case (byte)'c':
                frame.Kind = letter switch { (byte)'x' => Kind.Cancel, (byte)'e' => Kind.End, _ => Kind.Cancelled };
                if (!reader.Id(out frame.Id))
                {
                    return DecodeResult.Fail("bad id");
                }

                return reader.More ? DecodeResult.Fail("unexpected trailing data") : DecodeResult.Success;
            case (byte)'r':
                frame.Kind = Kind.Result;
                if (!reader.Id(out frame.Id))
                {
                    return DecodeResult.Fail("bad id");
                }

                frame.Payload = data[reader.Position..];
                return Check(!frame.Payload.IsEmpty, "missing result");
            case (byte)'i':
                frame.Kind = Kind.Items;
                if (!reader.Id(out frame.Id))
                {
                    return DecodeResult.Fail("bad id");
                }

                if (!reader.Long(out frame.Dropped))
                {
                    return DecodeResult.Fail("bad dropped count");
                }

                frame.Payload = data[reader.Position..];
                return Check(!frame.Payload.IsEmpty, "missing items");
            case (byte)'n':
            case (byte)'u':
                frame.Kind = letter == 'n' ? Kind.NotExecuted : Kind.OutcomeUnknown;
                if (!reader.Id(out frame.Id))
                {
                    return DecodeResult.Fail("bad id");
                }

                frame.Code = reader.String();
                frame.Payload = data[reader.Position..];
                return Check(frame.Code is not null, "missing code");
            default:
                // A newer peer's kind: by convention the second token is still the call id.
                frame.Kind = Kind.Unrecognized;
                frame.UnrecognizedKind = ((char)letter).ToString();
                if (reader.More && reader.Id(out var id))
                {
                    frame.Id = id;
                }

                return DecodeResult.Success;
        }
    }

    private static DecodeResult Check(bool condition, string error) => condition ? DecodeResult.Success : DecodeResult.Fail(error);

    private static int Length(string? value) => value?.Length ?? 0;

    private static void WriteTrace(ref TextWriter text, in Frame frame)
    {
        if (frame.TraceParent is null)
        {
            text.Byte((byte)'-');
            return;
        }

        text.Ascii(frame.TraceParent);
        if (frame.TraceState is not null)
        {
            text.Byte((byte)';');
            text.Ascii(Uri.EscapeDataString(frame.TraceState));
        }
    }

    private static bool ReadTrace(ReadOnlySpan<byte> token, ref Frame frame)
    {
        if (token.Length == 1 && token[0] == (byte)'-')
        {
            return true;
        }

        var split = token.IndexOf((byte)';');
        var parent = split < 0 ? token : token[..split];
        if (parent.Length > 128)
        {
            return false;
        }

        frame.TraceParent = Encoding.UTF8.GetString(parent);
        if (split >= 0)
        {
            frame.TraceState = Uri.UnescapeDataString(Encoding.UTF8.GetString(token[(split + 1)..]));
        }

        return true;
    }

    /// <summary>Reads space-separated tokens.</summary>
    private ref struct TokenReader(ReadOnlySpan<byte> span, int position)
    {
        private readonly ReadOnlySpan<byte> _span = span;

        public int Position { get; private set; } = Math.Min(position, span.Length);

        public readonly bool More => Position < _span.Length;

        public ReadOnlySpan<byte> Token()
        {
            var rest = _span[Position..];
            var end = rest.IndexOf((byte)' ');
            if (end < 0)
            {
                Position = _span.Length;
                return rest;
            }

            Position += end + 1;
            return rest[..end];
        }

        public string? String()
        {
            var token = Token();
            return token.IsEmpty ? null : Encoding.UTF8.GetString(token);
        }

        public bool Id(out long id)
        {
            if (Long(out id) && id <= Codecs.MaxId)
            {
                return true;
            }

            id = -1;
            return false;
        }

        public bool Int(out int value)
        {
            var ok = Long(out var wide) && wide <= int.MaxValue;
            value = ok ? (int)wide : 0;
            return ok;
        }

        /// <summary>A canonical non-negative decimal: no sign, no leading zeros, at most 18 digits.</summary>
        public bool Long(out long value)
        {
            value = 0;
            var token = Token();
            if (token.IsEmpty || token.Length > 18 || (token[0] == (byte)'0' && token.Length > 1))
            {
                return false;
            }

            foreach (var digit in token)
            {
                if (digit is < (byte)'0' or > (byte)'9')
                {
                    value = 0;
                    return false;
                }

                value = value * 10 + (digit - '0');
            }

            return true;
        }
    }

    /// <summary>Writes an ASCII header into one span taken from the output.</summary>
    private ref struct TextWriter(IBufferWriter<byte> output, int sizeHint)
    {
        private readonly IBufferWriter<byte> _output = output;
        private readonly Span<byte> _span = output.GetSpan(sizeHint);
        private int _written;

        public void Byte(byte value) => _span[_written++] = value;

        public void Space() => _span[_written++] = (byte)' ';

        public void Ascii(string? value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value), "A header field is missing.");
            }

            if (value.Length == 0 || value.AsSpan().ContainsAny(' ', '\n', '\t'))
            {
                throw new ArgumentException("A header field cannot be empty or contain spaces: '" + value + "'.", nameof(value));
            }

            _written += Encoding.UTF8.GetBytes(value, _span[_written..]);
        }

        public void Number(long value)
        {
            value.TryFormat(_span[_written..], out var count, default, System.Globalization.CultureInfo.InvariantCulture);
            _written += count;
        }

        public readonly void Commit() => _output.Advance(_written);
    }
}
