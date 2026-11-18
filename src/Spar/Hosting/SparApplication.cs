using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Spar.Runtime;

namespace Spar;

/// <summary>
/// The app's one composition, in <c>Program.cs</c>:
/// <c>var app = SparApplication.Create(args, spar => …); return app.Run();</c>
/// </summary>
public static class SparApplication
{
    /// <summary>Configures the app. Spar's generator connects this call to the app's generated contracts.</summary>
    public static SparApp Create(string[] args, Action<SparBuilder> configure) =>
        throw new InvalidOperationException(
            "SparApplication.Create was not connected to the app's generated contracts. Build the project with Spar.Sdk, " +
            "and call Create directly in the top-level statements of Program.cs.");

    /// <summary>The form Spar's generated code calls, with the app's contract catalog.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static SparApp Create(string[] args, Action<SparBuilder> configure, SparCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(catalog);

        // A test session exists only in test processes; the feature switch trims these branches from apps. The builder
        // gets the session's data root, so spar.ResolveFolders() gives the folders the test host will use.
        var session = SparTestSession.IsSupported ? SparTestSession.Current : null;
        var testDataRoot = SparTestSession.IsSupported ? session?.DataRoot : null;
        var builder = new SparBuilder(args, SparApp.AppIdOf(catalog), testDataRoot);
        configure(builder);
        return new SparApp(builder, catalog, session);
    }
}

/// <summary>What <c>Program.cs</c> configures.</summary>
public sealed class SparBuilder
{
    /// <summary>The test host's data root when <c>Program.cs</c> runs under it, else null.</summary>
    private readonly string? _testDataRoot;

    /// <summary>The folders, once <see cref="ResolveFolders"/> decided them.</summary>
    private SparFolders? _folders;

    internal SparBuilder(string[] args, string appId = "Spar", string? testDataRoot = null)
    {
        Args = args;
        AppId = appId;
        _testDataRoot = testDataRoot;
    }

    public string[] Args { get; }

    /// <summary>The app id the build checked (<c>SparAppId</c>), which names the app's per-user folder.</summary>
    internal string AppId { get; }

    /// <summary>The app's services (Microsoft DI). Each command runs in its own scope.</summary>
    public IServiceCollection Services { get; } = new ServiceCollection();

    internal FolderOptions FolderOptions { get; } = new();

    internal BridgeOptions BridgeOptions { get; } = new();

    /// <summary>The main window's options.</summary>
    internal WindowOptions MainWindow { get; } = new();

    /// <summary>The backend's close guard, or null when the app declared none.</summary>
    internal Func<CloseRequest, IServiceProvider, CancellationToken, ValueTask<CloseDecision>>? CloseGuard { get; private set; }

    /// <summary>The relaunch handler, or null when the app declared none.</summary>
    internal Func<Relaunch, IServiceProvider, CancellationToken, ValueTask>? RelaunchHandler { get; private set; }

    /// <summary>The start-up hook, or null when the app declared none.</summary>
    internal Func<StartupProblem, bool>? StartupProblemHandler { get; private set; }

    /// <summary>The page hang hook, or null when the app declared none.</summary>
    internal Func<PageHang, IServiceProvider, CancellationToken, ValueTask>? PageHangHandler { get; private set; }

    /// <summary>What Spar records for diagnosing the app.</summary>
    internal DiagnosticsOptions DiagnosticsOptions { get; } = new();

    /// <summary>The log file's levels as <c>Program.cs</c> set them.</summary>
    internal LoggingOptions LoggingOptions { get; } = new();

    /// <summary>The browser's strict defaults the app opted out of (plan section 3c).</summary>
    internal BrowserOptions BrowserOptions { get; } = new();

    /// <summary>The page's Content Security Policy as this run's configuration declared it.</summary>
    internal CspBuilder ContentSecurityOptions { get; } = new();

    /// <summary>The powerful features this run's configuration declared.</summary>
    internal PermissionsBuilder PermissionOptions { get; } = new();

    /// <summary>
    /// The page security this run's configuration declared. Hosts use the build's copy from the generated catalog
    /// (<see cref="SparCatalog.PageSecurity"/>); a Development build compares the two.
    /// </summary>
    internal SparPageSecurity DeclaredPageSecurity => ContentSecurityOptions.Build(PermissionOptions.Allowed);

    /// <summary>
    /// The main window: its colours before the page paints (plan section 3d), its size and its minimum size. The
    /// colours are read by the build, so give them as constants.
    /// </summary>
    public SparBuilder Window(Action<WindowOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(MainWindow);
        return this;
    }

    /// <summary>
    /// Where data, logs and the browser profile go; each has a per-user default under the app id's folder (plan section
    /// 3c). A test host moves them all under its data root. App code reads the folders in use from the
    /// <see cref="SparFolders"/> service. Call it before <see cref="ResolveFolders"/>: afterwards it throws.
    /// </summary>
    public SparBuilder Folders(Action<FolderOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        if (_folders is not null)
        {
            throw new InvalidOperationException(FolderOptions.ResolvedMessage);
        }

        configure(FolderOptions);
        return this;
    }

    /// <summary>
    /// The folders the app's services will get as the <see cref="SparFolders"/> service, decided now, for what
    /// <c>Program.cs</c> builds before the services exist, such as the app's own configuration file in its data folder:
    /// <c>%LOCALAPPDATA%\&lt;app id&gt;</c> on Windows (<c>&lt;app id&gt;.Development</c> in a Development build), the
    /// app's <c>spar.Folders(…)</c> choices, or the test host's own root in a test run. Call <c>spar.Folders(…)</c>
    /// first: after this call it throws, so the value can never differ from the service. Calling it again returns the
    /// same folders. App code that runs once the services exist takes the <see cref="SparFolders"/> service instead.
    /// </summary>
    public SparFolders ResolveFolders()
    {
        if (_folders is null)
        {
            _folders = _testDataRoot is not null
                ? SparFolders.ForTest(_testDataRoot)
                : SparFolders.ForApp(AppId, Hosting.SparDevelopment.IsSupported, FolderOptions);
            FolderOptions.Freeze();
        }

        return _folders;
    }

    /// <summary>
    /// Loosens the browser's strict defaults (plan section 3c), for example <c>spar.Browser(b => b.AllowDownloads())</c>.
    /// Every opt-in is for the whole app, documents its risk, and is logged at start-up. Without this call a Production
    /// build has devtools, the browser's keys, password saving and autofill, downloads, history navigation, the full
    /// context menu, the page's own file pickers and the WebView2 environment variables off.
    /// </summary>
    public SparBuilder Browser(Action<BrowserOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(BrowserOptions);
        return this;
    }

    /// <summary>
    /// The bridge's limits per document. The defaults protect the app from a page that sends too much; raise one only
    /// for a need you have measured, since it lets the page hold more of the app (see <see cref="Spar.BridgeOptions"/>).
    /// </summary>
    public SparBuilder Bridge(Action<BridgeOptions> configure)
    {
        configure(BridgeOptions);
        _ = BridgeOptions.Freeze();
        return this;
    }

    /// <summary>
    /// The backend's close guard (<c>docs/app-model.md</c> 6.11). It runs after the page's guard, off the window's UI
    /// thread, with the app's root services, and answers <see cref="CloseDecision.Close"/>,
    /// <see cref="CloseDecision.Keep"/> or <see cref="CloseDecision.Ask"/>. The token is cancelled at the session-end
    /// deadline (about 5 s) and when the app stops.
    /// </summary>
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

    /// <summary>
    /// Receives each later launch of the app (<c>docs/app-model.md</c> 3.14): Spar has already shown and focused the
    /// main window. It runs off the window's UI thread, one relaunch at a time, with the app's root services; the
    /// token is cancelled when the app stops. The arguments are untrusted input.
    /// </summary>
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

    /// <summary>
    /// Runs when the app cannot start (<c>docs/app-model.md</c> 3.15): a missing, old or broken prerequisite, an
    /// operating system that is too old, or a relaunch that cannot reach the running app. It runs before the app's
    /// services exist, on a thread of its own. Return true when it showed the app's own message, and Spar shows none;
    /// return false to let Spar show its message. Standard error and the per-user log are written either way, the
    /// process exits with the problem's fixed code, and in the automated mode the hook is not called.
    /// </summary>
    public SparBuilder OnStartupProblem(Func<StartupProblem, bool> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        StartupProblemHandler = handler;
        return this;
    }

    /// <summary>
    /// Observes a window's page that stops answering (<c>docs/app-model.md</c> 3.16), for logs and telemetry: called
    /// when it becomes unresponsive, recovers, is reloaded, or its window is closed anyway, with how long it had not
    /// answered. It runs off the window's UI thread, one change at a time, each awaited before the next, with the app's
    /// root services; one that throws is logged. Its token is cancelled when the app exits and the short wait for the
    /// hook has ended. It cannot hide Spar's dialog, reload the page or close the window: only the user's answer does.
    /// </summary>
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

    /// <summary>
    /// Widens the page's strict Content Security Policy (plan section 3b; <c>docs/app-model.md</c> section 2), for
    /// example <c>spar.ContentSecurity(csp => csp.FontSrc("https://fonts.gstatic.com"))</c>. The build reads it from
    /// <c>Program.cs</c> without running the app, so give constant values directly in the <c>Create</c> configuration.
    /// It may be called more than once; the values add up.
    /// </summary>
    public SparBuilder ContentSecurity(Action<CspBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(ContentSecurityOptions);
        return this;
    }

    /// <summary>
    /// Declares the powerful browser features the page may use (plan section 3b), for example
    /// <c>spar.Permissions(p => p.Allow(Permission.ClipboardRead))</c>. Everything else is denied with no prompt. Read
    /// by the build like <see cref="ContentSecurity"/>.
    /// </summary>
    public SparBuilder Permissions(Action<PermissionsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(PermissionOptions);
        return this;
    }

    /// <summary>
    /// What Spar records for diagnosing the app on a user's machine (<c>docs/app-model.md</c> 3.13), such as
    /// <see cref="DiagnosticsOptions.KeepStallProbe"/> in a Production build.
    /// </summary>
    public SparBuilder Diagnostics(Action<DiagnosticsOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(DiagnosticsOptions);
        return this;
    }

    /// <summary>
    /// The app's log levels and its log file (<c>docs/app-model.md</c> 3.13), for example
    /// <c>spar.Logging(l => l.Level(LogLevel.Information).Level("SqlTool.Copy", LogLevel.Debug))</c>. Every app writes
    /// <c>app.log</c> in its logs folder: Spar's lines and every <c>ILogger</c> of the app's services, Information and
    /// above in Production, Debug and above in Development and Test builds unless set here. A <c>logging.json</c> in the
    /// app's per-user folder overrides these at start (<c>{"level":"Debug","categories":{"SqlTool.Copy":"Trace"}}</c>),
    /// so a user can raise the level for support without a new build, even over the app's own logging configuration. The
    /// levels are the app's, for every provider; a rule of the app's own for every category (a configuration's
    /// <c>"Default"</c>) overrides the default set here
    /// (see <see cref="LoggingOptions.Level(Microsoft.Extensions.Logging.LogLevel)"/>).
    /// <c>l.WithoutFile()</c> writes no file (see <see cref="LoggingOptions.WithoutFile"/> for its risk).
    /// </summary>
    public SparBuilder Logging(Action<LoggingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(LoggingOptions);
        return this;
    }
}

/// <summary>The configured app. <see cref="Run()"/> starts it and returns the process exit code.</summary>
public sealed class SparApp
{
    /// <summary>The exit code when this build has no host for the OS it runs on (plan section 3, "Missing prerequisites").</summary>
    internal const int NoHostExitCode = 3;

    private readonly SparBuilder _builder;
    private readonly SparCatalog _catalog;
    private readonly SparTestSession? _session;

    internal SparApp(SparBuilder builder, SparCatalog catalog, SparTestSession? session)
    {
        _builder = builder;
        _catalog = catalog;
        _session = session;
    }

    /// <summary>What <c>Program.cs</c> configured; a host builds the app from it.</summary>
    internal SparBuilder Builder => _builder;

    /// <summary>The app's generated contracts.</summary>
    internal SparCatalog Catalog => _catalog;

    /// <summary>
    /// The app id the build checked (<c>SparAppId</c>), from the generated catalog; the assembly's name for an app built
    /// without Spar's build steps, which a Spar.Sdk app never is.
    /// </summary>
    internal string AppId => _builder.AppId;

    /// <summary>The app id of a catalog: <see cref="AppId"/>'s source.</summary>
    internal static string AppIdOf(SparCatalog catalog) => catalog.AppId ?? catalog.AppAssembly?.GetName().Name ?? "Spar";

    /// <summary>Starts the app in its window and returns the process exit code when it ends.</summary>
    public int Run() => Run(host: null);

    /// <summary>
    /// The form Spar's generated code calls with the desktop host the app's build references for this OS (decision
    /// "The Windows host, first slice"): the generator intercepts <c>app.Run()</c>, so no host is found by reflection,
    /// and each NativeAOT executable keeps only its own OS's host.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public int Run(ISparHost? host)
    {
        if (SparTestSession.IsSupported && _session is not null)
        {
            return _session.Run(_builder, _catalog);
        }

        if (host is null)
        {
            Console.Error.WriteLine(
                $"This build of the app has no desktop host for {System.Runtime.InteropServices.RuntimeInformation.OSDescription}. " +
                "Add the platform to <SparPlatforms> in the app's project (windows, linux) and build it again.");
            return NoHostExitCode;
        }

        return host.Run(this);
    }
}
