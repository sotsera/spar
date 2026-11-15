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
}

/// <summary>The configured app. <see cref="Run"/> starts it and returns the process exit code.</summary>
public sealed class SparApp
{
    private readonly SparBuilder _builder;
    private readonly SparCatalog _catalog;
    private readonly SparTestSession? _session;

    internal SparApp(SparBuilder builder, SparCatalog catalog, SparTestSession? session)
    {
        _builder = builder;
        _catalog = catalog;
        _session = session;
    }

    public int Run()
    {
        if (SparTestSession.IsSupported && _session is not null)
        {
            return _session.Run(_builder, _catalog);
        }

        // M1a has no window; the desktop hosts arrive in M1b and M1c.
        Console.Error.WriteLine("This build of Spar has no desktop host yet. Run the app's tests instead.");
        return 2;
    }
}
