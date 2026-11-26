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

    /// <summary>How long the end of a test waits for the window's teardown, which settles calls for at most about 4 s.</summary>
    private static readonly TimeSpan WindowCloseLimit = TimeSpan.FromSeconds(20);

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

    /// <summary>The Window surface: the app's real window, opened by the host <c>Run</c> is given (null for None and Browser).</summary>
    public SparTestWindowRequest? Window { get; init; }

    /// <summary>Called with the built app before its window opens: a scene sets its call recorder here (Spar.Testing).</summary>
    public Action<SparRunningApp>? Built { get; init; }

    public Task<SparRunningApp> Started => _started.Task;

    /// <summary>The app's window, once the host opened it (the Window surface only).</summary>
    public ISparTestWindow? OpenedWindow { get; private set; }

    public void Stop() => _stop.Set();

    internal int Run(SparApp sparApp, ISparHost? host)
    {
        var builder = sparApp.Builder;
        var catalog = sparApp.Catalog;
        var services = builder.Services;

        SparRunningApp app;
        string? loggingProblem;
        try
        {
            // The folders Program.cs could resolve early: the builder has this session's data root. A builder made
            // outside this session would resolve the per-user folders, which a test must never touch.
            var folders = builder.ResolveFolders();
            if (folders.DataRoot != DataRoot)
            {
                throw new InvalidOperationException($"The app's folders ({folders.DataRoot}) are not the test's ({DataRoot}).");
            }

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
            Built?.Invoke(app);
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

        if (Window is { } request)
        {
            // The app's real window, from the desktop host the app's build references for this OS.
            try
            {
                var windows = host as ISparTestWindowHost ?? throw new PlatformNotSupportedException(
                    "The Window surface needs the app's desktop host for this OS, and this build of the app has none: add the platform to <SparPlatforms>.");
                OpenedWindow = windows.Open(sparApp, app, request);
            }
            catch (Exception exception)
            {
                app.DisposeAsync().AsTask().GetAwaiter().GetResult();
                _started.TrySetException(exception);
                return 70;
            }
        }

        _started.TrySetResult(app);

        // This is the app's own thread (the test host started it for the entry point), so waiting blocks nothing else.
        _stop.Wait();
        try
        {
            return CloseWindow();
        }
        finally
        {
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _stop.Dispose();
        }
    }

    /// <summary>
    /// Ends the app's window before its services are disposed, as the desktop host does: the teardown, bounded, on the
    /// window's UI thread. Returns the window's exit code; 0 without a window.
    /// </summary>
    private int CloseWindow()
    {
        if (OpenedWindow is not { } window)
        {
            return 0;
        }

        var closed = window.CloseAsync();
        if (!closed.Wait(WindowCloseLimit))
        {
            throw new TimeoutException($"The app's window did not close within {WindowCloseLimit.TotalSeconds:0} s.");
        }

        return closed.Result;
    }
}
