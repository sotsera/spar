using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spar.Hosting;

namespace Spar.Testing;

/// <summary>
/// The Window surface of one test app (plan section 5, level 5): what the test host asks of the desktop host, and the
/// page driver it installs before the window's first navigation. Disposing it stops the driver; the session then
/// closes the window before the app's services are disposed.
/// </summary>
internal sealed class WindowSurface : IAsyncDisposable
{
    /// <summary>How long the window may take to show its first page: the first window of a run starts the browser.</summary>
    private static readonly TimeSpan OpenLimit = TimeSpan.FromSeconds(30);

    private readonly SparTestOptions _options;
    private readonly ProblemLog _problems = new();
    private PageSession? _session;

    public WindowSurface(SparTestOptions options)
    {
        _options = options;
        options.ServiceActions.Insert(0, services => services.AddSingleton<ILoggerProvider>(_problems));
        Request = new SparTestWindowRequest
        {
            ProfilesFolder = TestDataRoots.Profiles,
            BeforeFirstNavigation = InstallAsync,
            OnPhase = options.WindowPhase,
        };
    }

    public SparTestWindowRequest Request { get; }

    /// <summary>What Spar logged as a warning or an error for the app (<see cref="ProblemLog"/>).</summary>
    public IReadOnlyCollection<string> Problems => _problems.Problems;

    /// <summary>
    /// Waits for the window's first page, unless the test asked not to, and returns the page to drive. Without the
    /// wait the page exists only once the window got as far as its first navigation.
    /// </summary>
    public async Task<SparPage?> StartAsync(ISparTestWindow window, CancellationToken cancellationToken)
    {
        if (_options.WaitForPage)
        {
            await window.Opened.WaitAsync(OpenLimit, cancellationToken);
        }

        return _session is { } session ? new SparPage(session) : null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_session is { } session)
        {
            await session.DisposeAsync();
        }
    }

    private async Task InstallAsync(ISparTestWindow window) => _session = await PageSession.InstallAsync(window);
}
