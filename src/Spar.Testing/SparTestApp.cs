using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Spar.Loopback;
using Spar.Runtime;

namespace Spar.Testing;

/// <summary>What sits in front of the app's backend in a test.</summary>
public enum SparTestSurface
{
    /// <summary>No page: the test calls commands through the typed test clients.</summary>
    None,

    /// <summary>
    /// The page in an ordinary browser over in-process loopback: open <see cref="SparTestApp.PageUrl"/> with Playwright.
    /// CoreCLR only, like Playwright for .NET.
    /// </summary>
    Browser,
}

/// <summary>A test's changes to the app.</summary>
public sealed class SparTestOptions
{
    internal List<Action<IServiceCollection>> ServiceActions { get; } = [];

    internal HashSet<Type> AllowedExternals { get; } = [];

    internal List<(string Prefix, string Folder)> PageFolders { get; } = [];

    public SparTestSurface Surface { get; set; } = SparTestSurface.None;

    /// <summary>Keep the app's data root after the test (it is deleted by default).</summary>
    public bool KeepData { get; set; }

    /// <summary>Changes the app's services. Runs after the app's registrations and Spar's defaults.</summary>
    public SparTestOptions Services(Action<IServiceCollection> configure)
    {
        ServiceActions.Add(configure);
        return this;
    }

    /// <summary>
    /// Serves <paramref name="folder"/> as the page, or under <paramref name="prefix"/> (for example <c>"spar/"</c>), on
    /// the Browser surface. A stand-in until the packing step embeds the app's built page, which then becomes the default.
    /// </summary>
    public SparTestOptions Page(string folder, string prefix = "")
    {
        PageFolders.Add((prefix, folder));
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
    private readonly ConcurrentQueue<string> _problems = new();
    private LoopbackHost? _loopback;
    private long _nextCallId;

    private SparTestApp(SparTestSession session, Task<int> exit, SparRunningApp app, SparTestOptions options)
    {
        _session = session;
        _exit = exit;
        _app = app;
        _options = options;
    }

    /// <summary>The app's own service provider: a test can inspect backend state directly.</summary>
    public IServiceProvider Services => _app.Services;

    /// <summary>Every folder of this app instance, all under its own data root.</summary>
    public SparFolders Folders => _app.Folders;

    /// <summary>The contracts' fingerprint, as a page would see it in <c>open</c>.</summary>
    public string Fingerprint => _app.Catalog.Fingerprint;

    public TimeSpan StartedAfter { get; private set; }

    /// <summary>The page's origin on the Browser surface, for example <c>http://127.0.0.1:52311</c>.</summary>
    public Uri Origin => Loopback.Origin;

    /// <summary>What the bridge refused or dropped, and the page's CSP violations, so far. A clean run has none.</summary>
    public IReadOnlyList<string> Problems => [.. _problems];

    private LoopbackHost Loopback =>
        _loopback ?? throw new InvalidOperationException("The app has no page: start it with Surface = SparTestSurface.Browser.");

    /// <summary>The page's address with its token in the fragment, for the browser to open (Browser surface).</summary>
    public Uri PageUrl(string path = "/") => Loopback.PageUrl(path);

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

        var testApp = new SparTestApp(session, exit.Task, await session.Started, options);
        try
        {
            if (options.Surface == SparTestSurface.Browser)
            {
                await testApp.StartBrowserSurfaceAsync(cancellationToken);
            }
        }
        catch
        {
            await testApp.DisposeAsync();
            throw;
        }

        testApp.StartedAfter = clock.Elapsed;
        return testApp;
    }

    /// <summary>Runs one command through the app's real dispatch, as the bridge would.</summary>
    public ValueTask<SparOutcome> InvokeAsync(string contract, string command, ReadOnlyMemory<byte> request, CancellationToken cancellationToken)
    {
        var callId = Interlocked.Increment(ref _nextCallId);
        return _app.Dispatcher.InvokeAsync(contract, command, callId, request, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_loopback is not null)
        {
            await _loopback.DisposeAsync();
        }

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

    private async Task StartBrowserSurfaceAsync(CancellationToken cancellationToken)
    {
        if (_options.PageFolders.Count == 0)
        {
            throw new InvalidOperationException("The Browser surface needs the page: pass its folder with o.Page(folder).");
        }

        var assets = new LoopbackAssets();
        foreach (var (prefix, folder) in _options.PageFolders)
        {
            assets.Mount(prefix, folder);
        }

        _loopback = await LoopbackHost.StartAsync(
            new LoopbackOptions
            {
                Dispatcher = _app.Dispatcher,
                Fingerprint = _app.Catalog.Fingerprint,
                Assets = assets,
                IncludeDetails = true,
                Log = _problems.Enqueue,
            },
            cancellationToken);
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
