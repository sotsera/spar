using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Spar.Testing;

public enum SparTestSurface
{
    /// <summary>No page: tests call commands through the bridge.</summary>
    None,

    /// <summary>The page and bridge on 127.0.0.1, for a browser the test drives.</summary>
    Loopback,

    /// <summary>The app's real window (WebView2 or WebKitGTK), on the test process's UI thread.</summary>
    Window,
}

public sealed class SparTestOptions
{
    internal List<Action<IServiceCollection>> ServiceActions { get; } = [];

    internal HashSet<Type> AllowedExternals { get; } = [];

    public SparTestSurface Surface { get; set; } = SparTestSurface.None;

    /// <summary>Keep the app's data root after the test (it is deleted by default).</summary>
    public bool KeepData { get; set; }

    /// <summary>Runs after the app's registrations and Spar's test defaults.</summary>
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

/// <summary>An app started from its own entry point inside the test process.</summary>
public sealed class SparTestApp : IAsyncDisposable
{
    private readonly SparTestSession _session;
    private readonly Task<int> _exit;
    private readonly SparTestOptions _options;
    private IAsyncDisposable? _surface;

    private SparTestApp(SparTestSession session, Task<int> exit, SparRuntime runtime, SparTestOptions options)
    {
        _session = session;
        _exit = exit;
        Runtime = runtime;
        _options = options;
    }

    public IServiceProvider Services => Runtime.Services;

    public MemorySparLog Log => _session.Log;

    public SparFolders Folders => Runtime.Folders;

    /// <summary>The page's address on the loopback surface (with its token).</summary>
    public string? Url { get; private set; }

    public TimeSpan StartedAfter { get; private set; }

    /// <summary>The web engine processes the window started (Windows reports them; Linux finds them as children).</summary>
    public IReadOnlyList<int> EngineProcessIds => (_surface as ISparTestWindow)?.EngineProcessIds ?? [];

    internal SparRuntime Runtime { get; }

    /// <summary>
    /// Runs <paramref name="entryPoint"/> (the app's own <c>Program.cs</c>) on a thread of its own. Its
    /// <c>SparApplication.Create</c> sees this test's session; its <c>Run</c> builds the app and waits for disposal.
    /// </summary>
    public static async Task<SparTestApp> StartAsync(
        Func<string[], int> entryPoint, Action<SparTestOptions>? configure = null, CancellationToken cancellationToken = default)
    {
        var clock = Stopwatch.StartNew();
        var options = new SparTestOptions();
        configure?.Invoke(options);

        var session = new SparTestSession
        {
            Folders = TestDataRoots.Next(),
            Log = new MemorySparLog(),
            ConfigureServices = services => options.ServiceActions.ForEach(action => action(services)),
            AllowedExternals = options.AllowedExternals,
        };

        var exit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        SparTestSession.Current = session;
        var thread = new Thread(() =>
        {
            try
            {
                exit.TrySetResult(entryPoint([]));
            }
            catch (Exception exception)
            {
                exit.TrySetException(exception);
            }
        })
        { Name = "Spar app (test)", IsBackground = true };
        thread.Start(); // The thread captures the execution context, so it sees this session and no other.
        SparTestSession.Current = null;

        var first = await Task.WhenAny(session.Started, exit.Task).WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        if (first == exit.Task)
        {
            throw new InvalidOperationException($"The app's entry point returned {await exit.Task} without calling Run.");
        }

        var app = new SparTestApp(session, exit.Task, await session.Started, options);
        await app.OpenSurfaceAsync(cancellationToken);
        app.StartedAfter = clock.Elapsed;
        return app;
    }

    /// <summary>Sends one bridge message and returns the reply, exactly as a page would.</summary>
    public Task<string> SendAsync(string message, CancellationToken cancellationToken = default) =>
        Runtime.Bridge.HandleAsync(message, cancellationToken);

    /// <summary>Runs the body of an async function in the window's page and returns its result as JSON.</summary>
    public Task<string> EvaluateAsync(string asyncFunctionBody, CancellationToken cancellationToken = default) =>
        _surface is ISparTestWindow window
            ? window.EvaluateAsync(asyncFunctionBody, cancellationToken)
            : throw new InvalidOperationException("Only the Window surface runs page script; use a browser for Loopback.");

    /// <summary>Spike: how long each step of the last dispose took.</summary>
    public static string LastDisposeTimings { get; private set; } = "";

    public async ValueTask DisposeAsync()
    {
        var clock = Stopwatch.StartNew();
        if (_surface is not null)
        {
            await _surface.DisposeAsync();
        }

        var surface = clock.Elapsed.TotalMilliseconds;
        _session.Stop();
        var code = await _exit.WaitAsync(TimeSpan.FromSeconds(10));
        var app = clock.Elapsed.TotalMilliseconds;
        if (!_options.KeepData)
        {
            TestDataRoots.Delete(Folders);
        }

        LastDisposeTimings = $"surface={surface:F0}ms app={app - surface:F0}ms delete={clock.Elapsed.TotalMilliseconds - app:F0}ms";

        if (code != 0)
        {
            throw new InvalidOperationException($"The app exited with {code}.");
        }
    }

    private async Task OpenSurfaceAsync(CancellationToken cancellationToken)
    {
        switch (_options.Surface)
        {
            case SparTestSurface.Loopback:
                var loopback = await LoopbackSurface.StartAsync(Runtime, cancellationToken);
                Url = loopback.Url;
                _surface = loopback;
                break;
            case SparTestSurface.Window:
                _surface = await SparTestWindows.OpenAsync(Runtime, cancellationToken);
                break;
        }
    }
}
