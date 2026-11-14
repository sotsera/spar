namespace Csp.Shared;

/// <summary>Command-line options shared by the hosts. Relative paths resolve against the current directory.</summary>
public sealed class HostOptions
{
    private readonly string[] _args;

    public HostOptions(string[] args)
    {
        _args = args;
        var roots = new List<string>();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--root")
            {
                roots.Add(Path.GetFullPath(args[i + 1]));
            }
        }

        Roots = [.. roots];
        if (Roots.Length == 0)
        {
            throw new ArgumentException("Pass at least one --root <folder> (the frontend dist and the probe pages).");
        }

        ProfileName = Value("--profile") ?? "base";
        Page = Value("--page") ?? "/";
        Label = Value("--label") ?? ProfileName;
        ExternalPort = int.Parse(Value("--ext-port") ?? ExternalServer.DefaultPort.ToString(System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture);
        TimeoutSeconds = int.Parse(Value("--timeout") ?? "40", System.Globalization.CultureInfo.InvariantCulture);
    }

    public string[] Roots { get; }

    public string ProfileName { get; }

    public string Page { get; }

    public string Label { get; }

    public int ExternalPort { get; }

    public int TimeoutSeconds { get; }

    public string? DevUrl => Value("--dev-url");

    public string? StartUrl => Value("--start-url");

    public string? Tamper => Value("--tamper");

    public string? DataDirectory => Value("--data-dir");

    public bool NavigationLock => !Has("--no-navlock");

    public bool Inject => !Has("--no-inject");

    public bool RealOpen => Has("--real-open");

    public bool Offscreen => Has("--offscreen");

    public bool Has(string flag) => _args.Contains(flag);

    public List<string> Values(string name)
    {
        var values = new List<string>();
        for (var i = 0; i < _args.Length - 1; i++)
        {
            if (_args[i] == name)
            {
                values.Add(_args[i + 1]);
            }
        }

        return values;
    }

    public string? Value(string name)
    {
        var index = Array.IndexOf(_args, name);
        return index >= 0 && index + 1 < _args.Length ? _args[index + 1] : null;
    }

    /// <summary>The CSP hash of the SRI import map, when a served root has one (written by the Vite plugin).</summary>
    public string? ImportMapHash()
    {
        foreach (var root in Roots)
        {
            var file = Path.Combine(root, "spar-sri.json");
            if (File.Exists(file))
            {
                using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(file));
                return document.RootElement.TryGetProperty("importMapHash", out var hash) ? hash.GetString() : null;
            }
        }

        return null;
    }

    public string ProfilesFile()
    {
        foreach (var root in Roots)
        {
            var file = Path.Combine(root, "profiles.json");
            if (File.Exists(file))
            {
                return file;
            }
        }

        throw new FileNotFoundException("No profiles.json in the served roots.");
    }

    /// <summary>Spike rule: browser data lives under the repository's artifacts/webview2-profiles/, never in the user profile.</summary>
    public static string ProfileDirectory(string name)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")))
            {
                return Path.Combine(directory.FullName, "artifacts", "webview2-profiles", name);
            }
        }

        throw new InvalidOperationException("Run the spike from inside the repository (no global.json above the executable).");
    }
}
