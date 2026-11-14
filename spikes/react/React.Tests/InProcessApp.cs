using System.Diagnostics;
using Loop.Loopback;
using Microsoft.Extensions.DependencyInjection;

namespace React.Tests;

/// <summary>
/// Spike of an in-process test factory: runs the app's real entry point inside the test process with
/// service overrides, and starts its loopback host on a real 127.0.0.1 port that a browser can drive.
/// </summary>
public sealed class InProcessApp : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop;
    private readonly Task<int> _run;

    private InProcessApp(CancellationTokenSource stop, Task<int> run, LoopbackStarted started, TimeSpan readyAfter)
    {
        _stop = stop;
        _run = run;
        Url = started.Url;
        Services = started.Services ?? throw new InvalidOperationException("The app did not expose its services.");
        ReadyAfter = readyAfter;
    }

    public string Url { get; }

    /// <summary>The app's own service provider: tests can inspect backend state directly.</summary>
    public IServiceProvider Services { get; }

    public TimeSpan ReadyAfter { get; }

    public static async Task<InProcessApp> StartAsync(Action<IServiceCollection>? configureServices, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var stop = new CancellationTokenSource();
        var started = new TaskCompletionSource<LoopbackStarted>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entryPoint = typeof(React.App.NotesService).Assembly.EntryPoint
            ?? throw new InvalidOperationException("The app has no entry point.");

        // The hook is an AsyncLocal: setting it here flows into the task below, and only there.
        SparTestHooks.Current = new SparTestHooks.Hooks
        {
            ConfigureServices = configureServices,
            Started = s => started.TrySetResult(s),
            Stop = stop.Token,
        };
        var run = Task.Run(() => (int)entryPoint.Invoke(null, [new[] { "--loopback" }])!, CancellationToken.None);
        SparTestHooks.Current = null;

        var first = await Task.WhenAny(started.Task, run).WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        if (first == run)
        {
            throw new InvalidOperationException($"The app's entry point returned {await run} before it started.");
        }

        return new InProcessApp(stop, run, await started.Task, stopwatch.Elapsed);
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        var exitCode = await _run.WaitAsync(TimeSpan.FromSeconds(10));
        _stop.Dispose();
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"The app exited with {exitCode}.");
        }
    }
}

/// <summary>A clock that always reads the same instant.</summary>
public sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
