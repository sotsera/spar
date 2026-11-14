using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace WireProbe;

/// <summary>Item 3: lone surrogates. What the writer and reader do, and a lossless prototype.</summary>
internal static class SurrogateProbe
{
    private static readonly (string Name, string Value)[] s_cases =
    [
        ("lone-high", "a\uD800b"),
        ("lone-low", "a\uDC00b"),
        ("reversed", "\uDC00\uD800"),
        ("trailing-high", "abc\uD83D"),
        ("valid-pair", "\uD83D\uDE00"),
        ("plain", "plain \"text\" \u00E9"),
    ];

    public static void Run()
    {
        Writers();
        Readers();
        LosslessRoundTrip();
        Benchmark();
    }

    private static void Writers()
    {
        Console.WriteLine("### Utf8JsonWriter with each encoder");
        (string Name, JavaScriptEncoder? Encoder)[] encoders =
        [
            ("null (default)", null),
            ("JavaScriptEncoder.Default", JavaScriptEncoder.Default),
            ("UnsafeRelaxedJsonEscaping", JavaScriptEncoder.UnsafeRelaxedJsonEscaping),
            ("Create(UnicodeRanges.All)", JavaScriptEncoder.Create(UnicodeRanges.All)),
        ];
        foreach (var (name, value) in s_cases[..4])
        {
            foreach (var (encoderName, encoder) in encoders)
            {
                var options = new JsonWriterOptions { Encoder = encoder };
                Probe.Line($"{name} WriteStringValue(string), {encoderName}", Probe.Write(w => w.WriteStringValue(value), options));
            }
        }

        var lone = "a\uD800b";
        Probe.Line("WriteStringValue(ReadOnlySpan<char>)", Probe.Write(w => w.WriteStringValue(lone.AsSpan())));
        Probe.Line("WriteString(property name with lone surrogate)", Probe.Write(w =>
        {
            w.WriteStartObject();
            w.WriteString(lone, "v");
            w.WriteEndObject();
        }));
        Probe.Line("JsonEncodedText.Encode", Probe.Try(() => Probe.Show(JsonEncodedText.Encode(lone).EncodedUtf8Bytes)));
        Probe.Line("SkipValidation = true", Probe.Write(w => w.WriteStringValue(lone), new JsonWriterOptions { SkipValidation = true }));
        Probe.Line("STJ JsonSerializer (string)", Probe.Try(() => Probe.Show(JsonSerializer.SerializeToUtf8Bytes(lone, DefaultContext.Default.String))));
        Probe.Line("Encoding.UTF8.GetBytes", Probe.Show(Encoding.UTF8.GetBytes(lone)));
        Probe.Line("UTF8Encoding(throwOnInvalidBytes: true).GetBytes", Probe.Try(() => Probe.Show(new UTF8Encoding(false, true).GetBytes(lone))));

        byte[] cesu = [(byte)'a', 0xED, 0xA0, 0x80, (byte)'b'];
        Probe.Line("WriteStringValue(UTF-8 bytes with CESU ED A0 80)", Probe.Write(w => w.WriteStringValue(cesu)));
        Probe.Line("WriteStringValue(UTF-8 bytes with FF)", Probe.Write(w => w.WriteStringValue([(byte)'a', 0xFF])));
        Probe.Line("WriteRawValue(\"\\ud800\") with validation", Probe.Write(w => w.WriteRawValue("\"\\ud800\""u8)));
    }

    private static void Readers()
    {
        Console.WriteLine("### Utf8JsonReader and friends");
        var inputs = new (string Label, byte[] Utf8)[]
        {
            ("\"\\ud800\"", "\"\\ud800\""u8.ToArray()),
            ("\"a\\udc00b\"", "\"a\\udc00b\""u8.ToArray()),
            ("\"\\udbff\\udfff\" (valid pair)", "\"\\udbff\\udfff\""u8.ToArray()),
            ("raw bytes ED A0 80 (CESU)", [(byte)'"', 0xED, 0xA0, 0x80, (byte)'"']),
            ("raw bytes EF BF BD (U+FFFD)", [(byte)'"', 0xEF, 0xBF, 0xBD, (byte)'"']),
        };
        foreach (var (label, utf8) in inputs)
        {
            Probe.Line($"{label} Read()", Probe.Read(utf8, (ref r) => $"{r.TokenType}, ValueIsEscaped={r.ValueIsEscaped}"));
            Probe.Line($"{label} GetString()", Probe.Read(utf8, (ref r) => r.GetString()));
            Probe.Line($"{label} CopyString(char[])", Probe.Read(utf8, (ref r) =>
            {
                var chars = new char[16];
                var n = r.CopyString(chars);
                return new string(chars, 0, n);
            }));
            Probe.Line($"{label} JsonDocument GetString()", Probe.Try(() =>
            {
                using var doc = JsonDocument.Parse(utf8);
                return doc.RootElement.GetString();
            }));
            Probe.Line($"{label} JsonSerializer (string)", Probe.Try(() => JsonSerializer.Deserialize(utf8, DefaultContext.Default.String)));
            Probe.Line($"{label} lossless prototype", Probe.Read(utf8, (ref r) => LosslessString.Read(ref r)));
            Probe.Line($"{label} replacing prototype", Probe.Read(utf8, (ref r) => LosslessString.Read(ref r, replace: true)));
        }
    }

    private static void LosslessRoundTrip()
    {
        Console.WriteLine("### Lossless prototype, C# -> JSON");
        var file = new ResultFile("surrogates.json");
        foreach (var (name, value) in s_cases)
        {
            var standard = Probe.WriteBytes(w => w.WriteStringValue(value));
            var lossless = Probe.WriteBytes(w => LosslessString.Write(w, value));
            var reader = new Utf8JsonReader(lossless, Probe.ReaderOptions);
            reader.Read();
            var back = LosslessString.Read(ref reader);
            Probe.Line(name, $"standard {Probe.Show(standard)}; lossless {Probe.Show(lossless)}; " +
                             $"read back equal {string.Equals(back, value, StringComparison.Ordinal)}; " +
                             $"identical to standard when well-formed {standard.AsSpan().SequenceEqual(lossless)}");
            file.AddRaw(name, lossless);
            file.AddRaw(name + "Units", Probe.WriteBytes(w =>
            {
                w.WriteStartArray();
                foreach (var c in value)
                {
                    w.WriteNumberValue((int)c);
                }

                w.WriteEndArray();
            }));
        }

        file.Save();
        Console.WriteLine("- wrote results/surrogates.json (node js/surrogates.mjs answers in results/surrogates-from-js.json)");
    }

    /// <summary>Reads what JSON.stringify produced in Node and compares the code units with the C# originals.</summary>
    public static void RunBack()
    {
        var path = Path.Combine("results", "surrogates-from-js.json");
        if (!File.Exists(path))
        {
            Console.WriteLine("- results/surrogates-from-js.json missing: run node js/surrogates.mjs first");
            return;
        }

        var bytes = File.ReadAllBytes(path);
        var reader = new Utf8JsonReader(bytes, Probe.ReaderOptions);
        reader.Read();
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var name = reader.GetString()!;
            reader.Read();
            var expected = s_cases.First(c => c.Name == name).Value;
            var raw = Probe.Show(reader.ValueSpan);
            var standard = TryGetString(ref reader);
            var lossless = LosslessString.Read(ref reader);
            Probe.Line($"from JS {name}",
                $"bytes {raw}; GetString {standard}; lossless equal to C# original {string.Equals(lossless, expected, StringComparison.Ordinal)}");
        }
    }

    private static string TryGetString(ref Utf8JsonReader reader)
    {
        try
        {
            return Probe.Units(reader.GetString());
        }
        catch (Exception e)
        {
            return Probe.Fail(e);
        }
    }

    private static void Benchmark()
    {
        const int count = 1_000_000;
        string[] samples = ["Customer 12345", "Ordini di maggio — città", "A longer value that looks like a description field.",
            "naïve café 😀 emoji"];
        var values = new string[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = samples[i % samples.Length] + i.ToString(CultureInfo.InvariantCulture);
        }

        var buffer = new ArrayBufferWriter<byte>(80 * count);
        var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { SkipValidation = true, MaxDepth = 64 });

        void WriteAll(bool lossless)
        {
            buffer.ResetWrittenCount();
            writer.Reset(buffer);
            writer.WriteStartArray();
            foreach (var v in values)
            {
                if (lossless)
                {
                    LosslessString.Write(writer, v);
                }
                else
                {
                    writer.WriteStringValue(v);
                }
            }

            writer.WriteEndArray();
            writer.Flush();
        }

        var standardWrite = Probe.Time(() => WriteAll(false));
        var standardBytes = buffer.WrittenSpan.ToArray();
        var losslessWrite = Probe.Time(() => WriteAll(true));
        var same = buffer.WrittenSpan.SequenceEqual(standardBytes);

        var length = 0;
        var standardRead = Probe.Time(() =>
        {
            var reader = new Utf8JsonReader(standardBytes, Probe.ReaderOptions);
            reader.Read();
            while (reader.Read() && reader.TokenType == JsonTokenType.String)
            {
                length += reader.GetString()!.Length;
            }
        });
        var losslessRead = Probe.Time(() =>
        {
            var reader = new Utf8JsonReader(standardBytes, Probe.ReaderOptions);
            reader.Read();
            while (reader.Read() && reader.TokenType == JsonTokenType.String)
            {
                length += LosslessString.Read(ref reader).Length;
            }
        });

        var replacingRead = Probe.Time(() =>
        {
            var reader = new Utf8JsonReader(standardBytes, Probe.ReaderOptions);
            reader.Read();
            while (reader.Read() && reader.TokenType == JsonTokenType.String)
            {
                length += LosslessString.Read(ref reader, replace: true).Length;
            }
        });

        // The slow path: 10,000 strings that each hold one unpaired surrogate escape.
        const int lonely = 10_000;
        var lonelyBytes = Probe.WriteBytes(w =>
        {
            w.WriteStartArray();
            for (var i = 0; i < lonely; i++)
            {
                LosslessString.Write(w, "Customer " + i.ToString(CultureInfo.InvariantCulture) + "\uD800");
            }

            w.WriteEndArray();
        });
        var lonelyRead = Probe.Time(() =>
        {
            var reader = new Utf8JsonReader(lonelyBytes, Probe.ReaderOptions);
            reader.Read();
            while (reader.Read() && reader.TokenType == JsonTokenType.String)
            {
                length += LosslessString.Read(ref reader, replace: true).Length;
            }
        });

        Console.WriteLine($"### Cost, {count:N0} well-formed strings (a quarter non-ASCII, a quarter escaped; min of 5 runs)");
        Probe.Line("write: WriteStringValue", $"{standardWrite} ms");
        Probe.Line("write: lossless (range scan first)", $"{losslessWrite} ms, output byte-identical {same}");
        Probe.Line("read: GetString", $"{standardRead} ms");
        Probe.Line("read: lossless (GetString, fallback only when it throws)", $"{losslessRead} ms");
        Probe.Line("read: replacing (same, U+FFFD on the fallback)", $"{replacingRead} ms ({length % 7})");
        Probe.Line($"read: {lonely:N0} strings that each take the fallback (exception + unescape)",
            $"{lonelyRead} ms, {lonelyRead * 1000 / lonely:F1} us per string");
    }
}

/// <summary>
/// Prototype: strings are sequences of UTF-16 code units on both sides. A lone surrogate is written as a \uXXXX escape
/// (valid JSON grammar, exactly what JSON.stringify emits) and read back as the same code unit. Well-formed strings take
/// the normal writer and reader paths and produce identical bytes.
/// </summary>
internal static class LosslessString
{
    public static void Write(Utf8JsonWriter writer, string value)
    {
        var span = value.AsSpan();
        if (span.IndexOfAnyInRange('\uD800', '\uDFFF') < 0 || IsWellFormed(span))
        {
            writer.WriteStringValue(value);
            return;
        }

        // Slow path: escape each lone surrogate, encode the well-formed runs with the default encoder.
        var output = new ArrayBufferWriter<byte>(value.Length * 6 + 2);
        output.Write("\""u8);
        Span<byte> escape = stackalloc byte[6];
        "\\u"u8.CopyTo(escape);
        var start = 0;
        for (var i = 0; i < span.Length; i++)
        {
            var c = span[i];
            if (char.IsHighSurrogate(c) && i + 1 < span.Length && char.IsLowSurrogate(span[i + 1]))
            {
                i++;
                continue;
            }

            if (char.IsSurrogate(c))
            {
                AppendEncoded(output, span[start..i]);
                ((int)c).TryFormat(escape[2..], out _, "x4", CultureInfo.InvariantCulture);
                output.Write(escape);
                start = i + 1;
            }
        }

        AppendEncoded(output, span[start..]);
        output.Write("\""u8);
        writer.WriteRawValue(output.WrittenSpan, skipInputValidation: true);
    }

    /// <summary>
    /// GetString first; only when it throws (an unpaired surrogate escape, or invalid UTF-8) take the slow path. With
    /// <paramref name="replace"/> the unpaired surrogates become U+FFFD instead of being kept. Raw invalid UTF-8 is
    /// always a JsonException.
    /// </summary>
    public static string Read(ref Utf8JsonReader reader, bool replace = false)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected a string at byte {reader.TokenStartIndex}.");
        }

        try
        {
            return reader.GetString()!;
        }
        catch (InvalidOperationException e)
        {
            if (!reader.ValueIsEscaped || !HasSurrogateEscape(reader.ValueSpan))
            {
                throw new JsonException($"Invalid UTF-8 in the string at byte {reader.TokenStartIndex}.", e);
            }
        }

        var value = Unescape(reader.ValueSpan, reader.TokenStartIndex);
        return replace ? ReplaceLoneSurrogates(value) : value;
    }

    public static string ReplaceLoneSurrogates(string value)
    {
        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (char.IsHighSurrogate(chars[i]) && i + 1 < chars.Length && char.IsLowSurrogate(chars[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(chars[i]))
            {
                chars[i] = '�';
            }
        }

        return new string(chars);
    }

    private static bool IsWellFormed(ReadOnlySpan<char> span)
    {
        for (var i = 0; i < span.Length; i++)
        {
            if (char.IsHighSurrogate(span[i]) && i + 1 < span.Length && char.IsLowSurrogate(span[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(span[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static void AppendEncoded(ArrayBufferWriter<byte> output, ReadOnlySpan<char> run)
    {
        if (run.IsEmpty)
        {
            return;
        }

        output.Write(JsonEncodedText.Encode(run).EncodedUtf8Bytes);
    }

    /// <summary>True when the escaped value has a \u escape for a surrogate code unit (D800–DFFF).</summary>
    private static bool HasSurrogateEscape(ReadOnlySpan<byte> value)
    {
        for (var i = 0; i < value.Length - 1; i++)
        {
            if (value[i] != (byte)'\\')
            {
                continue;
            }

            if (value[i + 1] == (byte)'u' && i + 3 < value.Length && (value[i + 2] | 0x20) == (byte)'d' &&
                value[i + 3] is (byte)'8' or (byte)'9' or (>= (byte)'a' and <= (byte)'f') or (>= (byte)'A' and <= (byte)'F'))
            {
                return true;
            }

            i++; // skip the escaped character, so "\\u" (an escaped backslash) is not read as an escape
        }

        return false;
    }

    /// <summary>JSON unescape that keeps surrogate escapes as code units. Unescaped bytes must be valid UTF-8.</summary>
    private static string Unescape(ReadOnlySpan<byte> value, long offset)
    {
        var builder = new StringBuilder(value.Length);
        var strict = new UTF8Encoding(false, true);
        var run = 0;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != (byte)'\\')
            {
                continue;
            }

            try
            {
                builder.Append(strict.GetString(value[run..i]));
            }
            catch (DecoderFallbackException e)
            {
                throw new JsonException($"Invalid UTF-8 in the string at byte {offset}.", e);
            }

            var e2 = value[i + 1];
            switch (e2)
            {
                case (byte)'"': builder.Append('"'); break;
                case (byte)'\\': builder.Append('\\'); break;
                case (byte)'/': builder.Append('/'); break;
                case (byte)'b': builder.Append('\b'); break;
                case (byte)'f': builder.Append('\f'); break;
                case (byte)'n': builder.Append('\n'); break;
                case (byte)'r': builder.Append('\r'); break;
                case (byte)'t': builder.Append('\t'); break;
                case (byte)'u':
                    builder.Append((char)int.Parse(Encoding.ASCII.GetString(value.Slice(i + 2, 4)), NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture));
                    i += 4;
                    break;
                default: throw new JsonException($"Invalid escape in the string at byte {offset}.");
            }

            i++;
            run = i + 1;
        }

        try
        {
            builder.Append(strict.GetString(value[run..]));
        }
        catch (DecoderFallbackException e)
        {
            throw new JsonException($"Invalid UTF-8 in the string at byte {offset}.", e);
        }

        return builder.ToString();
    }
}
