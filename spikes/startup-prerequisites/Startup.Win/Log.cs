using System.Text;
using System.Text.Json;

namespace Startup.Win;

// JSON lines. "t" is milliseconds since the kernel created this process (GetProcessTimes), so it includes the loader
// and runtime start-up before Main.
internal sealed class Log : IDisposable
{
    private readonly long _created;
    private readonly StreamWriter? _file;

    public Log(string? path)
    {
        Native.GetProcessTimes(Native.GetCurrentProcess(), out _created, out _, out _, out _);
        if (path is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            _file = new StreamWriter(path, append: true, new UTF8Encoding(false));
        }
    }

    public double Now
    {
        get
        {
            Native.GetSystemTimePreciseAsFileTime(out var now);
            return (now - _created) / 10_000.0;
        }
    }

    public void Mark(string evt, params (string Key, object? Value)[] fields)
    {
        if (_file is null)
        {
            return;
        }

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
                    default: w.WriteString(key, value.ToString()); break;
                }
            }

            w.WriteEndObject();
        }

        _file.WriteLine(Encoding.UTF8.GetString(buffer.ToArray()));
        _file.Flush();
    }

    public void Dispose() => _file?.Dispose();

    public static string Hr(int hr) => $"0x{hr:X8}";
}
