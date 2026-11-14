using System.Buffers;
using System.Text.Json;

namespace WireProbe;

public sealed class Throwing
{
    public string Value => throw new InvalidOperationException("getter failed");
}

/// <summary>Item 6: exceptions while writing the response. Writer state afterwards, and a buffered dispatch prototype.</summary>
internal static class WriterErrorProbe
{
    private static readonly JsonWriterOptions s_options = new() { SkipValidation = true, MaxDepth = 64 };

    public static void Run()
    {
        Failures();
        Reuse();
        Dispatch();
        Stream();
        Partial();
    }

    private static readonly (string Name, Action<Utf8JsonWriter> Write)[] s_values =
    [
        ("ok", w => w.WriteStringValue("fine")),
        ("NaN", w => w.WriteNumberValue(double.NaN)),
        ("depth 65", w =>
        {
            for (var i = 0; i < 65; i++)
            {
                w.WriteStartArray();
            }
        }),
        ("lone surrogate", w => w.WriteStringValue("a\uD800")),
        ("user getter throws", w => w.WriteStringValue(new Throwing().Value)),
        ("invalid UTF-8 bytes", w => w.WriteStringValue([0xFF])),
    ];

    private static void Failures()
    {
        Console.WriteLine("### What each failure throws (SkipValidation = true, MaxDepth = 64, as the pooled writer)");
        foreach (var (name, write) in s_values)
        {
            Probe.Line(name, Probe.Write(write, s_options));
            Probe.Line(name + " (SkipValidation = false)", Probe.Write(write, new JsonWriterOptions { MaxDepth = 64 }));
        }
    }

    private static void Reuse()
    {
        Console.WriteLine("### Reusing a pooled writer after an exception");
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new Utf8JsonWriter(buffer, s_options);
        foreach (var (name, write) in s_values[1..])
        {
            buffer.ResetWrittenCount();
            writer.Reset(buffer);
            writer.WriteStartObject();
            writer.WritePropertyName("value");
            string state;
            try
            {
                write(writer);
                writer.Flush();
                state = "no exception";
            }
            catch (Exception e)
            {
                state = $"{e.GetType().Name}; BytesPending={writer.BytesPending}, BytesCommitted={writer.BytesCommitted}, " +
                        $"CurrentDepth={writer.CurrentDepth}, buffer={buffer.WrittenCount}";
            }

            buffer.ResetWrittenCount();
            writer.Reset(buffer);
            writer.WriteStartObject();
            writer.WriteBoolean("ok", true);
            writer.WriteEndObject();
            writer.Flush();
            Probe.Line(name, $"{state}; after Reset: {Probe.Show(buffer.WrittenSpan)}");
        }
    }

    /// <summary>Unary: the whole envelope is written into a pooled buffer before anything is sent.</summary>
    private static void Dispatch()
    {
        Console.WriteLine("### Buffered unary dispatch prototype (the command has already run)");
        var file = new ResultFile("writer-errors.json");
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new Utf8JsonWriter(buffer, s_options);
        foreach (var (name, write) in s_values)
        {
            buffer.ResetWrittenCount();
            writer.Reset(buffer);
            try
            {
                writer.WriteStartObject();
                writer.WriteNumber("id", 7);
                writer.WriteBoolean("ok", true);
                writer.WritePropertyName("value");
                write(writer);
                writer.WriteEndObject();
                writer.Flush();
                if (writer.CurrentDepth != 0)
                {
                    throw new InvalidOperationException("unbalanced");
                }
            }
            catch (Exception e)
            {
                // Nothing has left the process: discard the partial bytes and write the failure instead.
                buffer.ResetWrittenCount();
                writer.Reset(buffer);
                writer.WriteStartObject();
                writer.WriteNumber("id", 7);
                writer.WriteBoolean("ok", false);
                writer.WriteStartObject("failure");
                writer.WriteString("kind", "response-serialization");
                writer.WriteString("execution", "completed");
                writer.WriteString("detail", e.GetType().Name); // full message to the log only
                writer.WriteEndObject();
                writer.WriteEndObject();
                writer.Flush();
            }

            Probe.Line(name, Probe.Show(buffer.WrittenSpan));
            file.AddRaw(name.Replace(' ', '-'), buffer.WrittenSpan.ToArray());
        }

        file.Save();
    }

    /// <summary>Streams: each item is serialized into its own buffer before it is sent.</summary>
    private static void Stream()
    {
        Console.WriteLine("### Stream prototype: item 3 of 5 fails to serialize");
        var sent = new List<string>();
        var log = new List<string>();
        IEnumerable<double> Items()
        {
            try
            {
                yield return 1;
                yield return 2;
                yield return double.NaN;
                yield return 4;
                yield return 5;
            }
            finally
            {
                log.Add("enumerator finally ran");
            }
        }

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new Utf8JsonWriter(buffer, s_options);
        var delivered = 0;
        using (var e = Items().GetEnumerator())
        {
            while (e.MoveNext())
            {
                buffer.ResetWrittenCount();
                writer.Reset(buffer);
                try
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("id", 9);
                    writer.WriteString("type", "item");
                    writer.WriteNumber("value", e.Current);
                    writer.WriteEndObject();
                    writer.Flush();
                }
                catch (Exception ex)
                {
                    buffer.ResetWrittenCount();
                    writer.Reset(buffer);
                    writer.WriteStartObject();
                    writer.WriteNumber("id", 9);
                    writer.WriteString("type", "end");
                    writer.WriteBoolean("ok", false);
                    writer.WriteStartObject("failure");
                    writer.WriteString("kind", "response-serialization");
                    writer.WriteString("execution", "stopped-after-items");
                    writer.WriteNumber("itemsSent", delivered);
                    writer.WriteString("detail", ex.GetType().Name);
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                    writer.Flush();
                    sent.Add(Probe.Show(buffer.WrittenSpan));
                    break;
                }

                sent.Add(Probe.Show(buffer.WrittenSpan));
                delivered++;
            }
        }

        foreach (var message in sent)
        {
            Probe.Line("sent", message);
        }

        Probe.Line("producer", string.Join("; ", log));
    }

    /// <summary>What happens if a large unary response were written straight to the transport with periodic flushes.</summary>
    private static void Partial()
    {
        Console.WriteLine("### Unbuffered unary (writer flushes to the transport every 16 KB): failure at item 50,000");
        using var transport = new MemoryStream();
        using var writer = new Utf8JsonWriter(transport, s_options);
        try
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", 7);
            writer.WriteBoolean("ok", true);
            writer.WriteStartArray("value");
            for (var i = 0; i < 100_000; i++)
            {
                writer.WriteNumberValue(i == 50_000 ? double.NaN : i);
                if (writer.BytesPending > 16_384)
                {
                    writer.Flush();
                }
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }
        catch (Exception e)
        {
            Probe.Line("exception", e.GetType().Name);
        }

        var bytes = transport.ToArray();
        Probe.Line("bytes already sent", $"{bytes.Length:N0}, ends with `{Probe.Show(bytes.AsSpan(bytes.Length - 12))}`");
        File.WriteAllBytes(Path.Combine("results", "partial-unary.json"), bytes);
    }
}
