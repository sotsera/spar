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

    public WindowSurface(SparTestOptions options)
    {
        _options = options;
        options.ServiceActions.Insert(0, services => services.AddSingleton<ILoggerProvider>(_problems));
        Request = new SparTestWindowRequest
        {
            ProfilesFolder = TestDataRoots.Profiles,
            HomesFolder = TestDataRoots.LinuxHomes,
            Automation = _session.Automation,
            OnPhase = options.WindowPhase,
            Holder = SparTestCleanup.CurrentTest() ?? (object)this,
            Dark = options.ColorScheme switch
            {
                SparColorScheme.Dark => true,
                SparColorScheme.System => null,
                _ => false,
            },
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

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
