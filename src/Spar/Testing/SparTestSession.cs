using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Spar.Runtime;

namespace Spar;

/// <summary>
/// The test host's hook (plan section 5; decision "Test host mechanics: the generated entry point"). A test sets
/// <see cref="Current"/>, an <c>AsyncLocal</c>, and calls the app's entry point on a thread of its own;
/// <c>SparApplication.Create</c> captures the session, and <c>Run</c> builds the app with the test's folders and
/// service overrides, hands it to the test and waits until the test is done. Apps set the feature switch off, so the
/// trimmer removes this type and every branch that reaches it.
/// </summary>
internal sealed class SparTestSession
{
    private static readonly AsyncLocal<SparTestSession?> CurrentSession = new();

    private readonly TaskCompletionSource<SparRunningApp> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ManualResetEventSlim _stop = new();

    [FeatureSwitchDefinition("Spar.TestHost.IsSupported")]
    internal static bool IsSupported { get; } = AppContext.TryGetSwitch("Spar.TestHost.IsSupported", out var on) && on;

    internal static SparTestSession? Current
    {
        get => CurrentSession.Value;
        set => CurrentSession.Value = value;
    }

    public required string DataRoot { get; init; }

    public Action<IServiceCollection>? ConfigureServices { get; init; }

    public required IReadOnlySet<Type> AllowedExternals { get; init; }

    public Task<SparRunningApp> Started => _started.Task;

    public void Stop() => _stop.Set();

    internal int Run(SparBuilder builder, SparCatalog catalog)
    {
        var services = builder.Services;
        var folders = SparFolders.ForTest(DataRoot);

        SparRunningApp app;
        try
        {
            SparRunningApp.AddSparServices(services, catalog, folders);

            // The test's overrides run last, after the app's registrations and Spar's defaults.
            ConfigureServices?.Invoke(services);
            SparExternalGuard.Apply(services, AllowedExternals);
            app = SparRunningApp.Build(services, catalog, folders);
        }
        catch (Exception exception)
        {
            _started.TrySetException(exception);
            return 70;
        }

        _started.TrySetResult(app);

        // This is the app's own thread (the test host started it for the entry point), so waiting blocks nothing else.
        _stop.Wait();
        app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _stop.Dispose();
        return 0;
    }
}

/// <summary>A built app: its provider, folders and dispatcher.</summary>
internal sealed class SparRunningApp(ServiceProvider services, SparDispatcher dispatcher, SparCatalog catalog, SparFolders folders) : IAsyncDisposable
{
    public IServiceProvider Services => services;

    public SparDispatcher Dispatcher { get; } = dispatcher;

    public SparCatalog Catalog { get; } = catalog;

    public SparFolders Folders { get; } = folders;

    public ValueTask DisposeAsync() => services.DisposeAsync();

    /// <summary>Spar's own services, after the app's, so an app registration (or a test's) wins.</summary>
    internal static void AddSparServices(IServiceCollection services, SparCatalog catalog, SparFolders folders)
    {
        services.TryAddSingleton(folders);
        services.TryAddScoped<SparCall>();
        services.TryAddScoped<ISparCall>(provider => provider.GetRequiredService<SparCall>());
        catalog.AddServices(services);
    }

    internal static SparRunningApp Build(IServiceCollection services, SparCatalog catalog, SparFolders folders)
    {
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        return new SparRunningApp(provider, new SparDispatcher(provider, catalog), catalog, folders);
    }
}
