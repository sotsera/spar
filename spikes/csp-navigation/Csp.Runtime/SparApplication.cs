namespace Spar;

/// <summary>Powerful features denied by default through Permissions-Policy and the hosts' permission events.</summary>
public enum Permission
{
    Camera,
    Microphone,
    Geolocation,
    Usb,
    Serial,
    Hid,
    Bluetooth,
    Payment,
    DisplayCapture,
}

/// <summary>Stub: the Spar application, configured in one lambda the build can read without running it.</summary>
public sealed class SparApplication
{
    private SparApplication(SecurityPolicy policy)
    {
        Policy = policy;
    }

    /// <summary>The policy computed at run time from the same declaration (compared with the generated one in Debug).</summary>
    public SecurityPolicy Policy { get; }

    public static SparApplication Create(string[] args, Action<SparBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new SparBuilder();
        configure(builder);
        return new SparApplication(builder.Security.Build());
    }
}

public sealed class SparBuilder
{
    internal ContentSecurityBuilder Security { get; } = new();

    /// <summary>Adds to the strict base policy. Values must be constants and the call must sit directly in Create's lambda.</summary>
    public SparBuilder ContentSecurity(Action<ContentSecurityBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(Security);
        return this;
    }
}

public sealed record SecurityPolicy(string ContentSecurityPolicy, string PermissionsPolicy);

public sealed class ContentSecurityBuilder
{
    private readonly Dictionary<string, List<string>> _added = [];
    private readonly HashSet<Permission> _allowed = [];
    private bool _trustedTypes;

    public ContentSecurityBuilder ScriptSrc(params string[] sources) => Add("script-src", sources);

    public ContentSecurityBuilder StyleSrc(params string[] sources) => Add("style-src", sources);

    public ContentSecurityBuilder ImgSrc(params string[] sources) => Add("img-src", sources);

    public ContentSecurityBuilder FontSrc(params string[] sources) => Add("font-src", sources);

    public ContentSecurityBuilder ConnectSrc(params string[] sources) => Add("connect-src", sources);

    public ContentSecurityBuilder FrameSrc(params string[] sources) => Add("frame-src", sources);

    public ContentSecurityBuilder MediaSrc(params string[] sources) => Add("media-src", sources);

    public ContentSecurityBuilder WorkerSrc(params string[] sources) => Add("worker-src", sources);

    public ContentSecurityBuilder TrustedTypes()
    {
        _trustedTypes = true;
        return this;
    }

    /// <summary>Opts one feature back in: Permissions-Policy <c>feature=(self)</c>, and the host grants its permission event.</summary>
    public ContentSecurityBuilder Allow(Permission permission)
    {
        _allowed.Add(permission);
        return this;
    }

    private ContentSecurityBuilder Add(string directive, string[] sources)
    {
        if (!_added.TryGetValue(directive, out var list))
        {
            _added[directive] = list = [];
        }

        list.AddRange(sources);
        return this;
    }

    internal SecurityPolicy Build() => new(
        PolicyText.Csp(_added.Select(p => (p.Key, (IReadOnlyList<string>)p.Value)), _trustedTypes),
        PolicyText.Permissions(_allowed));
}

/// <summary>The policy text rules. The generator has its own copy of these rules (netstandard2.0); a test compares them.</summary>
public static class PolicyText
{
    public static readonly string[] BaseOrder =
    [
        "default-src", "script-src", "style-src", "img-src", "font-src", "connect-src", "worker-src", "manifest-src",
        "frame-src", "media-src", "base-uri", "form-action", "frame-ancestors", "object-src",
    ];

    private static readonly Dictionary<string, string> s_base = new()
    {
        ["default-src"] = "'none'",
        ["script-src"] = "'self'",
        ["style-src"] = "'self'",
        ["img-src"] = "'self'",
        ["font-src"] = "'self'",
        ["connect-src"] = "'self'",
        ["worker-src"] = "'self'",
        ["manifest-src"] = "'self'",
        ["base-uri"] = "'none'",
        ["form-action"] = "'none'",
        ["frame-ancestors"] = "'none'",
        ["object-src"] = "'none'",
    };

    private static readonly string[] s_features =
        ["camera", "microphone", "geolocation", "usb", "serial", "hid", "bluetooth", "payment", "display-capture"];

    public static string Csp(IEnumerable<(string Directive, IReadOnlyList<string> Sources)> added, bool trustedTypes)
    {
        var map = added.ToDictionary(a => a.Directive, a => a.Sources);
        var parts = new List<string>();
        foreach (var directive in BaseOrder)
        {
            var sources = new List<string>();
            if (s_base.TryGetValue(directive, out var value))
            {
                sources.Add(value);
            }

            if (map.TryGetValue(directive, out var extra))
            {
                sources.AddRange(extra.Where(s => !sources.Contains(s)));
            }

            if (sources.Count > 0)
            {
                parts.Add(directive + " " + string.Join(" ", sources));
            }
        }

        if (trustedTypes)
        {
            parts.Add("require-trusted-types-for 'script'");
        }

        return string.Join("; ", parts);
    }

    public static string Permissions(IReadOnlySet<Permission> allowed) =>
        string.Join(", ", s_features.Select((feature, index) => feature + (allowed.Contains((Permission)index) ? "=(self)" : "=()")));
}
