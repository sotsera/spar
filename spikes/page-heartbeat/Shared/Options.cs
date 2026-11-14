using System.Diagnostics;
using System.Globalization;

namespace Spar.PageHeartbeat;

/// <summary><c>--name value</c> pairs; a name followed by another <c>--name</c> (or nothing) is a flag.</summary>
internal sealed class Options
{
    private readonly Dictionary<string, string> _values = [];

    public Options(IEnumerable<string> args)
    {
        var list = args.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            if (!list[i].StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var name = list[i][2..];
            _values[name] = i + 1 < list.Count && !list[i + 1].StartsWith("--", StringComparison.Ordinal) ? list[++i] : "";
        }
    }

    public string Text => string.Join(' ', _values.Select(pair => pair.Value.Length == 0 ? "--" + pair.Key : $"--{pair.Key} {pair.Value}"));

    public bool Has(string name) => _values.ContainsKey(name);

    public string? Get(string name) => _values.TryGetValue(name, out var value) ? value : null;

    public string Get(string name, string fallback) => Get(name) is { Length: > 0 } value ? value : fallback;

    public int Int(string name, int fallback) => Get(name) is { Length: > 0 } value ? int.Parse(value, CultureInfo.InvariantCulture) : fallback;
}

/// <summary>
/// Log lines are <c>T KEY k=v ...</c>: T is milliseconds since process start (monotonic). They go to the
/// <c>--log</c> file when given (Windows Sandbox relays no stdout) and to stdout.
/// </summary>
internal static class Log
{
    private static readonly long s_start = Stopwatch.GetTimestamp();
    private static readonly Lock s_lock = new();
    private static StreamWriter? s_file;

    public static double Now => Stopwatch.GetElapsedTime(s_start).TotalMilliseconds;

    public static void Open(string? path)
    {
        if (path is { Length: > 0 })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            s_file = new StreamWriter(path, append: false) { AutoFlush = true };
        }
    }

    public static void Line(string key, string text)
    {
        var line = string.Create(CultureInfo.InvariantCulture, $"{Now:F1} {key} {text}");
        lock (s_lock)
        {
            s_file?.WriteLine(line);
            try
            {
                Console.Out.WriteLine(line);
                Console.Out.Flush();
            }
            catch (IOException)
            {
                // No console (GUI subsystem without inherited handles): the file is enough.
            }
        }
    }

    public static double Ms(long started) => Stopwatch.GetElapsedTime(started).TotalMilliseconds;

    public static string F(double value) => value.ToString("F1", CultureInfo.InvariantCulture);
}

/// <summary>Median, range and p99 of a set of timings.</summary>
internal static class Stats
{
    public static string Of(List<double> values, string unit = "ms")
    {
        if (values.Count == 0)
        {
            return "n=0";
        }

        var sorted = values.Order().ToArray();
        double At(double q) => sorted[Math.Min(sorted.Length - 1, (int)Math.Round(q * (sorted.Length - 1)))];
        return string.Create(CultureInfo.InvariantCulture,
            $"n={sorted.Length} median={At(0.5):F2}{unit} p99={At(0.99):F2}{unit} min={sorted[0]:F2}{unit} max={sorted[^1]:F2}{unit}");
    }
}

/// <summary>Timed actions from <c>--actions "ms:verb[:arg],..."</c>, relative to the first page ready.</summary>
internal sealed record TimedAction(int At, string Verb, string Arg)
{
    public static List<TimedAction> Parse(string? text)
    {
        var list = new List<TimedAction>();
        foreach (var item in (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = item.Split(':', 3);
            list.Add(new TimedAction(int.Parse(parts[0], CultureInfo.InvariantCulture), parts[1], parts.Length > 2 ? parts[2] : ""));
        }

        return [.. list.OrderBy(a => a.At)];
    }
}
