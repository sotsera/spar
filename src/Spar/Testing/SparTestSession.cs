using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spar.Hosting;
using Spar.Logging;
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

    /// <summary>Makes the app instance's own log, given the app's clock; Spar.Testing passes it.</summary>
    public Func<TimeProvider, ILogger>? CreateLog { get; init; }

    public Task<SparRunningApp> Started => _started.Task;

    public void Stop() => _stop.Set();

    internal int Run(SparBuilder builder, SparCatalog catalog)
    {
        var services = builder.Services;
        var folders = SparFolders.ForTest(DataRoot);

        SparRunningApp app;
        string? loggingProblem;
        try
        {
            // The app's log file as a desktop run writes it, in this test's own logs folder, deleted with its data root;
            // the services open it with their first logger and close it when they are disposed.
            var logging = builder.LoggingOptions;
            var levels = SparLogging.Levels(logging, folders.DataRoot, SparDevelopment.IsSupported, out loggingProblem);
            Func<LogFile>? file = logging.WritesFile ? () => LogFile.Open(folders.Logs) : null;
            SparLogging.Add(services, levels, file, ownsFile: true);
            SparRunningApp.AddSparServices(services, catalog, folders, builder.BridgeOptions.Freeze());

            // The test's overrides run last, after the app's registrations and Spar's defaults.
            ConfigureServices?.Invoke(services);
            SparExternalGuard.Apply(services, AllowedExternals);
            app = SparRunningApp.Build(services, catalog, folders, CreateLog);
        }
        catch (Exception exception)
        {
            _started.TrySetException(exception);
            return 70;
        }

        if (loggingProblem is not null)
        {
            app.Services.GetService<ILoggerFactory>()?.CreateLogger(SparLogging.HostCategory).LogWarning("{Problem}", loggingProblem);
        }

        _started.TrySetResult(app);

        // This is the app's own thread (the test host started it for the entry point), so waiting blocks nothing else.
        _stop.Wait();
        app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _stop.Dispose();
        return 0;
    }
}
