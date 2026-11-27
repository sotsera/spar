using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Spar.Files;
using Spar.Hosting;
using Spar.Runtime;

namespace Spar.Testing;

/// <summary>What sits in front of the app's backend in a test.</summary>
public enum SparTestSurface
{
    /// <summary>No page: the test calls commands through the typed test clients.</summary>
    None,

    /// <summary>
    /// The page in an ordinary browser over in-process loopback, from the <c>Spar.Testing.Browser</c> package: pass the
    /// page with <c>o.Page(folder)</c> and open <c>app.PageUrl()</c> with Playwright. End-to-end projects only.
    /// </summary>
    Browser,

    /// <summary>
    /// The app's real window, as users get it, in the test process: the app's own packed page in its WebView2 window
    /// (WebKitGTK on Linux), offscreen and never activated, driven through <c>app.Page</c>. End-to-end projects only;
    /// window tests run as their own invocation (plan section 5).
    /// </summary>
    Window,
}

/// <summary>A test's changes to the app.</summary>
public sealed class SparTestOptions
{
    internal List<Action<IServiceCollection>> ServiceActions { get; } = [];

    internal HashSet<Type> AllowedExternals { get; } = [];

    /// <summary>The page's folders for the Browser surface, which <c>Spar.Testing.Browser</c> fills.</summary>
    internal List<(string Prefix, string Folder)> PageFolders { get; } = [];

    /// <summary>Starts the Browser surface; set by <c>Spar.Testing.Browser</c>, so this package carries no web server.</summary>
    internal Func<SparTestApp, CancellationToken, Task<IAsyncDisposable>>? StartBrowser { get; set; }

    /// <summary>The Window surface's start-up and shutdown phases as the host reaches them, for Spar's own host tests (UI thread).</summary>
    internal Action<string>? WindowPhase { get; set; }

    /// <summary>Whether <c>StartAsync</c> waits for the window's first page; Spar's host tests that close it earlier do not.</summary>
    internal bool WaitForPage { get; set; } = true;

    /// <summary>A scene's page settings (<see cref="SparScene"/>): its clock script and fixed viewport; null for a test.</summary>
    internal ScenePage? Scene { get; set; }

    /// <summary>Called with the built app before its window opens (a scene's call recorder).</summary>
    internal Action<SparRunningApp>? Built { get; set; }

    public SparTestSurface Surface { get; set; } = SparTestSurface.None;

    /// <summary>
    /// The Window surface's colour scheme: <see cref="SparColorScheme.Light"/> by default, so the page's
    /// <c>prefers-color-scheme</c> and the window's own colours, and so its screenshots, are the same on every machine.
    /// </summary>
    public SparColorScheme ColorScheme { get; set; } = SparColorScheme.Light;

    /// <summary>Keep the app's data root after the test (it is deleted by default).</summary>
    public bool KeepData { get; set; }

    /// <summary>Changes the app's services. Runs after the app's registrations and Spar's defaults.</summary>
    public SparTestOptions Services(Action<IServiceCollection> configure)
    {
        ServiceActions.Add(configure);
        return this;
    }

    /// <summary>
    /// The app's clock: every <see cref="TimeProvider"/> the app resolves, and Spar's own (the bridge's timers, the log's
    /// timestamps), is <paramref name="time"/>. Pass a fake to control time.
    /// </summary>
    public SparTestOptions Time(TimeProvider time)
    {
        ServiceActions.Add(services => services.Replace(ServiceDescriptor.Singleton(time)));
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
    private readonly SparTestLog _log;
    private readonly SparStreamClient _streams;
    private readonly BlobOwner _blobs;
    private SparPage? _page;
    private SparWindow? _window;
    private long _nextCallId;

    private SparTestApp(SparTestSession session, Task<int> exit, SparRunningApp app, SparTestOptions options, SparTestLog log, SparTestFileDialogs dialogs)
    {
        FileDialogs = dialogs;
        _session = session;
        _exit = exit;
        _app = app;
        _options = options;
        _log = log;
        _streams = new SparStreamClient(app.Dispatcher, () => Interlocked.Increment(ref _nextCallId));

        // The typed clients read the handles a result gives them from the app's store, held as a page would hold them.
        _blobs = app.Dispatcher.Blobs.Open(app.Dispatcher.Limits.MaxBlobBytes, app.Dispatcher.Limits.MaxBlobs);

        // The test's own calls come from a window of their own: its grants, and the test's answers to its dialogs.
        _blobs.Window = new HostWindow(app.Dispatcher.Time, dialogs);
        app.Dispatcher.Blobs.StartTest();
    }

    /// <summary>The app's own service provider: a test can inspect backend state directly.</summary>
    public IServiceProvider Services => _app.Services;

    /// <summary>Every folder of this app instance, all under its own data root.</summary>
    public SparFolders Folders => _app.Folders;

    /// <summary>The contracts' fingerprint, as a page would see it in <c>open</c>.</summary>
    public string Fingerprint => _app.Catalog.Fingerprint;

    /// <summary>
    /// The app's file dialogs, every window's: none reaches the screen, and each takes the next answer queued here, as
    /// <c>app.FileDialogs.Choose(path)</c> or <c>Dismiss()</c>.
    /// </summary>
    public SparTestFileDialogs FileDialogs { get; }

    public TimeSpan StartedAfter { get; private set; }

    /// <summary>
    /// What Spar logged for this app instance, every level, in order: a command that threw (with its exception and
    /// correlation id), a refused call, a result over the limit. The newest <see cref="SparTestLog.Capacity"/> entries
    /// are kept; <see cref="LogDropped"/> counts the older ones. The app's own logging goes where the app sends it.
    /// </summary>
    public IReadOnlyList<SparLogEntry> Log => _log.Entries;

    /// <summary>How many of the oldest entries <see cref="Log"/> no longer holds.</summary>
    public int LogDropped => _log.Dropped;

    /// <summary>The app's clock (<see cref="SparTestOptions.Time"/>, or the system's).</summary>
    public TimeProvider Time => _app.Dispatcher.Time;

    /// <summary>What the bridge refused or dropped, and the page's CSP violations, so far. A clean run has none.</summary>
    public IReadOnlyList<string> Problems => Surface is WindowSurface window ? [.. _problems, .. window.Problems] : [.. _problems];

    /// <summary>
    /// The page in the app's window, for the Window surface: locators, trusted input, screenshots and assertions
    /// (<see cref="SparAssertions"/>), with the method names of Microsoft's browser automation library.
    /// </summary>
    public SparPage Page => _page ?? throw new InvalidOperationException(
        "The app has no window: start it with o.Surface = SparTestSurface.Window to drive its page.");

    /// <summary>The surface in front of the backend, when it is not <see cref="SparTestSurface.None"/>.</summary>
    internal IAsyncDisposable? Surface { get; private set; }

    internal SparRunningApp RunningApp => _app;

    internal SparTestOptions Options => _options;

    /// <summary>
    /// The app's window, for the Window surface: <c>CloseAsync</c> closes it through its close request, as the close
    /// button does, so the page's and the backend's close guards run.
    /// </summary>
    public SparWindow Window => _window ??= _session.OpenedWindow is { } opened
        ? new SparWindow(opened)
        : throw new InvalidOperationException("The app has no window: start it with o.Surface = SparTestSurface.Window to close it.");

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
        RequireSurfaceTrait(options.Surface);

        SparTestLog? log = null;
        SparTestApp? started = null;
        var dialogs = new SparTestFileDialogs(problem => started?.ReportProblem(problem));
        var window = options.Surface == SparTestSurface.Window ? new WindowSurface(options) : null;
        var session = new SparTestSession
        {
            DataRoot = TestDataRoots.Next(),
            ConfigureServices = services =>
            {
                // Spar's fakes first, so a test's own replacement wins.
                services.AddSingleton<INativeFileDialogs>(dialogs);
                options.ServiceActions.ForEach(action => action(services));
            },
            AllowedExternals = options.AllowedExternals,
            CreateLog = time => log = new SparTestLog(time),
            Window = window?.Request,
            Built = options.Built,
        };

        var exit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        SparTestSession.Current = session;
        var thread = new Thread(() => RunEntryPoint<TApp>(exit)) { Name = "Spar app (test)", IsBackground = true };
        thread.Start(); // The thread captures the execution context, so it sees this session and no other.
        SparTestSession.Current = null;

        var first = await Task.WhenAny(session.Started, exit.Task).WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);

        // Run completes Started before it returns, also when the app fails to build (it then returns 70). Both tasks
        // run their continuations asynchronously, so WhenAny can still pick the exit: a completed Started means Run was
        // called, and its outcome, the build failure included, is the answer.
        if (first == exit.Task && !session.Started.IsCompleted)
        {
            throw new InvalidOperationException($"The app's Program.cs returned {await exit.Task} without calling Run.");
        }

        var running = await session.Started;
        var testApp = new SparTestApp(session, exit.Task, running, options, log!, dialogs);
        started = testApp;
        try
        {
            if (options.Surface == SparTestSurface.Browser)
            {
                await testApp.StartBrowserSurfaceAsync(cancellationToken);
            }

            if (window is not null)
            {
                testApp.Surface = window;
                testApp._page = await window.StartAsync(session.OpenedWindow!, cancellationToken);
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
        return _app.Dispatcher.InvokeAsync(contract, command, callId, request, _blobs, cancellationToken, SparCaller.Test);
    }

    /// <summary>Loads a resource through the app's real dispatch, as one load of its address would.</summary>
    public async ValueTask<SparBlob?> LoadResourceAsync(string contract, string command, ReadOnlyMemory<byte> request, CancellationToken cancellationToken)
    {
        var (outcome, blob) = await _app.Dispatcher.InvokeResourceAsync(contract, command, request, cancellationToken, SparCaller.Test);
        return outcome.Failure is { } failure ? throw new SparCallFailedException(contract, command, failure) : blob;
    }

    SparStreamClient ISparInvoker.Streams => _streams;

    /// <summary>
    /// Drops the channel, as a lost connection would: the typed test clients' open streams and subscriptions end, and, on
    /// the Browser surface, every page's loopback channel; none opens until <see cref="RestoreChannel"/>. An automatic
    /// subscription reports <c>Reconnecting</c>, subscribes again once the channel is back, within 30 s, and reports
    /// <c>Reset</c> and <c>Resumed</c>; a stream, a manual subscription and a call that was running end lost.
    /// </summary>
    public void DropChannel()
    {
        _streams.Drop();
        (Surface as IChannelSurface)?.DropChannel();
    }

    /// <summary>Brings the channel back after <see cref="DropChannel"/>.</summary>
    public void RestoreChannel()
    {
        _streams.Restore();
        (Surface as IChannelSurface)?.RestoreChannel();
    }

    /// <summary>
    /// Stops the surface and the app, and deletes the data root. Every step runs even when an earlier one fails. A
    /// failure is kept for the test framework's hook when the test project has one (<see cref="SparTestCleanup"/>), so
    /// it is reported beside the test's own failure instead of replacing it; without the hook, it is thrown.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        if (Surface is not null)
        {
            await Step(failures, async () => await Surface.DisposeAsync());
        }

        _blobs.Close();
        _blobs.Window?.Close();
        _app.Dispatcher.Blobs.EndTest();
        _session.Stop();
        await Step(failures, async () =>
        {
            // A window's teardown settles its calls and checks its renderer processes are gone, within about 10 s.
            var limit = _session.Window is null ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(30);
            var code = await _exit.WaitAsync(limit);
            if (code != 0)
            {
                throw new InvalidOperationException($"The app exited with {code}.");
            }
        });

        if (!_options.KeepData)
        {
            await Step(failures, () =>
            {
                TestDataRoots.Delete(_app.Folders.DataRoot);
                return Task.CompletedTask;
            });
        }

        if (failures.Count == 0)
        {
            return;
        }

        var failure = failures.Count == 1 ? failures[0] : new AggregateException("The app did not stop cleanly.", failures);
        if (!SparTestCleanup.TryKeep(failure))
        {
            throw failure;
        }
    }

    private static async Task Step(List<Exception> failures, Func<Task> step)
    {
        try
        {
            await step();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    /// <summary>A surface reports what its bridge refused or dropped here.</summary>
    internal void ReportProblem(string problem) => _problems.Enqueue(problem);

    private async Task StartBrowserSurfaceAsync(CancellationToken cancellationToken)
    {
        if (_options.StartBrowser is null)
        {
            throw new InvalidOperationException(
                "The Browser surface comes from the Spar.Testing.Browser package: reference it and call o.Page() for the app's page.");
        }

        Surface = await _options.StartBrowser(this, cancellationToken);
    }

    /// <summary>
    /// A test that starts the Window or Browser surface carries the matching trait, <c>[Trait("Surface", "Window")]</c>
    /// or <c>[Trait("Surface", "Browser")]</c>, on the method, its class or the assembly: the trait keeps it out of the
    /// fast invocation and runs it in its own (decision "M1d step 4's shape: tests in the template"). The test's traits
    /// come from the test framework's hook; an app started where no hook watches (no hook, or a fixture) is not checked.
    /// </summary>
    private static void RequireSurfaceTrait(SparTestSurface surface)
    {
        var name = surface switch
        {
            SparTestSurface.Window => "Window",
            SparTestSurface.Browser => "Browser",
            _ => null,
        };
        if (name is null || SparTestCleanup.CurrentSurfaces() is not { } surfaces
            || surfaces.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var trait = SparTestCleanup.SurfaceTrait;
        throw new InvalidOperationException(
            $"This test starts the {name} surface but has no [Trait(\"{trait}\", \"{name}\")]. Add the trait to the test, its class "
            + $"or its assembly, so that --filter-trait \"{trait}={name}\" runs it with the other {name.ToLowerInvariant()} tests as "
            + $"their own invocation and --filter-not-trait \"{trait}={name}\" keeps it out of the fast tests.");
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
