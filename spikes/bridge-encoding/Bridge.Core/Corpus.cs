using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Bridge;

/// <summary>
/// The message mix, encoded by every codec: the frames the page decodes in its micro benchmark, the page-to-host
/// frames the C# benchmark decodes, and the malformed or newer frames of the robustness checks.
/// </summary>
public static class Corpus
{
    public static readonly string[] HostToPageNames =
    [
        "open", "result-void", "result-echo", "result-note", "result-failure", "items-rows-100", "items-rows-1000",
        "items-event-1", "items-events-20", "items-events-300", "end", "cancelled", "not-executed", "outcome-unknown",
    ];

    public static readonly string[] PageToHostNames =
    [
        "hello", "call-note", "call-note-traced", "call-note-traced-state", "call-void", "stream-rows", "stream-rows-traced",
        "credits", "cancel",
    ];

    public static Frame HostToPage(string name) => name switch
    {
        "open" => new Frame { Kind = Kind.Open, Id = -1, Doc = "k3j9x2ab", Protocol = 1 },
        "result-void" => new Frame { Kind = Kind.Result, Id = 1234, Payload = Payloads.VoidResult() },
        "result-echo" => new Frame { Kind = Kind.Result, Id = 1234, Payload = EchoResult() },
        "result-note" => new Frame { Kind = Kind.Result, Id = 1234, Payload = Payloads.NoteResult(1234) },
        "result-failure" => new Frame { Kind = Kind.Result, Id = 1234, Payload = Payloads.SerializationFailure() },
        "items-rows-100" => new Frame { Kind = Kind.Items, Id = 1234, Payload = Payloads.Rows(50_000, 100) },
        "items-rows-1000" => new Frame { Kind = Kind.Items, Id = 1234, Payload = Payloads.Rows(50_000, 1000) },
        "items-event-1" => new Frame { Kind = Kind.Items, Id = 1234, Payload = Payloads.Events(40_000, 1) },
        "items-events-20" => new Frame { Kind = Kind.Items, Id = 1234, Payload = Payloads.Events(40_000, 20) },
        "items-events-300" => new Frame { Kind = Kind.Items, Id = 1234, Dropped = 17, Payload = Payloads.Events(40_000, 300) },
        "end" => Frame.Of(Kind.End, 1234),
        "cancelled" => Frame.Of(Kind.Cancelled, 1234),
        "not-executed" => new Frame { Kind = Kind.NotExecuted, Id = 1234, Code = "contract-mismatch", Detail = Payloads.Fingerprint },
        "outcome-unknown" => new Frame { Kind = Kind.OutcomeUnknown, Id = 1234, Code = "failed", Detail = "InvalidOperationException: Sequence contains no elements" },
        _ => throw new ArgumentException(name, nameof(name)),
    };

    public static Frame PageToHost(string name) => name switch
    {
        "hello" => new Frame { Kind = Kind.Hello, Id = -1, Doc = "k3j9x2ab", Protocol = 1 },
        "call-note" => Call(Kind.Call, "save", null, null, Payloads.NoteRequest()),
        "call-note-traced" => Call(Kind.Call, "save", Payloads.TraceParent, null, Payloads.NoteRequest()),
        "call-note-traced-state" => Call(Kind.Call, "save", Payloads.TraceParent, Payloads.TraceState, Payloads.NoteRequest()),
        "call-void" => Call(Kind.Call, "count", null, null, default),
        "stream-rows" => Call(Kind.Stream, "rows", null, null, "{\"count\":100000}"u8.ToArray()) with { Batch = 1000, Credits = 8 },
        "stream-rows-traced" => Call(Kind.Stream, "rows", Payloads.TraceParent, null, "{\"count\":100000}"u8.ToArray()) with { Batch = 1000, Credits = 8 },
        "credits" => new Frame { Kind = Kind.Credits, Id = 1234, Credits = 4 },
        "cancel" => Frame.Of(Kind.Cancel, 1234),
        _ => throw new ArgumentException(name, nameof(name)),
    };

    /// <summary>The payload alone, for the "JSON.parse of the payload only" baseline (null when there is none).</summary>
    public static byte[]? PayloadOf(string name)
    {
        var frame = HostToPage(name);
        return frame.Payload.IsEmpty ? null : frame.Payload.ToArray();
    }

    public static byte[] Encode(ICodec codec, in Frame frame)
    {
        var buffer = new ArrayBufferWriter<byte>(1024);
        codec.Write(frame, buffer);
        return buffer.WrittenSpan.ToArray();
    }

    private static Frame Call(Kind kind, string command, string? traceParent, string? traceState, ReadOnlyMemory<byte> arg) => new()
    {
        Kind = kind,
        Id = 1234,
        Contract = "bench",
        Command = command,
        Fingerprint = Payloads.Fingerprint,
        TraceParent = traceParent,
        TraceState = traceState,
        Payload = arg,
    };

    private static byte[] EchoResult()
    {
        var note = Payloads.NoteRequest();
        return [.. "{\"ok\":true,\"value\":"u8, .. note, .. "}"u8];
    }

    // ------------------------------------------------------------------ robustness cases

    /// <summary>Host-to-page frames, malformed or from a newer host, for the page decoders.</summary>
    public static IReadOnlyList<(string Name, string Frame)> HostToPageCases(string codec)
    {
        var note = Encoding.UTF8.GetString(Payloads.NoteResult(7));
        var result = Encoding.UTF8.GetString(Encode(Codecs.Get(codec), new Frame { Kind = Kind.Result, Id = 7, Payload = Payloads.NoteResult(7) }));
        var cases = new List<(string, string)>();
        string Wrap(string kind, string id, string? extra, string? json) => codec switch
        {
            "A" => $"{kind} {id}" + (extra is null ? "" : " " + extra) + (json is null ? "" : " " + json),
            "B" => kind switch
            {
                "r" => $"{{\"type\":\"result\",\"id\":{id}," + json![1..],
                "i" => $"{{\"type\":\"items\",\"id\":{id},\"dropped\":{extra ?? "0"},\"items\":{json}}}",
                _ => throw new ArgumentException(kind),
            },
            _ => kind switch
            {
                "r" => $"{{\"type\":\"result\",\"id\":{id}}}" + (json is null ? "" : "\t" + json),
                "i" => $"{{\"type\":\"items\",\"id\":{id},\"dropped\":{extra ?? "0"}}}" + (json is null ? "" : "\t" + json),
                _ => throw new ArgumentException(kind),
            },
        };

        cases.Add(("valid result", result));
        cases.Add(("escaped newline, U+2028, U+2029 in a string", Wrap("r", "7", null, "{\"ok\":true,\"value\":\"a\\nb\\u2028c\\u2029d\"}")));
        cases.Add(("raw U+2028 and U+2029 in a string", Wrap("r", "7", null, "{\"ok\":true,\"value\":\"a\u2028b\u2029c\"}")));
        cases.Add(("lone surrogate escape in a string", Wrap("r", "7", null, "{\"ok\":true,\"value\":\"a\\ud800b\"}")));
        cases.Add(("raw newline as JSON whitespace", Wrap("r", "7", null, "{\"ok\":true,\n\"value\":1}")));
        cases.Add(("id with 10,000 digits", Wrap("r", new string('9', 10_000), null, note)));
        cases.Add(("id 2^53 + 1", Wrap("r", "9007199254740993", null, note)));
        cases.Add(("id with a leading zero", Wrap("r", "07", null, note)));
        cases.Add(("negative id", Wrap("r", "-7", null, note)));
        cases.Add(("truncated in the payload", result[..(result.Length - 20)]));
        cases.Add(("truncated in the header", result[..Math.Min(result.Length, codec == "A" ? 2 : 12)]));
        cases.Add(("two frames concatenated", result + result));
        cases.Add(("items that are not an array", Wrap("i", "7", "0", "{\"a\":1}")));
        cases.Add(("result without a payload", Wrap("r", "7", null, codec == "B" ? "{}" : null)));
        cases.Add(("leading BOM", "\uFEFF" + result));
        switch (codec)
        {
            case "A":
                cases.Add(("unknown kind from a newer host", "p 7 {\"progress\":0.5}"));
                cases.Add(("newer host adds a header field to i", "i 7 0 3 [{\"seq\":\"1\"}]"));
                cases.Add(("double space", "r  7 " + note));
                cases.Add(("unknown kind without an id", "v 2"));
                break;
            case "B":
                cases.Add(("unknown kind from a newer host", "{\"type\":\"progress\",\"id\":7,\"progress\":0.5}"));
                cases.Add(("newer host adds a header field to items", "{\"type\":\"items\",\"id\":7,\"dropped\":0,\"seqStart\":3,\"items\":[{\"seq\":\"1\"}]}"));
                cases.Add(("duplicate id key", "{\"type\":\"result\",\"id\":7,\"id\":8,\"ok\":true}"));
                cases.Add(("type key last", "{\"id\":7,\"ok\":true,\"value\":1,\"type\":\"result\"}"));
                break;
            default:
                cases.Add(("unknown kind from a newer host", "{\"type\":\"progress\",\"id\":7}\t{\"progress\":0.5}"));
                cases.Add(("newer host adds a header field to items", "{\"type\":\"items\",\"id\":7,\"dropped\":0,\"seqStart\":3}\t[{\"seq\":\"1\"}]"));
                cases.Add(("duplicate id key", "{\"type\":\"result\",\"id\":7,\"id\":8}\t{\"ok\":true}"));
                cases.Add(("raw TAB inside a header string", "{\"type\":\"result\",\"id\":7,\"x\":\"a\tb\"}\t{\"ok\":true}"));
                break;
        }

        return cases;
    }

    /// <summary>Page-to-host frames, malformed, hostile or from a newer page, for the C# decoders.</summary>
    public static IReadOnlyList<(string Name, byte[] Frame)> PageToHostCases(string codec)
    {
        var c = Codecs.Get(codec);
        var call = Encode(c, PageToHost("call-note-traced-state"));
        var cases = new List<(string, byte[])>
        {
            ("valid call", call),
            ("truncated in the payload", call[..^10]),
            ("truncated in the header", call[..Math.Min(call.Length, codec == "A" ? 9 : 20)]),
            ("two frames concatenated", [.. call, .. call]),
            ("leading BOM", [0xEF, 0xBB, 0xBF, .. call]),
            ("empty", []),
        };

        string Q(string id, string contract, string trace, string arg) => codec switch
        {
            "A" => $"q {id} {contract} save {Payloads.Fingerprint} {trace} {arg}",
            "B" => $"{{\"type\":\"call\",\"id\":{id},\"contract\":\"{contract}\",\"command\":\"save\",\"fingerprint\":\"{Payloads.Fingerprint}\""
                + (trace == "-" ? "" : $",\"traceparent\":\"{trace}\"") + $",\"arg\":{arg}}}",
            _ => $"{{\"type\":\"call\",\"id\":{id},\"contract\":\"{contract}\",\"command\":\"save\",\"fingerprint\":\"{Payloads.Fingerprint}\""
                + (trace == "-" ? "" : $",\"traceparent\":\"{trace}\"") + "}\t" + arg,
        };

        var arg = "{\"title\":\"t\",\"body\":\"b\",\"tags\":[]}";
        cases.Add(("id with 10,000 digits", Utf8(Q(new string('9', 10_000), "bench", "-", arg))));
        cases.Add(("id 2^53 + 1", Utf8(Q("9007199254740993", "bench", "-", arg))));
        cases.Add(("id as a decimal fraction", Utf8(Q("7.5", "bench", "-", arg))));
        cases.Add(("lone surrogate escape in the argument", Utf8(Q("7", "bench", "-", "{\"title\":\"a\\ud800b\",\"body\":\"b\",\"tags\":[]}"))));
        cases.Add(("invalid UTF-8 in the argument", Splice(Utf8(Q("7", "bench", "-", "{\"title\":\"@@\",\"body\":\"b\",\"tags\":[]}")), [0xC3, 0x28])));
        cases.Add(("raw newline as JSON whitespace in the argument", Utf8(Q("7", "bench", "-", "{\"title\":\"t\",\n\"body\":\"b\",\"tags\":[]}"))));
        cases.Add(("10 MB argument", Utf8(Q("7", "bench", "-", "{\"title\":\"" + new string('x', 10_000_000) + "\",\"body\":\"b\",\"tags\":[]}"))));
        switch (codec)
        {
            case "A":
                cases.Add(("contract with a space", Utf8(Q("7", "be nch", "-", arg))));
                cases.Add(("tracestate with a percent-encoded surrogate (%ED%A0%80)", Utf8($"q 7 bench save {Payloads.Fingerprint} {Payloads.TraceParent};k=%ED%A0%80 {arg}")));
                cases.Add(("tracestate with a raw space", Utf8($"q 7 bench save {Payloads.Fingerprint} {Payloads.TraceParent};k=a b {arg}")));
                cases.Add(("unknown kind from a newer page", Utf8("p 7 {\"progress\":0.5}")));
                cases.Add(("newer page adds a header field to q", Utf8($"q 7 bench save {Payloads.Fingerprint} - 500 {arg}")));
                cases.Add(("cancel with trailing data", Utf8("x 7 now")));
                break;
            case "B":
                cases.Add(("lone surrogate escape in tracestate", Utf8($"{{\"type\":\"call\",\"id\":7,\"contract\":\"bench\",\"command\":\"save\",\"fingerprint\":\"{Payloads.Fingerprint}\",\"traceparent\":\"{Payloads.TraceParent}\",\"tracestate\":\"k=\\ud800\",\"arg\":{arg}}}")));
                cases.Add(("unknown kind from a newer page", Utf8("{\"type\":\"progress\",\"id\":7,\"progress\":0.5}")));
                cases.Add(("newer page adds a header field to call", Utf8($"{{\"type\":\"call\",\"id\":7,\"contract\":\"bench\",\"command\":\"save\",\"fingerprint\":\"{Payloads.Fingerprint}\",\"deadlineMs\":500,\"arg\":{arg}}}")));
                cases.Add(("duplicate id key in cancel", Utf8("{\"type\":\"cancel\",\"id\":7,\"id\":8}")));
                cases.Add(("arg before type", Utf8($"{{\"arg\":{arg},\"type\":\"call\",\"id\":7,\"contract\":\"bench\",\"command\":\"save\",\"fingerprint\":\"{Payloads.Fingerprint}\"}}")));
                cases.Add(("id as a string", Utf8("{\"type\":\"cancel\",\"id\":\"7\"}")));
                break;
            default:
                cases.Add(("lone surrogate escape in tracestate", Utf8($"{{\"type\":\"call\",\"id\":7,\"contract\":\"bench\",\"command\":\"save\",\"fingerprint\":\"{Payloads.Fingerprint}\",\"traceparent\":\"{Payloads.TraceParent}\",\"tracestate\":\"k=\\ud800\"}}\t{arg}")));
                cases.Add(("unknown kind from a newer page", Utf8("{\"type\":\"progress\",\"id\":7}\t{\"progress\":0.5}")));
                cases.Add(("newer page adds a header field to call", Utf8($"{{\"type\":\"call\",\"id\":7,\"contract\":\"bench\",\"command\":\"save\",\"fingerprint\":\"{Payloads.Fingerprint}\",\"deadlineMs\":500}}\t{arg}")));
                cases.Add(("duplicate id key in cancel", Utf8("{\"type\":\"cancel\",\"id\":7,\"id\":8}")));
                cases.Add(("raw TAB inside a header string", Utf8("{\"type\":\"cancel\",\"id\":7,\"x\":\"a\tb\"}")));
                cases.Add(("id as a string", Utf8("{\"type\":\"cancel\",\"id\":\"7\"}")));
                break;
        }

        return cases;
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>Replaces the first <c>@@</c> with raw bytes (for invalid UTF-8).</summary>
    private static byte[] Splice(byte[] text, byte[] bytes)
    {
        var at = text.AsSpan().IndexOf("@@"u8);
        return [.. text[..at], .. bytes, .. text[(at + 2)..]];
    }

    // ------------------------------------------------------------------ corpus.js for the page

    /// <summary><c>globalThis.CORPUS = {…};</c> with every encoding's host-to-page frames and robustness cases.</summary>
    public static byte[] Script()
    {
        var buffer = new ArrayBufferWriter<byte>(1 << 20);
        buffer.Write("globalThis.CORPUS = "u8);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var codec in Codecs.All)
            {
                writer.WriteStartObject(codec.Name);
                foreach (var name in HostToPageNames)
                {
                    writer.WriteString(name, Encoding.UTF8.GetString(Encode(codec, HostToPage(name))));
                }

                writer.WriteEndObject();
            }

            writer.WriteStartObject("payloads");
            foreach (var name in HostToPageNames)
            {
                if (PayloadOf(name) is { } payload)
                {
                    writer.WriteString(name, Encoding.UTF8.GetString(payload));
                }
            }

            writer.WriteEndObject();
            writer.WriteStartObject("robustness");
            foreach (var codec in Codecs.All)
            {
                writer.WriteStartObject(codec.Name);
                foreach (var (name, frame) in HostToPageCases(codec.Name))
                {
                    writer.WriteString(name, frame);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        buffer.Write(";\n"u8);
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Reads an embedded page file (bench.html, bench.js).</summary>
    public static byte[] PageFile(string name)
    {
        using var stream = typeof(Corpus).Assembly.GetManifestResourceStream("page/" + name)
            ?? throw new FileNotFoundException("Embedded page file not found.", name);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
