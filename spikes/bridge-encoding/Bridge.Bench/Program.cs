using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Bridge;

// Bridge.Bench: sizes, C# costs and C# robustness of encodings A (letters), B (JSON envelopes), C (JSON header + TAB).
// Usage: Bridge.Bench [sizes|cost|robust|corpus|all]   (default all). Writes results/ and artifacts/bridge-encoding/.
var what = args.Length > 0 ? args[0] : "all";
var root = Paths.Root();
var results = Path.Combine(root, "spikes", "bridge-encoding", "results");
var artifacts = Path.Combine(root, "artifacts", "bridge-encoding");
Directory.CreateDirectory(results);
Directory.CreateDirectory(artifacts);
var invariant = CultureInfo.InvariantCulture;

if (what is "all" or "corpus")
{
    File.WriteAllBytes(Path.Combine(artifacts, "corpus.js"), Corpus.Script());
    Console.WriteLine("corpus.js written");
}

if (what is "all" or "samples")
{
    var md = new StringBuilder("# What each encoding looks like in a log\n\nWritten by `Bridge.Bench samples` (TAB shown as `→`).\n\n");
    foreach (var codec in Codecs.All)
    {
        md.Append("## ").Append(codec.Name).Append("\n\n```text\n");
        foreach (var frame in new[]
        {
            Corpus.PageToHost("hello"), Corpus.HostToPage("open"), Corpus.PageToHost("call-note-traced"), Corpus.HostToPage("result-note"),
            Corpus.PageToHost("stream-rows"), new Frame { Kind = Kind.Items, Id = 1234, Payload = Payloads.Rows(0, 2) },
            Corpus.PageToHost("credits"), Corpus.PageToHost("cancel"), Corpus.HostToPage("cancelled"), Corpus.HostToPage("result-failure"),
            Corpus.HostToPage("not-executed"),
        })
        {
            md.Append(Encoding.UTF8.GetString(Corpus.Encode(codec, frame)).Replace("\t", "→", StringComparison.Ordinal)).Append('\n');
        }

        md.Append("```\n\n");
    }

    File.WriteAllText(Path.Combine(results, "samples.md"), md.ToString(), Encoding.UTF8);
    Console.WriteLine("samples.md written");
}

if (what is "all" or "sizes")
{
    File.WriteAllText(Path.Combine(results, "sizes.md"), Sizes(), Encoding.UTF8);
    Console.WriteLine("sizes.md written");
}

if (what is "all" or "robust")
{
    File.WriteAllText(Path.Combine(results, "robustness-csharp.md"), Robustness(), Encoding.UTF8);
    Console.WriteLine("robustness-csharp.md written");
}

if (what is "all" or "cost")
{
    var text = Costs();
    File.WriteAllText(Path.Combine(results, "cost-csharp.md"), text, Encoding.UTF8);
    Console.WriteLine(text);
}

return 0;

// ---------------------------------------------------------------------------------------------- sizes

string Sizes()
{
    var md = new StringBuilder();
    md.AppendLine("# Encoded sizes (UTF-8 bytes)").AppendLine();
    md.AppendLine("Written by `Bridge.Bench sizes`. Payload bytes are identical for every encoding; the difference is the header or envelope.").AppendLine();
    md.AppendLine("## Per message").AppendLine();
    md.AppendLine("| Message | Payload | A | B | C | B − A | C − A |");
    md.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
    foreach (var name in Corpus.PageToHostNames)
    {
        Row(name + " (page→host)", Corpus.PageToHost(name));
    }

    foreach (var name in Corpus.HostToPageNames)
    {
        Row(name + " (host→page)", Corpus.HostToPage(name));
    }

    void Row(string name, Frame frame)
    {
        var a = Corpus.Encode(Codecs.A, frame).Length;
        var b = Corpus.Encode(Codecs.B, frame).Length;
        var c = Corpus.Encode(Codecs.C, frame).Length;
        md.AppendLine(invariant, $"| {name} | {frame.Payload.Length} | {a} | {b} | {c} | {b - a:+0;-0;0} | {c - a:+0;-0;0} |");
    }

    md.AppendLine().AppendLine("## Whole exchanges").AppendLine();
    md.AppendLine("Credits: window 8, refilled in halves (one `credits` message per 4 batches), as in `spikes/react`.").AppendLine();
    md.AppendLine("| Exchange | Messages | Payload bytes | A | B | C | B overhead vs A | C overhead vs A |");
    md.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");
    Exchange("unary call + result (note)", [Corpus.PageToHost("call-note"), Corpus.HostToPage("result-note")]);
    Exchange("unary call with traceparent + result", [Corpus.PageToHost("call-note-traced"), Corpus.HostToPage("result-note")]);
    Exchange("unary call with traceparent + tracestate + result", [Corpus.PageToHost("call-note-traced-state"), Corpus.HostToPage("result-note")]);
    Exchange("cancel + cancelled", [Corpus.PageToHost("cancel"), Corpus.HostToPage("cancelled")]);
    Exchange("100k rows, batch 1000", StreamFrames(100_000, 1000));
    Exchange("100k rows, batch 100", StreamFrames(100_000, 100));
    Exchange("100k rows, batch 1", StreamFrames(100_000, 1));
    foreach (var perFrame in new[] { 1, 20, 300 })
    {
        Exchange($"60,000 push events (20,000/s × 3 s), {perFrame} per frame", PushFrames(60_000, perFrame));
    }

    void Exchange(string name, IEnumerable<Frame> frames)
    {
        long count = 0, payload = 0, a = 0, b = 0, c = 0;
        var buffer = new ArrayBufferWriter<byte>(1 << 20);
        foreach (var frame in frames)
        {
            count++;
            payload += frame.Payload.Length;
            a += Size(Codecs.A, frame, buffer);
            b += Size(Codecs.B, frame, buffer);
            c += Size(Codecs.C, frame, buffer);
        }

        md.AppendLine(invariant, $"| {name} | {count:N0} | {payload:N0} | {a:N0} | {b:N0} | {c:N0} | {b - a:+#,0;-#,0;0} ({Percent(b - a, a)}) | {c - a:+#,0;-#,0;0} ({Percent(c - a, a)}) |");
    }

    return md.ToString();
}

static string Percent(long delta, long total) => (100.0 * delta / total).ToString("+0.00;-0.00;0", CultureInfo.InvariantCulture) + "%";

static long Size(ICodec codec, in Frame frame, ArrayBufferWriter<byte> buffer)
{
    buffer.ResetWrittenCount();
    codec.Write(frame, buffer);
    return buffer.WrittenCount;
}

static IEnumerable<Frame> StreamFrames(long rows, int batch)
{
    yield return Corpus.PageToHost("stream-rows") with { Batch = batch };
    var sentBatches = 0;
    for (long first = 0; first < rows; first += batch)
    {
        var n = (int)Math.Min(batch, rows - first);
        yield return new Frame { Kind = Kind.Items, Id = 1234, Payload = Payloads.Rows(first, n) };
        if (++sentBatches % 4 == 0)
        {
            yield return new Frame { Kind = Kind.Credits, Id = 1234, Credits = 4 };
        }
    }

    yield return Frame.Of(Kind.End, 1234);
}

static IEnumerable<Frame> PushFrames(long events, int perFrame)
{
    yield return Corpus.PageToHost("stream-rows") with { Command = "watch", Batch = 1000, Payload = "{\"rate\":20000,\"ms\":3000,\"capacity\":1000}"u8.ToArray() };
    var frames = 0;
    for (long first = 0; first < events; first += perFrame)
    {
        yield return new Frame { Kind = Kind.Items, Id = 1234, Payload = Payloads.Events(40_000_000 + first, (int)Math.Min(perFrame, events - first)) };
        if (++frames % 4 == 0)
        {
            yield return new Frame { Kind = Kind.Credits, Id = 1234, Credits = 4 };
        }
    }

    yield return Frame.Of(Kind.End, 1234);
}

// ---------------------------------------------------------------------------------------------- costs

string Costs()
{
    var md = new StringBuilder();
    md.AppendLine("# C# encode and decode cost (NativeAOT, ns per message, median of 7 samples)").AppendLine();
    md.AppendLine(invariant, $"Written by `Bridge.Bench cost`. {RuntimeInformationText()}").AppendLine();
    md.AppendLine("- **encode**: the header or envelope written around an already buffered payload, into a reused `ArrayBufferWriter`.");
    md.AppendLine("- **encode + string**: plus the UTF-8 → UTF-16 conversion that `PostWebMessageAsString` needs.");
    md.AppendLine("- **decode**: header parsed and payload located (B skips over `arg`/`items` with `Utf8JsonReader.Skip`).");
    md.AppendLine("- **decode + validate**: plus one full `Utf8JsonReader` pass over the payload, the least a generated reader does.");
    md.AppendLine("- **from string**: the host receives a UTF-16 string from WebView2 and transcodes it to UTF-8 first.").AppendLine();

    md.AppendLine("## Host → page (encode)").AppendLine();
    md.AppendLine("| Message | Bytes A/B/C | encode A | encode B | encode C | + string A | + string B | + string C |");
    md.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|");
    foreach (var name in Corpus.HostToPageNames)
    {
        var frame = Corpus.HostToPage(name);
        var encode = Compare(codec => () => Encode(codec, frame));
        var withString = Compare(codec => () => EncodeToString(codec, frame));
        md.AppendLine(invariant, $"| {name} | {Sizes3(frame)} | {encode[0]:F0} | {encode[1]:F0} | {encode[2]:F0} | {withString[0]:F0} | {withString[1]:F0} | {withString[2]:F0} |");
    }

    md.AppendLine().AppendLine("## Page → host (decode)").AppendLine();
    md.AppendLine("| Message | Bytes A/B/C | decode A | decode B | decode C | + validate A | + validate B | + validate C | from string + decode + validate A | B | C |");
    md.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
    foreach (var name in Corpus.PageToHostNames)
    {
        var frame = Corpus.PageToHost(name);
        var encoded = Codecs.All.Select(codec => (ReadOnlyMemory<byte>)Corpus.Encode(codec, frame)).ToArray();
        var strings = encoded.Select(bytes => Encoding.UTF8.GetString(bytes.Span)).ToArray();
        var decode = Compare(codec => { var data = encoded[Index(codec)]; return () => Decode(codec, data, validate: false); });
        var validate = Compare(codec => { var data = encoded[Index(codec)]; return () => Decode(codec, data, validate: true); });
        var fromString = Compare(codec => { var text = strings[Index(codec)]; return () => DecodeString(codec, text); });
        md.AppendLine(invariant, $"| {name} | {Sizes3(frame)} | {decode[0]:F0} | {decode[1]:F0} | {decode[2]:F0} | {validate[0]:F0} | {validate[1]:F0} | {validate[2]:F0} | {fromString[0]:F0} | {fromString[1]:F0} | {fromString[2]:F0} |");
    }

    // A large argument shows B's second pass over the payload.
    md.AppendLine().AppendLine("## Page → host with a large argument (B's extra pass over `arg`)").AppendLine();
    md.AppendLine("| Argument | decode A | decode B | decode C | + validate A | + validate B | + validate C |");
    md.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
    foreach (var rows in new[] { 10, 1000 })
    {
        var frame = Corpus.PageToHost("call-note") with { Payload = Payloads.Rows(0, rows) };
        var encoded = Codecs.All.Select(codec => (ReadOnlyMemory<byte>)Corpus.Encode(codec, frame)).ToArray();
        var decode = Compare(codec => { var data = encoded[Index(codec)]; return () => Decode(codec, data, validate: false); });
        var validate = Compare(codec => { var data = encoded[Index(codec)]; return () => Decode(codec, data, validate: true); });
        md.AppendLine(invariant, $"| {rows} rows ({frame.Payload.Length:N0} bytes) | {decode[0]:F0} | {decode[1]:F0} | {decode[2]:F0} | {validate[0]:F0} | {validate[1]:F0} | {validate[2]:F0} |");
    }

    return md.ToString();
}

static int Index(ICodec codec) => codec.Name switch { "A" => 0, "B" => 1, _ => 2 };

static string Sizes3(Frame frame) =>
    string.Join("/", Codecs.All.Select(codec => Corpus.Encode(codec, frame).Length.ToString(CultureInfo.InvariantCulture)));

static string RuntimeInformationText() =>
    $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}, {System.Runtime.InteropServices.RuntimeInformation.OSDescription}, " +
    $"NativeAOT: {!RuntimeFeature.IsDynamicCodeSupported}, {Environment.ProcessorCount} logical processors";

static double[] Compare(Func<ICodec, Func<long>> make)
{
    var actions = Codecs.All.Select(make).ToArray();
    var samples = actions.Select(_ => new List<double>()).ToArray();
    foreach (var action in actions)
    {
        Measure(action, 20);
    }

    for (var round = 0; round < 7; round++)
    {
        for (var i = 0; i < actions.Length; i++)
        {
            samples[i].Add(Measure(actions[i], 40));
        }
    }

    return [.. samples.Select(list => list.Order().ElementAt(list.Count / 2))];
}

static double Measure(Func<long> action, int minMs)
{
    long sink = 0;
    for (var iterations = 16L; ; iterations *= 2)
    {
        var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < iterations; i++)
        {
            sink += action();
        }

        var elapsed = Stopwatch.GetElapsedTime(start);
        if (elapsed.TotalMilliseconds >= minMs)
        {
            GC.KeepAlive(sink);
            return elapsed.TotalNanoseconds / iterations;
        }
    }
}

static long Encode(ICodec codec, in Frame frame)
{
    var buffer = Buffers.Frame;
    buffer.ResetWrittenCount();
    codec.Write(frame, buffer);
    return buffer.WrittenCount;
}

static long EncodeToString(ICodec codec, in Frame frame)
{
    var buffer = Buffers.Frame;
    buffer.ResetWrittenCount();
    codec.Write(frame, buffer);
    return Encoding.UTF8.GetString(buffer.WrittenSpan).Length;
}

static long Decode(ICodec codec, ReadOnlyMemory<byte> data, bool validate)
{
    var result = codec.TryRead(data, out var frame);
    if (!result.Ok)
    {
        throw new InvalidOperationException(codec.Name + ": " + result.Error);
    }

    if (validate && !frame.Payload.IsEmpty && !Payloads.Validate(frame.Payload.Span))
    {
        throw new InvalidOperationException("invalid payload");
    }

    return frame.Id + frame.Payload.Length;
}

static long DecodeString(ICodec codec, string text)
{
    var buffer = Buffers.Input;
    var bytes = buffer.GetSpan(Encoding.UTF8.GetMaxByteCount(text.Length));
    var count = Encoding.UTF8.GetBytes(text, bytes);
    var data = buffer.GetMemory(count)[..count];
    return Decode(codec, data, validate: true);
}

// ---------------------------------------------------------------------------------------------- robustness

string Robustness()
{
    var md = new StringBuilder();
    md.AppendLine("# C# decoder robustness (page → host frames)").AppendLine();
    md.AppendLine("Written by `Bridge.Bench robust`. \"decode\" is the header decoder; \"payload\" is a full `Utf8JsonReader` pass over the located payload, and \"strings\" also calls `GetString()` on every string token, as a generated reader does. \"id\" is the call id the decoder could still attribute the frame to (−1: none).").AppendLine();
    foreach (var codec in Codecs.All)
    {
        md.AppendLine(invariant, $"## {codec.Name}").AppendLine();
        md.AppendLine("| Case | Bytes | decode | id | kind | payload | strings |");
        md.AppendLine("|---|---:|---|---:|---|---|---|");
        foreach (var (name, frame) in Corpus.PageToHostCases(codec.Name))
        {
            var start = Stopwatch.GetTimestamp();
            var result = codec.TryRead(frame, out var decoded);
            var elapsed = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
            var payload = result.Ok && !decoded.Payload.IsEmpty ? (Payloads.Validate(decoded.Payload.Span) ? "valid" : "**invalid**") : "—";
            var strings = result.Ok && !decoded.Payload.IsEmpty ? Strings(decoded.Payload.Span) : "—";
            var kind = decoded.Kind == Kind.Unrecognized ? "unrecognized `" + decoded.UnrecognizedKind + "`" : decoded.Kind.ToString();
            var extra = decoded.TraceState is { } state ? $" (tracestate `{Escape(state)}`)" : "";
            var outcome = result.Ok ? "ok" + extra : "**rejected**: " + Escape(result.Error ?? "");
            md.AppendLine(invariant, $"| {name} | {frame.Length:N0} | {outcome} ({elapsed:F1} µs) | {decoded.Id} | {kind} | {payload} | {strings} |");
        }

        md.AppendLine();
    }

    md.AppendLine("## Writer output for awkward strings (shared by all encodings)").AppendLine();
    md.AppendLine("| Input | Default encoder | `UnsafeRelaxedJsonEscaping` |");
    md.AppendLine("|---|---|---|");
    foreach (var (name, value) in new[]
    {
        ("newline, tab", "a\nb\tc"),
        ("U+2028, U+2029", "a\u2028b\u2029c"),
        ("lone surrogate U+D800", "a\ud800b"),
        ("é, 日本", "é日本"),
        ("<script>&'", "<script>&'"),
    })
    {
        var standard = Encoding.UTF8.GetString(Payloads.Write(writer => writer.WriteStringValue(value)));
        var relaxed = Encoding.UTF8.GetString(Payloads.Write(writer => writer.WriteStringValue(value), JavaScriptEncoder.UnsafeRelaxedJsonEscaping));
        md.AppendLine(invariant, $"| {name} | `{Escape(Visible(standard))}` | `{Escape(Visible(relaxed))}` |");
    }

    return md.ToString();
}

static string Strings(ReadOnlySpan<byte> json)
{
    var reader = new Utf8JsonReader(json);
    try
    {
        while (reader.Read())
        {
            if (reader.TokenType is JsonTokenType.String or JsonTokenType.PropertyName)
            {
                _ = reader.GetString();
            }
        }

        return "ok";
    }
    catch (Exception exception) when (exception is JsonException or InvalidOperationException)
    {
        return "**" + exception.GetType().Name + "**";
    }
}

static string Escape(string text) => text.Replace("|", "\\|", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);

static string Visible(string text)
{
    var builder = new StringBuilder();
    foreach (var ch in text)
    {
        builder.Append(ch is < ' ' or '\u2028' or '\u2029' or (>= '\ud800' and <= '\udfff') ? $"<U+{(int)ch:X4}>" : ch.ToString());
    }

    return builder.ToString();
}

internal static class Buffers
{
    [ThreadStatic]
    private static ArrayBufferWriter<byte>? t_frame;

    [ThreadStatic]
    private static ArrayBufferWriter<byte>? t_input;

    public static ArrayBufferWriter<byte> Frame => t_frame ??= new ArrayBufferWriter<byte>(1 << 20);

    public static ArrayBufferWriter<byte> Input
    {
        get
        {
            var buffer = t_input ??= new ArrayBufferWriter<byte>(1 << 20);
            buffer.ResetWrittenCount();
            return buffer;
        }
    }
}

internal static class Paths
{
    public static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Repository root (global.json) not found.");
    }
}
