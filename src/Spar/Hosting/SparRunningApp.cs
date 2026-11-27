using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Spar.Runtime;

namespace Spar;

/// <summary>A built app: its provider, folders and dispatcher. The test host builds one, and so does every host.</summary>
internal sealed class SparRunningApp(ServiceProvider services, SparDispatcher dispatcher, SparCatalog catalog, SparFolders folders) : IAsyncDisposable
{
    public IServiceProvider Services => services;

    public SparDispatcher Dispatcher { get; } = dispatcher;

    public SparCatalog Catalog { get; } = catalog;

    public SparFolders Folders { get; } = folders;

    public ValueTask DisposeAsync() => services.DisposeAsync();

    /// <summary>Spar's own services, after the app's, so an app registration (or a test's) wins.</summary>
    internal static void AddSparServices(IServiceCollection services, SparCatalog catalog, SparFolders folders, BridgeLimits limits)
    {
        services.TryAddSingleton(folders);
        services.TryAddSingleton(new Hosting.BrowserProfile(folders.Profile));
        services.TryAddSingleton(limits);
        services.TryAddSingleton(TimeProvider.System);
        services.AddLogging();
        services.TryAddScoped<SparCall>();
        services.TryAddScoped<ISparCall>(provider => provider.GetRequiredService<SparCall>());
        services.TryAddSingleton(new Settings.SparSettingsSerializers(catalog.Settings));
        services.TryAddSingleton(provider => new SparBlobs(provider.GetRequiredService<SparFolders>()));
        catalog.AddServices(services);
    }

    internal static SparRunningApp Build(IServiceCollection services, SparCatalog catalog, SparFolders folders, Func<TimeProvider, ILogger>? createLog = null)
    {
        RequireImplementations(services, catalog);
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var testLog = createLog?.Invoke(provider.GetService<TimeProvider>() ?? TimeProvider.System);

        // What must run once the services exist, without a generic host to start it (the OpenTelemetry layer's providers).
        foreach (var startup in provider.GetServices<SparStartup>())
        {
            startup.Start(provider);
        }

        return new SparRunningApp(provider, new SparDispatcher(provider, catalog, testLog), catalog, folders);
    }

    /// <summary>
    /// The provider's validation checks what is registered, so it cannot see an interface contract the app never
    /// registered: without this check, the app would start and every call to that contract would fail.
    /// </summary>
    private static void RequireImplementations(IServiceCollection services, SparCatalog catalog)
    {
        foreach (var contract in catalog.Interfaces)
        {
            if (services.Any(descriptor => descriptor.ServiceType == contract.Service))
            {
                continue;
            }

            var shortName = contract.Name[(contract.Name.LastIndexOf('.') + 1)..];
            throw new InvalidOperationException(
                $"The contract '{contract.Contract}' is the interface {contract.Name}, and the app registers no " +
                $"implementation of it. Register one in SparApplication.Create, for example " +
                $"spar.Services.AddScoped<{shortName}, MyImplementation>().");
        }
    }
}

/// <summary>
/// Work that runs once the app's services are built, before the window or the first call (an app has no generic host
/// to start <c>IHostedService</c>s): the optional OpenTelemetry layer registers one that resolves its providers, which
/// start their exporters when they are created.
/// </summary>
internal sealed class SparStartup(Action<IServiceProvider> start)
{
    public void Start(IServiceProvider services) => start(services);
}
