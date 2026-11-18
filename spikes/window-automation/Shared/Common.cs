using System.Diagnostics;
using System.Globalization;

namespace Spar.WindowAutomation;

/// <summary>
/// Every user-data folder of this spike lives under <c>artifacts/webview2-profiles/window-automation/</c>; other
/// output under <c>artifacts/window-automation/</c>.
/// </summary>
internal static class Paths
{
    private static readonly Lazy<string> s_repository = new(() =>
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")) && Directory.Exists(Path.Combine(directory.FullName, "spikes")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Run from inside the repository: profiles go under artifacts/webview2-profiles/.");
    });

    public static string Repository => s_repository.Value;

    public static string ProfileRoot => Path.Combine(Repository, "artifacts", "webview2-profiles", "window-automation");

    public static string Output => Path.Combine(Repository, "artifacts", "window-automation");

    public static string Page => Path.Combine(Repository, "spikes", "window-automation", "page", "obj", "dist") + Path.DirectorySeparatorChar;

    /// <summary>A new root for this test process.</summary>
    public static string NewRoot(string kind) => Path.Combine(ProfileRoot, $"{kind}-{Environment.ProcessId}-{DateTime.UtcNow:HHmmssfff}");

    /// <summary>Deletes a root of this spike once its browser is gone (bounded retries).</summary>
    public static async Task<bool> DeleteRootAsync(string path)
    {
        if (!path.StartsWith(ProfileRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to delete outside the spike's profile root: " + path);
        }

        for (var attempt = 0; attempt < 50; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }

                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(100);
            }
        }

        return false;
    }
}

internal static class Log
{
    private static readonly Lock s_lock = new();

    public static void Write(string key, string text)
    {
        lock (s_lock)
        {
            Console.Out.WriteLine($"{key} {text}");
            Console.Out.Flush();
        }
    }
}

internal static class Stats
{
    public static string Summary(IReadOnlyCollection<double> values)
    {
        if (values.Count == 0)
        {
            return "n=0";
        }

        var sorted = values.OrderBy(v => v).ToArray();
        return string.Create(CultureInfo.InvariantCulture,
            $"n={sorted.Length} min={sorted[0]:F1} p50={Percentile(sorted, 0.50):F1} p95={Percentile(sorted, 0.95):F1} max={sorted[^1]:F1}");
    }

    // Nearest-rank percentile.
    public static double Percentile(double[] sorted, double fraction)
    {
        var rank = (int)Math.Ceiling(fraction * sorted.Length);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Length - 1)];
    }
}

/// <summary>Per-step timings of one journey, collected over many runs.</summary>
internal sealed class Timings
{
    private readonly Dictionary<string, List<double>> _steps = [];
    private readonly List<string> _order = [];

    public async Task<T> TimeAsync<T>(string step, Func<Task<T>> action)
    {
        var started = Stopwatch.GetTimestamp();
        var result = await action();
        Add(step, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return result;
    }

    public async Task TimeAsync(string step, Func<Task> action)
    {
        var started = Stopwatch.GetTimestamp();
        await action();
        Add(step, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    public void Add(string step, double milliseconds)
    {
        if (!_steps.TryGetValue(step, out var list))
        {
            _steps[step] = list = [];
            _order.Add(step);
        }

        list.Add(milliseconds);
    }

    public void Report(string prefix)
    {
        foreach (var step in _order)
        {
            Log.Write(prefix, $"{step,-28} {Stats.Summary(_steps[step])}");
        }
    }
}

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

    public bool Has(string name) => _values.ContainsKey(name);

    public string? Get(string name) => _values.TryGetValue(name, out var value) ? value : null;

    public int Int(string name, int fallback) => Get(name) is { Length: > 0 } value ? int.Parse(value, CultureInfo.InvariantCulture) : fallback;
}
