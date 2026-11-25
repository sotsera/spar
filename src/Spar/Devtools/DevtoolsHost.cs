using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Spar.Devtools;

/// <summary>
/// What a host does for the devtools, in its order: <see cref="Prepare"/> while it adds the app's services (the merged
/// log is a logger provider), <see cref="Start"/> once the services are built, the window's page into
/// <see cref="Pages"/> when its automation is installed, and <see cref="StopAsync"/> before the services are disposed.
/// Every call site reads <see cref="SparDevtools.IsSupported"/> itself, so the trimmer removes all of it from other builds.
/// </summary>
internal sealed class DevtoolsHost
{
    /// <summary>The engine's resource in a Development build of an app (Spar.Sdk embeds it with the contract snapshot).</summary>
    public const string EngineResource = "Spar.Devtools.PageEngine.js";

    /// <summary>The page engine's isolated world, the window tests' name, so the page's own scripts never see it.</summary>
    public const string World = "spar-automation";

    private readonly string _host;
    private readonly string _file;
    private DevtoolsServer? _server;

    private DevtoolsHost(string host, string file, DevtoolsLog log, string? pagesUnavailable)
    {
        _host = host;
        _file = file;
        Log = log;
        Pages = new DevtoolsPages { Unavailable = pagesUnavailable };
    }

    public DevtoolsPages Pages { get; }

    public DevtoolsLog Log { get; }

    /// <summary>
    /// The devtools of this run, or null when the build named no discovery file (a test or a library). Adds the merged
    /// log to <paramref name="services"/>. <paramref name="pagesUnavailable"/> says why a host has no page tools.
    /// </summary>
    public static DevtoolsHost? Prepare(IServiceCollection services, string host, string? pagesUnavailable = null)
    {
        if (SparDevtools.DiscoveryFile(AppContext.GetData) is not { } file)
        {
            return null;
        }

        return new DevtoolsHost(host, file, DevtoolsLog.AddTo(services, TimeProvider.System), pagesUnavailable);
    }

    /// <summary>The page engine the build embedded in the app, or null when it embedded none.</summary>
    public static string? EngineScript(Assembly? app)
    {
        using var stream = app?.GetManifestResourceStream(EngineResource);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public void Start(SparRunningApp running, string run, ILogger log) =>
        _server = DevtoolsServer.Start(new DevtoolsContext(running, run, _host, _file, Pages, Log), log);

    public async ValueTask StopAsync()
    {
        Pages.Current = null;
        if (_server is { } server)
        {
            await server.DisposeAsync();
        }
    }
}
