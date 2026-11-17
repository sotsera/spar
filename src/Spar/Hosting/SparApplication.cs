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

        var builder = new SparBuilder(args);
        configure(builder);

        // A test session exists only in test processes; the feature switch trims this branch from apps.
        var session = SparTestSession.IsSupported ? SparTestSession.Current : null;
        return new SparApp(builder, catalog, session);
    }
}

/// <summary>What <c>Program.cs</c> configures.</summary>
public sealed class SparBuilder
{
    internal SparBuilder(string[] args)
    {
        Args = args;
    }

    public string[] Args { get; }

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

    /// <summary>Where data and logs go; each has a per-user default. A test host moves them all under its data root.</summary>
    public SparBuilder Folders(Action<FolderOptions> configure)
    {
        configure(FolderOptions);
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
