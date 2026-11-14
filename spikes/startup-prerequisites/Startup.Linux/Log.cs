using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Startup.Linux;

// JSON lines with milliseconds since Main ("t"); the first line also records the process age at Main from
// /proc/self/stat (clock ticks, so 10 ms resolution).
internal sealed class Log : IDisposable
{
    private readonly long _start = Stopwatch.GetTimestamp();
    private readonly StreamWriter? _file;

    public Log(string? path)
    {
        if (path is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            _file = new StreamWriter(path, append: true, new UTF8Encoding(false));
        }
    }

    public double Now => Stopwatch.GetElapsedTime(_start).TotalMilliseconds;

    public static double ProcessAgeMs()
    {
        try
        {
            var stat = File.ReadAllText("/proc/self/stat");
            var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
            var startTicks = double.Parse(fields[19], CultureInfo.InvariantCulture); // field 22 overall
            var uptime = double.Parse(File.ReadAllText("/proc/uptime").Split(' ')[0], CultureInfo.InvariantCulture);
            return Math.Round((uptime * 1000) - (startTicks * 10), 1); // USER_HZ = 100 on Linux x64
        }
        catch (IOException)
        {
            return -1;
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
}
