using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Spar.Runtime;

namespace Spar.Testing;

/// <summary>What sits in front of the app's backend in a test.</summary>
public enum SparTestSurface
{
    /// <summary>No page: the test calls commands through the typed test clients.</summary>
    None,
}

/// <summary>A test's changes to the app.</summary>
public sealed class SparTestOptions
{
    internal List<Action<IServiceCollection>> ServiceActions { get; } = [];

    internal HashSet<Type> AllowedExternals { get; } = [];

    public SparTestSurface Surface { get; set; } = SparTestSurface.None;

    /// <summary>Keep the app's data root after the test (it is deleted by default).</summary>
    public bool KeepData { get; set; }

    /// <summary>Changes the app's services. Runs after the app's registrations and Spar's defaults.</summary>
    public SparTestOptions Services(Action<IServiceCollection> configure)
    {
        ServiceActions.Add(configure);
        return this;
    }

    /// <summary>Lets the test use the real implementation of an external dependency.</summary>
    public SparTestOptions AllowExternal<TService>()
    {
        AllowedExternals.Add(typeof(TService));
        return this;
    }
}

/// <summary>
/// An app started from its own <c>Program.cs</c> inside the test process (decision "Test strategy: the app runs inside
/// the test process"). Pass it to a generated client, <c>new PagesClient(app)</c>, to call its commands.
/// </summary>
public sealed class SparTestApp : ISparInvoker, IAsyncDisposable
{
    private readonly SparTestSession _session;
    private readonly Task<int> _exit;
    private readonly SparRunningApp _app;
    private readonly SparTestOptions _options;
    private long _nextCallId;

    private SparTestApp(SparTestSession session, Task<int> exit, SparRunningApp app, SparTestOptions options, TimeSpan startedAfter)
    {
        _session = session;
        _exit = exit;
        _app = app;
        _options = options;
        StartedAfter = startedAfter;
    }

    /// <summary>The app's own service provider: a test can inspect backend state directly.</summary>
    public IServiceProvider Services => _app.Services;

    /// <summary>Every folder of this app instance, all under its own data root.</summary>
    public SparFolders Folders => _app.Folders;

    /// <summary>The contracts' fingerprint, as a page would see it in <c>open</c>.</summary>
    public string Fingerprint => _app.Catalog.Fingerprint;

    public TimeSpan StartedAfter { get; }

    /// <summary>
    /// Runs <typeparamref name="TApp"/>'s top-level statements on a thread of their own. Their
    /// <c>SparApplication.Create</c> sees this test's session, and their <c>Run</c> builds the app and waits for disposal.
    /// </summary>
    public static async Task<SparTestApp> StartAsync<TApp>(Action<SparTestOptions>? configure = null, CancellationToken cancellationToken = default)
        where TApp : ISparEntryPoint
    {
        if (!SparTestSession.IsSupported)
        {
            throw new InvalidOperationException("The test host is off in this process: the test project must be built with Spar.Sdk.");
        }

        var clock = Stopwatch.StartNew();
        var options = new SparTestOptions();
        configure?.Invoke(options);

        var session = new SparTestSession
        {
            DataRoot = TestDataRoots.Next(),
            ConfigureServices = services => options.ServiceActions.ForEach(action => action(services)),
            AllowedExternals = options.AllowedExternals,
        };

        var exit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        SparTestSession.Current = session;
        var thread = new Thread(() => RunEntryPoint<TApp>(exit)) { Name = "Spar app (test)", IsBackground = true };
        thread.Start(); // The thread captures the execution context, so it sees this session and no other.
        SparTestSession.Current = null;

        var first = await Task.WhenAny(session.Started, exit.Task).WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        if (first == exit.Task)
        {
            throw new InvalidOperationException($"The app's Program.cs returned {await exit.Task} without calling Run.");
        }

        var app = await session.Started;
        return new SparTestApp(session, exit.Task, app, options, clock.Elapsed);
    }

    /// <summary>Runs one command through the app's real dispatch, as the bridge would.</summary>
    public ValueTask<SparOutcome> InvokeAsync(string contract, string command, ReadOnlyMemory<byte> request, CancellationToken cancellationToken)
    {
        var callId = Interlocked.Increment(ref _nextCallId);
        return _app.Dispatcher.InvokeAsync(contract, command, callId, request, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _session.Stop();
        var code = await _exit.WaitAsync(TimeSpan.FromSeconds(10));
        if (!_options.KeepData)
        {
            TestDataRoots.Delete(_app.Folders.DataRoot);
        }

        if (code != 0)
        {
            throw new InvalidOperationException($"The app exited with {code}.");
        }
    }

    private static void RunEntryPoint<TApp>(TaskCompletionSource<int> exit)
        where TApp : ISparEntryPoint
    {
        try
        {
            exit.TrySetResult(TApp.Run([]));
        }
        catch (Exception exception)
        {
            exit.TrySetException(exception);
        }
    }
}
