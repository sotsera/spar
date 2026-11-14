using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace WireProbe;

internal delegate T ReadFn<T>(ref Utf8JsonReader reader);

internal static class Probe
{
    public static readonly JsonReaderOptions ReaderOptions = new() { MaxDepth = 64 };

    /// <summary>Runs a write against a fresh writer and shows the bytes, or the exception.</summary>
    public static string Write(Action<Utf8JsonWriter> write, JsonWriterOptions options = default)
    {
        var buffer = new ArrayBufferWriter<byte>();
        try
        {
            using var writer = new Utf8JsonWriter(buffer, options);
            write(writer);
            writer.Flush();
            return Show(buffer.WrittenSpan);
        }
        catch (Exception e)
        {
            return Fail(e);
        }
    }

    public static byte[] WriteBytes(Action<Utf8JsonWriter> write, JsonWriterOptions options = default)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer, options);
        write(writer);
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Reads the first token of <paramref name="utf8"/> and applies <paramref name="read"/>.</summary>
    public static string Read<T>(byte[] utf8, ReadFn<T> read)
    {
        try
        {
            var reader = new Utf8JsonReader(utf8, ReaderOptions);
            reader.Read();
            return Describe(read(ref reader));
        }
        catch (Exception e)
        {
            return Fail(e);
        }
    }

    public static string Read<T>(string json, ReadFn<T> read) => Read(Encoding.UTF8.GetBytes(json), read);

    public static string Try<T>(Func<T> action)
    {
        try
        {
            return Describe(action());
        }
        catch (Exception e)
        {
            return Fail(e);
        }
    }

    public static string Fail(Exception e)
    {
        var message = e.Message.Replace("\r", "", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
        return $"THROWS {e.GetType().Name}: {message}";
    }

    /// <summary>Printable ASCII as is; every other byte as \xHH, so replacement characters and CESU bytes show.</summary>
    public static string Show(ReadOnlySpan<byte> bytes)
    {
        var builder = new StringBuilder(bytes.Length);
        foreach (var b in bytes)
        {
            if (b is >= 0x20 and < 0x7F)
            {
                builder.Append((char)b);
            }
            else
            {
                builder.Append(CultureInfo.InvariantCulture, $"\\x{b:X2}");
            }
        }

        return builder.ToString();
    }

    /// <summary>A .NET string as code units: printable ASCII as is, everything else as {U+XXXX}.</summary>
    public static string Units(string? value)
    {
        if (value is null)
        {
            return "null";
        }

        var builder = new StringBuilder();
        builder.Append('"');
        foreach (var c in value)
        {
            if (c is >= ' ' and < (char)0x7F)
            {
                builder.Append(c);
            }
            else
            {
                builder.Append(CultureInfo.InvariantCulture, $"{{U+{(int)c:X4}}}");
            }
        }

        builder.Append('"');
        return builder.ToString();
    }

    public static string Describe(object? value) => value switch
    {
        null => "null",
        string s => Units(s),
        byte[] bytes => "bytes[" + Convert.ToHexString(bytes) + "]",
        DateTime d => $"{d.ToString("O", CultureInfo.InvariantCulture)} Kind={d.Kind} Ticks={d.Ticks}",
        DateTimeOffset d => $"{d.ToString("O", CultureInfo.InvariantCulture)} UtcTicks={d.UtcTicks}",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    public static void Line(string label, string result) => Console.WriteLine($"- {label}: `{result}`");

    /// <summary>Minimum of <paramref name="rounds"/> timed runs, in milliseconds, after one warm-up.</summary>
    public static double Time(Action action, int rounds = 5)
    {
        action();
        var best = double.MaxValue;
        for (var i = 0; i < rounds; i++)
        {
            var watch = Stopwatch.StartNew();
            action();
            best = Math.Min(best, watch.Elapsed.TotalMilliseconds);
        }

        return Math.Round(best, 1);
    }
}

/// <summary>Collects (case, value) pairs and writes them as one JSON object for the Node scripts.</summary>
internal sealed class ResultFile(string name)
{
    private readonly List<(string Key, byte[] Json)> _entries = [];

    /// <summary>Adds a raw JSON value (already valid JSON text).</summary>
    public void AddRaw(string key, byte[] json) => _entries.Add((key, json));

    public void AddString(string key, string value) =>
        _entries.Add((key, Probe.WriteBytes(w => w.WriteStringValue(value))));

    public void Save()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            foreach (var (key, json) in _entries)
            {
                writer.WritePropertyName(key);
                writer.WriteRawValue(json, skipInputValidation: true);
            }

            writer.WriteEndObject();
        }

        File.WriteAllBytes(Path.Combine("results", name), buffer.WrittenSpan.ToArray());
    }
}
