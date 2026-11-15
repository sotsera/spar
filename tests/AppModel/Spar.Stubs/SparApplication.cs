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
        Console.WriteLine($"window size {window.InitialSize}, minimum {window.Minimum}, remembers placement {window.RemembersPlacement}");
        Console.WriteLine($"font-src additions: {string.Join(' ', _builder.ContentSecurityOptions.FontSources)}");
        Console.WriteLine($"allowed permissions: {string.Join(", ", _builder.PermissionOptions.Allowed)}");
        foreach (var (kind, options) in _builder.WindowKinds)
        {
            Console.WriteLine($"window kind {kind}: route {options.PageRoute}, modal {options.IsModal}, single {options.IsSingleInstance}, contracts {string.Join(", ", options.AllowedContracts.Select(t => t.Name))}");
        }

        Console.WriteLine($"tray: {_builder.TrayOptions?.TooltipText} [{string.Join(" | ", _builder.TrayOptions?.MenuItems.Items ?? [])}], close to tray {window.HidesToTray}");

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

    internal TrayOptions? TrayOptions { get; private set; }

    internal ShellOptions ShellOptions { get; } = new();

    internal FolderOptions FolderOptions { get; } = new();

    /// <summary>Where data, logs and crash records go. Every folder has a per-user default.</summary>
    public SparBuilder Folders(Action<FolderOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(FolderOptions);
        return this;
    }

    /// <summary>What the app may open outside itself.</summary>
    public SparBuilder Shell(Action<ShellOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(ShellOptions);
        return this;
    }

    /// <summary>A tray icon with its menu (plan 3, native services).</summary>
    public SparBuilder Tray(Action<TrayOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        TrayOptions ??= new TrayOptions();
        configure(TrayOptions);
        return this;
    }

    public SparBuilder Window(Action<WindowOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(WindowOptions);
        return this;
    }

    internal Dictionary<string, WindowOptions> WindowKinds { get; } = [];

    /// <summary>A window kind the page can open by name; the generator turns the names into a TypeScript union.</summary>
    public SparBuilder Window(string kind, Action<WindowOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        if (WindowKinds.ContainsKey(kind))
        {
            throw new InvalidOperationException($"Window kind '{kind}' is already declared.");
        }

        var options = new WindowOptions();
        configure(options);
        WindowKinds[kind] = options;
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
