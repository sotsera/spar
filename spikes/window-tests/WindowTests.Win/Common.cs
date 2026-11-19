using System.Globalization;

namespace Spar.WindowTests;

/// <summary><c>--name value</c> pairs; a name followed by another <c>--name</c> (or nothing) is a flag.</summary>
internal sealed class Options
{
    private static readonly HashSet<string> s_alwaysValued = ["browser-args"];

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
            if (i + 1 < list.Count && (s_alwaysValued.Contains(name) || !list[i + 1].StartsWith("--", StringComparison.Ordinal)))
            {
                _values[name] = list[++i];
            }
            else
            {
                _values[name] = "";
            }
        }
    }

    public bool Has(string name) => _values.ContainsKey(name);

    public string? Get(string name) => _values.TryGetValue(name, out var value) ? value : null;

    public int Int(string name, int fallback) => Get(name) is { Length: > 0 } value ? int.Parse(value, CultureInfo.InvariantCulture) : fallback;
}

/// <summary>
/// Every user-data folder of this spike lives under <c>artifacts/webview2-profiles/window-tests/</c>; other output
/// under <c>artifacts/window-tests/</c>.
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

    public static string UdfRoot => Path.Combine(Repository, "artifacts", "webview2-profiles", "window-tests");

    public static string Output => Path.Combine(Repository, "artifacts", "window-tests");

    public static string Udf(string name)
    {
        if (name.Length == 0 || name.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '/')))
        {
            throw new ArgumentException("UDF names are ASCII letters, digits, '-', '_' and '/'.", nameof(name));
        }

        return Path.GetFullPath(Path.Combine(UdfRoot, name));
    }

    /// <summary>The command-line fragment that identifies this UDF's browser processes.</summary>
    public static string Marker(string udfPath) => Path.Combine(udfPath, "EBWebView");

    public static (int Files, long Bytes) Size(string path)
    {
        if (!Directory.Exists(path))
        {
            return (0, 0);
        }

        int files = 0;
        long bytes = 0;
        foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 }))
        {
            files++;
            bytes += file.Length;
        }

        return (files, bytes);
    }

    public static bool TryDelete(string path, out string error)
    {
        if (!path.StartsWith(UdfRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to delete outside the spike's profile root: " + path);
        }

        error = "";
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
            error = $"{exception.GetType().Name}:0x{exception.HResult:X8}";
            return false;
        }
    }
}

internal static class Log
{
    private static readonly Lock s_lock = new();

    public static void Write(string key, string text)
    {
        lock (s_lock)
        {
            Console.Out.WriteLine($"{key} t={DateTime.UtcNow:HH:mm:ss.fff} {text}");
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
