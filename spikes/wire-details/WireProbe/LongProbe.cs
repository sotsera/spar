using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.Text.Json;

namespace WireProbe;

/// <summary>Item 1: long and ulong beyond ±(2^53 − 1). What the C# side writes and accepts, and what each encoding costs.</summary>
internal static class LongProbe
{
    private const long MaxSafe = 9007199254740991; // 2^53 - 1

    public static void Run()
    {
        long[] longs = [1, MaxSafe, MaxSafe + 1, MaxSafe + 2, -(MaxSafe + 2), long.MaxValue, long.MinValue];
        ulong[] ulongs = [MaxSafe + 2, ulong.MaxValue];

        var file = new ResultFile("long.json");
        file.AddRaw("longAsNumber", Probe.WriteBytes(w =>
        {
            w.WriteStartArray();
            foreach (var v in longs)
            {
                w.WriteNumberValue(v);
            }

            w.WriteEndArray();
        }));
        file.AddRaw("longAsString", Probe.WriteBytes(w =>
        {
            w.WriteStartArray();
            foreach (var v in longs)
            {
                WriteInt64AsString(w, v);
            }

            w.WriteEndArray();
        }));
        file.AddRaw("ulongAsNumber", Probe.WriteBytes(w =>
        {
            w.WriteStartArray();
            foreach (var v in ulongs)
            {
                w.WriteNumberValue(v);
            }

            w.WriteEndArray();
        }));
        file.Save();
        Console.WriteLine("- wrote results/long.json (the values as numbers and as strings)");

        Console.WriteLine("Reading a number token with TryGetInt64 / TryGetUInt64:");
        foreach (var json in new[] { "9007199254740993", "9223372036854775807", "9223372036854775808", "-0", "1e3",
                     "1.0", "1E+21", "9007199254740992.0", "18446744073709551615" })
        {
            var asLong = Probe.Read(json, (ref r) => r.TryGetInt64(out var v) ? v.ToString(CultureInfo.InvariantCulture) : "false");
            var asULong = Probe.Read(json, (ref r) => r.TryGetUInt64(out var v) ? v.ToString(CultureInfo.InvariantCulture) : "false");
            Probe.Line(json, $"Int64 {asLong}; UInt64 {asULong}");
        }

        Console.WriteLine("Reading a string token: Utf8Parser.TryParse against the strict canonical check:");
        foreach (var text in new[] { "9007199254740993", "-9223372036854775808", "+1", "007", "-0", " 1", "1 ", "1e3",
                     "9223372036854775808", "" })
        {
            var utf8 = System.Text.Encoding.UTF8.GetBytes(text);
            var parser = Utf8Parser.TryParse(utf8, out long parsed, out var consumed)
                ? $"{parsed} (consumed {consumed}/{utf8.Length})"
                : "false";
            var bcl = long.TryParse(utf8, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var bclValue)
                ? bclValue.ToString(CultureInfo.InvariantCulture)
                : "false";
            var strict = TryParseCanonicalInt64(utf8, out var strictValue)
                ? strictValue.ToString(CultureInfo.InvariantCulture)
                : "reject";
            Probe.Line($"\"{text}\"", $"Utf8Parser {parser}; long.TryParse(AllowLeadingSign) {bcl}; canonical {strict}");
        }

        Benchmark();
    }

    /// <summary>Formats into a stack buffer and writes the digits as a JSON string (no escaping is ever needed).</summary>
    public static void WriteInt64AsString(Utf8JsonWriter writer, long value)
    {
        Span<byte> digits = stackalloc byte[20];
        value.TryFormat(digits, out var written, default, CultureInfo.InvariantCulture);
        writer.WriteStringValue(digits[..written]);
    }

    /// <summary>Accepts only -?(0|[1-9][0-9]*) within range: no sign plus, no leading zeros, no "-0", no spaces.</summary>
    public static bool TryParseCanonicalInt64(ReadOnlySpan<byte> utf8, out long value)
    {
        value = 0;
        var digits = utf8.Length > 0 && utf8[0] == (byte)'-' ? utf8[1..] : utf8;
        if (digits.Length == 0 || digits.Length > 19 || (digits[0] == (byte)'0' && (digits.Length > 1 || digits != utf8)))
        {
            return false;
        }

        foreach (var b in digits)
        {
            if (b is < (byte)'0' or > (byte)'9')
            {
                return false;
            }
        }

        return Utf8Parser.TryParse(utf8, out value, out var consumed) && consumed == utf8.Length;
    }

    private static void Benchmark()
    {
        const int count = 1_000_000;
        var random = new Random(53);
        var values = new long[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = random.NextInt64(long.MinValue, long.MaxValue);
        }

        var buffer = new ArrayBufferWriter<byte>(32 * count);
        var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { SkipValidation = true, MaxDepth = 64 });

        void WriteAll(bool asString)
        {
            buffer.ResetWrittenCount();
            writer.Reset(buffer);
            writer.WriteStartArray();
            foreach (var v in values)
            {
                if (asString)
                {
                    WriteInt64AsString(writer, v);
                }
                else
                {
                    writer.WriteNumberValue(v);
                }
            }

            writer.WriteEndArray();
            writer.Flush();
        }

        var writeNumber = Probe.Time(() => WriteAll(false));
        var numberBytes = buffer.WrittenSpan.ToArray();
        var writeString = Probe.Time(() => WriteAll(true));
        var stringBytes = buffer.WrittenSpan.ToArray();

        long sum = 0;
        var readNumber = Probe.Time(() =>
        {
            var reader = new Utf8JsonReader(numberBytes, Probe.ReaderOptions);
            reader.Read();
            while (reader.Read() && reader.TokenType == JsonTokenType.Number)
            {
                if (!reader.TryGetInt64(out var v))
                {
                    throw new JsonException("not an Int64");
                }

                sum += v;
            }
        });
        var readString = Probe.Time(() =>
        {
            var reader = new Utf8JsonReader(stringBytes, Probe.ReaderOptions);
            reader.Read();
            while (reader.Read() && reader.TokenType == JsonTokenType.String)
            {
                if (reader.ValueIsEscaped || !TryParseCanonicalInt64(reader.ValueSpan, out var v))
                {
                    throw new JsonException("not a canonical Int64 string");
                }

                sum += v;
            }
        });

        Console.WriteLine($"C# cost, {count:N0} random full-range longs (min of 5 runs, Release, JIT):");
        Probe.Line("write as number", $"{writeNumber} ms, {numberBytes.Length:N0} bytes");
        Probe.Line("write as string", $"{writeString} ms, {stringBytes.Length:N0} bytes");
        Probe.Line("read number (TryGetInt64)", $"{readNumber} ms");
        Probe.Line("read string (canonical check + Utf8Parser)", $"{readString} ms (checksum {sum & 0xFF})");
    }
}
