using Spar.Hosting;

namespace Spar.AppWindows;

/// <summary>One of an app's windows as <see cref="WindowFamily{TWindow}"/> sees it: the host's own window type.</summary>
internal interface IFamilyWindow
{
    /// <summary><c>main</c>, or the window's id (<c>settings-1</c>).</summary>
    string Id { get; }

    /// <summary>The window of one of the app's kinds; null for the main window.</summary>
    OpenWindow? Opened { get; }

    /// <summary>Whether its teardown has started.</summary>
    bool IsClosing { get; }

    /// <summary>Its own two-stage close: its page's guard, then the backend's with its kind in the request.</summary>
    Task<CloseReport> RequestCloseAsync(CloseReason reason, bool automated, ICloseQuestions? questions);

    /// <summary>Its teardown, without asking its guards.</summary>
    Task CloseNowAsync();
}

/// <summary>
/// An app's open windows on one UI thread, the main window first (decision "M3d's decide-first questions", item 4),
/// and the order they close in: closing the main window closes every other one first, and closing any window closes
/// the modals it owns first, each through its own close request, the latest opened first, so a modal goes before the
/// window that owns it. A window that keeps itself open keeps the one being closed open too. UI thread only.
/// </summary>
internal sealed class WindowFamily<TWindow>
    where TWindow : class, IFamilyWindow
{
    private readonly List<TWindow> _windows = [];

    /// <summary>The windows of the app's kinds, in the order they opened.</summary>
    public IReadOnlyList<TWindow> Others => [.. _windows.Where(window => window.Opened is not null)];

    public int Count => _windows.Count;

    public void Add(TWindow window) => _windows.Add(window);

    public bool Remove(TWindow window) => _windows.Remove(window);

    public TWindow? Find(string id) => _windows.FirstOrDefault(window => window.Id == id);

    /// <summary>What closes before <paramref name="window"/>: every other window for the main one, a window's own modals otherwise.</summary>
    public IReadOnlyList<TWindow> ClosesFirst(TWindow window)
    {
        var dependents = window.Opened is null
            ? _windows.Where(other => other.Opened is not null)
            : _windows.Where(other => other.Opened is { IsModal: true } opened && opened.OpenerId == window.Id);
        return [.. dependents.Reverse()];
    }

    /// <summary>
    /// The windows that close before <paramref name="window"/>, each through its own close request with the same
    /// reason and answers; the report of the first that stayed open, or null once every one closed.
    /// </summary>
    public async Task<(TWindow Window, CloseReport Report)?> CloseFirstAsync(
        TWindow window, CloseReason reason, bool automated, ICloseQuestions? questions)
    {
        foreach (var other in ClosesFirst(window))
        {
            if (other.IsClosing)
            {
                continue;
            }

            var report = await other.RequestCloseAsync(reason, automated, questions);
            if (!report.Closed)
            {
                return (other, report);
            }
        }

        return null;
    }

    /// <summary>The teardown of every window that closes before <paramref name="window"/>, without their guards.</summary>
    public Task TearDownFirst(TWindow window) => Task.WhenAll(ClosesFirst(window).Select(other => other.CloseNowAsync()));
}
