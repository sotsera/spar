using System.Text.Json;

namespace Csp.Shared;

/// <summary>One header profile from <c>pages/profiles.json</c>, with its placeholders resolved.</summary>
public sealed class HeaderProfile
{
    private HeaderProfile(string name, string? csp, string? meta, List<KeyValuePair<string, string>> headers)
    {
        Name = name;
        Csp = csp;
        Meta = meta;
        Headers = headers;
    }

    public string Name { get; }

    /// <summary>The Content-Security-Policy header value, or null.</summary>
    public string? Csp { get; }

    /// <summary>The policy to put in a <c>&lt;meta http-equiv&gt;</c> at the top of every HTML response, or null.</summary>
    public string? Meta { get; }

    /// <summary>Other headers sent on every response.</summary>
    public List<KeyValuePair<string, string>> Headers { get; }

    /// <summary>Loads the profile named on the command line, minus any header named by --drop-header.</summary>
    public static HeaderProfile Load(HostOptions options, string origin, string external)
    {
        var profile = Load(options.ProfilesFile(), options.ProfileName, origin, external, options.ImportMapHash());
        var dropped = options.Values("--drop-header");
        profile.Headers.RemoveAll(h => dropped.Contains(h.Key, StringComparer.OrdinalIgnoreCase));
        return profile;
    }

    public static HeaderProfile Load(string profilesFile, string name, string origin, string external, string? importMapHash)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(profilesFile));
        var root = document.RootElement;
        var basePolicy = root.GetProperty("base").GetString()!;
        if (!root.GetProperty("profiles").TryGetProperty(name, out var profile))
        {
            throw new ArgumentException($"Unknown profile '{name}' in {profilesFile}.");
        }

        string Resolve(string value) => value
            .Replace("{BASE}", basePolicy, StringComparison.Ordinal)
            .Replace("{ORIGIN}", origin, StringComparison.Ordinal)
            .Replace("{EXT}", external, StringComparison.Ordinal)
            .Replace("{IMPORTMAP}", importMapHash is null ? "" : $"'{importMapHash}'", StringComparison.Ordinal);

        var headers = new List<KeyValuePair<string, string>>();
        if (profile.TryGetProperty("other", out var other) && other.GetBoolean())
        {
            foreach (var header in root.GetProperty("other").EnumerateObject())
            {
                headers.Add(new(header.Name, Resolve(header.Value.GetString()!)));
            }
        }

        if (profile.TryGetProperty("headers", out var extra))
        {
            foreach (var header in extra.EnumerateObject())
            {
                headers.Add(new(header.Name, Resolve(header.Value.GetString()!)));
            }
        }

        var csp = profile.TryGetProperty("csp", out var c) ? Resolve(c.GetString()!) : null;
        var meta = profile.TryGetProperty("meta", out var m) ? Resolve(m.GetString()!) : null;
        return new HeaderProfile(name, csp, meta, headers);
    }
}
