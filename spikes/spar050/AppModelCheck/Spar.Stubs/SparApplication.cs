using Microsoft.Extensions.DependencyInjection;

namespace Spar;

/// <summary>Stand-in for the app: records the configuration and reports it from <see cref="Run"/>.</summary>
public sealed class SparApplication
{
    private readonly SparBuilder _builder;

    private SparApplication(SparBuilder builder)
    {
        _builder = builder;
    }

    public static SparApplication Create(string[] args, Action<SparBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new SparBuilder();
        configure(builder);
        return new SparApplication(builder);
    }

    public int Run()
    {
        var window = _builder.WindowOptions;
        Console.WriteLine($"window background: light {window.LightBackground}, dark {window.DarkBackground}");
        Console.WriteLine($"font-src additions: {string.Join(' ', _builder.ContentSecurityOptions.FontSources)}");
        Console.WriteLine($"allowed permissions: {string.Join(", ", _builder.PermissionOptions.Allowed)}");

        foreach (var service in _builder.Services)
        {
            Console.WriteLine($"service: {service.Lifetime} {service.ServiceType.Name} -> {service.ImplementationType?.Name}");
        }

        return 0;
    }
}

public sealed class SparBuilder
{
    internal SparBuilder()
    {
    }

    public IServiceCollection Services { get; } = new ServiceCollection();

    internal WindowOptions WindowOptions { get; } = new();

    internal CspBuilder ContentSecurityOptions { get; } = new();

    internal PermissionsBuilder PermissionOptions { get; } = new();

    public SparBuilder Window(Action<WindowOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(WindowOptions);
        return this;
    }

    public SparBuilder ContentSecurity(Action<CspBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(ContentSecurityOptions);
        return this;
    }

    public SparBuilder Permissions(Action<PermissionsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(PermissionOptions);
        return this;
    }
}

public sealed class WindowOptions
{
    internal WindowOptions()
    {
    }

    internal string? LightBackground { get; private set; }

    internal string? DarkBackground { get; private set; }

    /// <summary>The window colour before the page paints, for each theme.</summary>
    public WindowOptions Background(string light, string dark)
    {
        LightBackground = light;
        DarkBackground = dark;
        return this;
    }
}

public sealed class CspBuilder
{
    private readonly List<string> _fontSources = [];

    internal CspBuilder()
    {
    }

    internal IReadOnlyList<string> FontSources => _fontSources;

    /// <summary>Adds sources to <c>font-src</c> on top of the strict default.</summary>
    public CspBuilder FontSrc(params string[] sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _fontSources.AddRange(sources);
        return this;
    }
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

/// <summary>Powerful browser features, denied unless allowed.</summary>
public enum Permission
{
    ClipboardRead,
}
