namespace Spar;

/// <summary>
/// Stand-in for <c>spar.ContentSecurity(csp => …)</c> (plan section 3b; decision "Page security's public shape"):
/// the directive methods take hosts and schemes, several at once and over several calls, and the risky widenings are
/// named methods. It records what the configuration declares.
/// </summary>
public sealed class CspBuilder
{
    private readonly Dictionary<string, List<string>> _sources = new(StringComparer.Ordinal);
    private readonly List<string> _options = [];

    internal CspBuilder()
    {
    }

    public CspBuilder FontSrc(params string[] sources) => Add("font-src", sources);

    public CspBuilder ImgSrc(params string[] sources) => Add("img-src", sources);

    public CspBuilder ConnectSrc(params string[] sources) => Add("connect-src", sources);

    public CspBuilder MediaSrc(params string[] sources) => Add("media-src", sources);

    public CspBuilder ScriptSrc(params string[] sources) => Add("script-src", sources);

    public CspBuilder StyleSrc(params string[] sources) => Add("style-src", sources);

    public CspBuilder FrameSrc(params string[] sources) => Add("frame-src", sources);

    public CspBuilder AllowInlineStyles() => Option("AllowInlineStyles");

    public CspBuilder AllowEval() => Option("AllowEval");

    public CspBuilder AllowWebAssembly() => Option("AllowWebAssembly");

    public CspBuilder StyleNonce() => Option("StyleNonce");

    public CspBuilder WithoutTrustedTypes() => Option("WithoutTrustedTypes");

    public CspBuilder WithoutIntegrity() => Option("WithoutIntegrity");

    public CspBuilder Reporting(CspReports listener = CspReports.Always, CspReports endpoint = CspReports.Always) =>
        Option($"Reporting({listener}, {endpoint})");

    /// <summary>Each directive once, with its sources in the order first declared.</summary>
    internal string Describe() =>
        string.Join("; ", _sources.Select(directive => $"{directive.Key} {string.Join(' ', directive.Value)}").Concat(_options));

    private CspBuilder Add(string directive, string[] sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (!_sources.TryGetValue(directive, out var list))
        {
            list = [];
            _sources.Add(directive, list);
        }

        foreach (var source in sources.Where(source => !list.Contains(source)))
        {
            list.Add(source);
        }

        return this;
    }

    private CspBuilder Option(string option)
    {
        _options.Add(option);
        return this;
    }
}

/// <summary>When a channel reports Content Security Policy violations.</summary>
public enum CspReports
{
    Always,
    DevelopmentOnly,
    Off,
}

public sealed class PermissionsBuilder
{
    private readonly List<Permission> _allowed = [];

    internal PermissionsBuilder()
    {
    }

    internal IReadOnlyList<Permission> Allowed => _allowed;

    public PermissionsBuilder Allow(Permission permission)
    {
        _allowed.Add(permission);
        return this;
    }
}

/// <summary>Powerful browser features, denied unless allowed: those both Permissions-Policy and the WebView know.</summary>
public enum Permission
{
    Camera,
    Microphone,
    Geolocation,
    ClipboardRead,
    Sensors,
    LocalFonts,
    Midi,
    WindowManagement,
    Autoplay,
}
