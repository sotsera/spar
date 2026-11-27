using Spar.AppWindows;
using Spar.Hosting;
using Windows.Win32;

namespace Spar.Windows;

/// <summary>
/// The window among the app's others (decision "M3d's decide-first questions", item 4): its place in the app's
/// <see cref="WindowFamily{TWindow}"/>, which closes the main window's others, and a window's own modals, before it;
/// and, for a modal, the window that owns it. A modal is an owned window of its opener, so it stays above it and has no
/// taskbar button of its own, and the opener is disabled (<c>EnableWindow</c>) while any modal of its own is open.
/// </summary>
internal sealed partial class AppWindow : IFamilyWindow
{
    /// <summary>How long a teardown waits for the windows that close before it (their page's close work, then their calls).</summary>
    private const int OthersCloseMilliseconds = PageCloseWorkMilliseconds + CommandSettleMilliseconds;

    /// <summary>The window that owns this modal; null for any other window.</summary>
    private AppWindow? _modalOwner;

    /// <summary>How many modals of this window's own are open.</summary>
    private int _modalsOpen;

    /// <summary>The app's windows on this UI thread; null for a window alone (a test window without app windows).</summary>
    public WindowFamily<AppWindow>? Family { get; init; }

    OpenWindow? IFamilyWindow.Opened => _opened;

    /// <summary>Whether the window takes input: false while a modal of its own is open (<c>IsWindowEnabled</c>).</summary>
    public bool TakesInput => !_window.IsNull && PInvoke.IsWindowEnabled(_window);

    Task IFamilyWindow.CloseNowAsync() => TeardownAsync(0);

    /// <summary>For a modal: the open window that showed it, which owns it; null for any other window.</summary>
    private AppWindow? FindModalOwner()
    {
        if (_opened is not { IsModal: true } opened)
        {
            return null;
        }

        return Family?.Find(opened.OpenerId) is { IsClosing: false } owner
            ? owner
            : throw new InvalidOperationException($"The window that showed the modal {opened.Id} ({opened.OpenerId}) is closing.");
    }

    /// <summary>A modal of this window's own is shown: the window takes no input until the last one closes.</summary>
    private void ModalOpened()
    {
        _modalsOpen++;
        if (_modalsOpen == 1 && !_destroyed)
        {
            PInvoke.EnableWindow(_window, false);
        }
    }

    /// <summary>
    /// A modal of this window's own closed: the last one gives the window its input back, before the modal is
    /// destroyed, so the activation returns to it rather than to another app.
    /// </summary>
    private void ModalClosed()
    {
        _modalsOpen--;
        if (_modalsOpen == 0 && !_destroyed)
        {
            PInvoke.EnableWindow(_window, true);
        }
    }

    /// <summary>
    /// <see cref="Start"/> failed for a window of the app's kinds: its owner takes input again, and the window it may
    /// have created goes, so nothing of it is left on the screen.
    /// </summary>
    public void AbandonStart()
    {
        ReleaseModalOwner();
        _state = State.Closing;
        if (!_window.IsNull && !_destroyed)
        {
            PInvoke.DestroyWindow(_window);
        }
    }

    /// <summary>This modal is closing: its owner takes input again (once).</summary>
    private void ReleaseModalOwner()
    {
        var owner = _modalOwner;
        _modalOwner = null;
        owner?.ModalClosed();
    }

    /// <summary>
    /// Before the window's own guards: the windows that close first, each through its own close request; the report of
    /// one that stayed open, which keeps this window open too.
    /// </summary>
    private async Task<CloseReport?> CloseOthersFirstAsync(CloseReason reason, bool automated, ICloseQuestions? questions)
    {
        if (Family?.CloseFirstAsync(this, reason, automated, questions) is not { } closing || await closing is not { } kept)
        {
            return null;
        }

        _log.Step($"close kept: the window {kept.Window.Id} stayed open");
        return new CloseReport(closed: false, kept.Report.Question, kept.Report.Unanswered);
    }
}
