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
        Console.WriteLine($"content security: {_builder.ContentSecurityOptions.Describe()}");
        Console.WriteLine($"allowed permissions: {string.Join(", ", _builder.PermissionOptions.Allowed)}");
        foreach (var (kind, options) in _builder.WindowKinds)
        {
            Console.WriteLine($"window kind {kind}: route {options.PageRoute}, modal {options.IsModal}, single {options.IsSingleInstance}, contracts {string.Join(", ", options.AllowedContracts.Select(t => t.Name))}");
        }

        Console.WriteLine($"browser opt-ins: {string.Join(", ", _builder.BrowserOptions.Allowed)}");
        Console.WriteLine($"page hang hook {(_builder.PageHangHandler is null ? "none" : "set")}, stall probe kept {_builder.DiagnosticsOptions.KeepsStallProbe}");
        var logging = _builder.LoggingOptions;
        Console.WriteLine($"log file {logging.WritesFile}, level {logging.DefaultLevel?.ToString() ?? "the build's"}, categories {string.Join(", ", logging.Categories.Select(c => $"{c.Category} {c.Level}"))}");
        Console.WriteLine($"folders resolved early: {_builder.ResolvedFolders?.ToString() ?? "no"}");
        Console.WriteLine($"tray: {_builder.TrayOptions?.TooltipText} [{string.Join(" | ", _builder.TrayOptions?.MenuItems.Items ?? [])}], close to tray {window.HidesToTray}");

        foreach (var service in _builder.Services)
        {
            Console.WriteLine($"service: {service.Lifetime} {service.ServiceType.Name} -> {service.ImplementationType?.Name ?? service.ImplementationInstance?.GetType().Name}");
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

    internal SparFolders? ResolvedFolders { get; private set; }

    internal BrowserOptions BrowserOptions { get; } = new();

    /// <summary>Loosens the browser's strict defaults; each opt-in is logged at start-up.</summary>
    public SparBuilder Browser(Action<BrowserOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(BrowserOptions);
        return this;
    }

    /// <summary>Where data, logs and crash records go. Every folder has a per-user default. Throws after ResolveFolders.</summary>
    public SparBuilder Folders(Action<FolderOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        if (ResolvedFolders is not null)
        {
            throw new InvalidOperationException("The folders were already resolved by spar.ResolveFolders().");
        }

        configure(FolderOptions);
        return this;
    }

    /// <summary>The folders the services will get, for what Program.cs builds before they exist (3.13).</summary>
    public SparFolders ResolveFolders()
    {
        if (ResolvedFolders is null)
        {
            var root = Path.Combine(Path.GetTempPath(), "com.example.SqlTool");
            ResolvedFolders = new SparFolders(
                root,
                FolderOptions.DataRoot ?? Path.Combine(root, "data"),
                FolderOptions.LogsFolder ?? Path.Combine(root, "logs"),
                Path.Combine(root, "cache"),
                FolderOptions.ProfileFolder ?? Path.Combine(root, "WebView2"));
        }

        return ResolvedFolders;
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

    internal Func<CloseRequest, IServiceProvider, CancellationToken, ValueTask<CloseDecision>>? CloseGuard { get; private set; }

    /// <summary>The backend's close guard, asked after the page's guard agreed (two-stage close).</summary>
    public SparBuilder OnCloseRequested(Func<CloseRequest, IServiceProvider, CancellationToken, ValueTask<CloseDecision>> guard)
    {
        ArgumentNullException.ThrowIfNull(guard);
        CloseGuard = guard;
        return this;
    }

    /// <summary>The backend's close guard when it decides at once.</summary>
    public SparBuilder OnCloseRequested(Func<CloseRequest, IServiceProvider, CloseDecision> guard)
    {
        ArgumentNullException.ThrowIfNull(guard);
        return OnCloseRequested((request, services, _) => ValueTask.FromResult(guard(request, services)));
    }

    internal Func<Relaunch, IServiceProvider, CancellationToken, ValueTask>? RelaunchHandler { get; private set; }

    /// <summary>Receives later launches' arguments, after Spar has shown and focused the main window.</summary>
    public SparBuilder OnRelaunch(Func<Relaunch, IServiceProvider, CancellationToken, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        RelaunchHandler = handler;
        return this;
    }

    /// <summary>The relaunch handler when it finishes at once.</summary>
    public SparBuilder OnRelaunch(Action<Relaunch, IServiceProvider> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return OnRelaunch((relaunch, services, _) =>
        {
            handler(relaunch, services);
            return ValueTask.CompletedTask;
        });
    }

    internal Func<StartupProblem, bool>? StartupProblemHandler { get; private set; }

    /// <summary>Runs when the app cannot start; true when the app showed its own message, so Spar shows none.</summary>
    public SparBuilder OnStartupProblem(Func<StartupProblem, bool> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        StartupProblemHandler = handler;
        return this;
    }

    internal Func<PageHang, IServiceProvider, CancellationToken, ValueTask>? PageHangHandler { get; private set; }

    /// <summary>Observes a page that stops responding (3.16); Spar's dialog always shows.</summary>
    public SparBuilder OnPageUnresponsive(Func<PageHang, IServiceProvider, CancellationToken, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        PageHangHandler = handler;
        return this;
    }

    /// <summary>The page hang hook when it finishes at once.</summary>
    public SparBuilder OnPageUnresponsive(Action<PageHang, IServiceProvider> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return OnPageUnresponsive((hang, services, _) =>
        {
            handler(hang, services);
            return ValueTask.CompletedTask;
        });
    }

    internal DiagnosticsOptions DiagnosticsOptions { get; } = new();

    /// <summary>What Spar records for diagnosing the app (3.13).</summary>
    public SparBuilder Diagnostics(Action<DiagnosticsOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(DiagnosticsOptions);
        return this;
    }

    internal LoggingOptions LoggingOptions { get; } = new();

    /// <summary>The log file's levels (3.13); a per-user logging.json overrides them at start.</summary>
    public SparBuilder Logging(Action<LoggingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(LoggingOptions);
        return this;
    }
}
