using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Titlebar.Desktop;

// JSON lines with milliseconds since the process started, written to <out>/<label>.jsonl and to stdout.
internal sealed class Log : IDisposable
{
    private readonly long _start = Stopwatch.GetTimestamp();
    private readonly StreamWriter _file;
    private readonly Lock _lock = new();

    public Log(string folder, string label)
    {
        Directory.CreateDirectory(folder);
        Folder = folder;
        Label = label;
        _file = new StreamWriter(Path.Combine(folder, label + ".jsonl"), append: false, new UTF8Encoding(false));
    }

    public string Folder { get; }
    public string Label { get; }
    public double Now => Stopwatch.GetElapsedTime(_start).TotalMilliseconds;

    public void Mark(string evt, params (string Key, object? Value)[] fields)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteNumber("t", Math.Round(Now, 2));
            w.WriteString("evt", evt);
            foreach (var (key, value) in fields)
            {
                switch (value)
                {
                    case null: w.WriteNull(key); break;
                    case string s: w.WriteString(key, s); break;
                    case bool b: w.WriteBoolean(key, b); break;
                    case int i: w.WriteNumber(key, i); break;
                    case uint u: w.WriteNumber(key, u); break;
                    case long l: w.WriteNumber(key, l); break;
                    case double d: w.WriteNumber(key, Math.Round(d, 3)); break;
                    case RawJson r: w.WritePropertyName(key); w.WriteRawValue(r.Json); break;
                    default: w.WriteString(key, value.ToString()); break;
                }
            }

            w.WriteEndObject();
        }

        var line = Encoding.UTF8.GetString(buffer.ToArray());
        lock (_lock)
        {
            _file.WriteLine(line);
            _file.Flush();
            Console.WriteLine(line);
        }
    }

    public void Dispose() => _file.Dispose();
}

internal sealed record RawJson(string Json);
