using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SandboxRunner;

/// <summary>Steps of one session as JSON lines: time since the session began, the step's duration and details.</summary>
internal sealed class Timeline(string path) : IDisposable
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly StreamWriter _writer = new(path, append: false, new UTF8Encoding(false)) { AutoFlush = true };
    private readonly Lock _gate = new();

    public TimeSpan Now => _clock.Elapsed;

    public void Write(string step, TimeSpan? duration, params (string Name, object? Value)[] details)
    {
        var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteNumber("t_ms", Math.Round(_clock.Elapsed.TotalMilliseconds));
            json.WriteString("step", step);
            if (duration is { } d)
            {
                json.WriteNumber("ms", Math.Round(d.TotalMilliseconds));
            }

            foreach (var (name, value) in details)
            {
                switch (value)
                {
                    case null:
                        json.WriteNull(name);
                        break;
                    case int i:
                        json.WriteNumber(name, i);
                        break;
                    case long l:
                        json.WriteNumber(name, l);
                        break;
                    case double x:
                        json.WriteNumber(name, Math.Round(x, 1));
                        break;
                    case bool b:
                        json.WriteBoolean(name, b);
                        break;
                    default:
                        json.WriteString(name, Convert.ToString(value, CultureInfo.InvariantCulture));
                        break;
                }
            }

            json.WriteEndObject();
        }

        var line = Encoding.UTF8.GetString(buffer.ToArray());
        lock (_gate)
        {
            _writer.WriteLine(line);
            Console.WriteLine(line);
        }
    }

    public void Dispose() => _writer.Dispose();
}
