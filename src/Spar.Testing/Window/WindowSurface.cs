using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spar.Hosting;

namespace Spar.Testing;

/// <summary>
/// The Window surface of one test app (plan section 5, level 5): what the test host asks of the desktop host, with the
/// page driver's engine and events the host installs before the window's first navigation. The session then closes
/// the window before the app's services are disposed.
/// </summary>
internal sealed class WindowSurface : IAsyncDisposable
{
    /// <summary>How long the window may take to show its first page: the first window of a run starts the browser.</summary>
    private static readonly TimeSpan OpenLimit = TimeSpan.FromSeconds(30);

    private readonly SparTestOptions _options;
    private readonly ProblemLog _problems = new();
    private readonly PageSession _session = new();
    private readonly Lock _gate = new();
    private readonly List<SparAppWindow> _appWindows = [];
    private TaskCompletionSource _appWindowAdded = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public WindowSurface(SparTestOptions options)
    {
        _options = options;
        options.ServiceActions.Insert(0, services => services.AddSingleton<ILoggerProvider>(_problems));
        Request = new SparTestWindowRequest
        {
            ProfilesFolder = TestDataRoots.Profiles,
            HomesFolder = TestDataRoots.LinuxHomes,
            Automation = options.Scene is { } scene
                ? _session.Automation with { PageScript = scene.PageScript, Viewport = scene.Viewport, ReducedMotion = true }
                : _session.Automation,
            OnPhase = options.WindowPhase,
            Holder = SparTestCleanup.CurrentTest() ?? (object)this,
            Dark = options.ColorScheme switch
            {
                SparColorScheme.Dark => true,
                SparColorScheme.System => null,
                _ => false,
            },
            ForAppWindow = ForAppWindow,
        };
    }

    public SparTestWindowRequest Request { get; }

    /// <summary>What Spar logged as a warning or an error for the app (<see cref="ProblemLog"/>).</summary>
    public IReadOnlyCollection<string> Problems => _problems.Problems;

    /// <summary>
    /// Waits for the window's first page, unless the test asked not to, and returns the page to drive; without the
    /// wait, the page answers once the window got as far as its first document.
    /// </summary>
    public async Task<SparPage?> StartAsync(ISparTestWindow window, CancellationToken cancellationToken)
    {
        _session.Attach(window);
        if (_options.WaitForPage)
        {
            await window.Opened.WaitAsync(OpenLimit, cancellationToken);
        }

        return new SparPage(_session);
    }

    /// <summary>The windows the app's page has opened so far, open or closed, in the order they were made.</summary>
    public IReadOnlyList<SparAppWindow> AppWindows
    {
        get
        {
            lock (_gate)
            {
                return [.. _appWindows];
            }
        }
    }

    /// <summary>
    /// The <paramref name="index"/>th window of <paramref name="kind"/> the app opened (0 for the first), once its first
    /// page has loaded; waits for the app to open it.
    /// </summary>
    public async Task<SparAppWindow> AppWindowAsync(string kind, int index, CancellationToken cancellationToken)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(OpenLimit);
        while (true)
        {
            Task added;
            SparAppWindow? found;
            lock (_gate)
            {
                found = _appWindows.Where(window => window.Kind == kind).ElementAtOrDefault(index);
                added = _appWindowAdded.Task;
            }

            if (found is not null)
            {
                await found.Window.Opened.WaitAsync(OpenLimit, cancellationToken);
                return found;
            }

            try
            {
                await added.WaitAsync(limit.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"The app opened no window '{kind}' number {index + 1} within {OpenLimit.TotalSeconds:0} s: does its page call windows.open(\"{kind}\")?");
            }
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>A window the app's page opens: a page driver of its own, attached once the host made the window.</summary>
    private (SparTestWindowRequest Request, Action<ISparTestWindow> Opened) ForAppWindow(string kind, string id)
    {
        var session = new PageSession();
        var request = new SparTestWindowRequest
        {
            ProfilesFolder = Request.ProfilesFolder,
            HomesFolder = Request.HomesFolder,
            Automation = session.Automation,
            Holder = Request.Holder,
            Dark = Request.Dark,
            ForAppWindow = ForAppWindow,
        };
        return (request, window =>
        {
            session.Attach(window);
            lock (_gate)
            {
                _appWindows.Add(new SparAppWindow(kind, id, new SparPage(session), new SparWindow(window)));
                var added = _appWindowAdded;
                _appWindowAdded = new(TaskCreationOptions.RunContinuationsAsynchronously);
                added.TrySetResult();
            }
        });
    }
}
