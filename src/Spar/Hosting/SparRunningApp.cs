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
        services.TryAddSingleton(limits);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<SparCall>();
        services.TryAddScoped<ISparCall>(provider => provider.GetRequiredService<SparCall>());
        catalog.AddServices(services);
    }

    internal static SparRunningApp Build(IServiceCollection services, SparCatalog catalog, SparFolders folders, Func<TimeProvider, ILogger>? createLog = null)
    {
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var testLog = createLog?.Invoke(provider.GetService<TimeProvider>() ?? TimeProvider.System);
        return new SparRunningApp(provider, new SparDispatcher(provider, catalog, testLog), catalog, folders);
    }
}
