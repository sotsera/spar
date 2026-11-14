using System.Globalization;

namespace Spar.WebViewProfiles;

/// <summary><c>--name value</c> pairs; a name followed by another <c>--name</c> (or nothing) is a flag.</summary>
internal sealed class Options
{
    // These take the next argument as their value even when it starts with "--" (browser arguments, host arguments).
    private static readonly HashSet<string> s_alwaysValued = ["browser-args", "alt-args", "host", "page-extra"];

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

/// <summary>Every user-data folder of this spike lives under <c>artifacts/webview2-profiles/webview-profiles/</c>.</summary>
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

    public static string UdfRoot => Path.Combine(Repository, "artifacts", "webview2-profiles", "webview-profiles");

    public static string Output => Path.Combine(Repository, "artifacts", "webview-profiles");

    public static string Udf(string name)
    {
        if (name.Length == 0 || name.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
        {
            throw new ArgumentException("UDF names are ASCII letters, digits, '-' and '_'.", nameof(name));
        }

        return Path.Combine(UdfRoot, name);
    }

    /// <summary>The command-line fragment that identifies this UDF's browser processes.</summary>
    public static string Marker(string name) => Path.Combine(Udf(name), "EBWebView");

    public static (int Files, long Bytes) Size(string path)
    {
        if (!Directory.Exists(path))
        {
            return (0, 0);
        }

        int files = 0;
        long bytes = 0;
        try
        {
            foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
            {
                files++;
                bytes += file.Length;
            }
        }
        catch (IOException)
        {
        }

        return (files, bytes);
    }

    /// <summary>The largest files under <paramref name="path"/>, as "relative:kB" entries.</summary>
    public static string Listing(string path, int top)
    {
        if (!Directory.Exists(path))
        {
            return "missing";
        }

        var root = new DirectoryInfo(path);
        var files = root.EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
            .OrderByDescending(f => f.Length)
            .Take(top)
            .Select(f => $"{Path.GetRelativePath(path, f.FullName)}:{f.Length / 1024}");
        return string.Join(' ', files);
    }

    public static bool TryDelete(string path)
    {
        if (!path.StartsWith(UdfRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to delete outside the spike's profile root: " + path);
        }

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
            Console.Error.WriteLine($"Delete failed for {path}: {exception.Message}");
            return false;
        }
    }
}
