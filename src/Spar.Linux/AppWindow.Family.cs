using Spar.AppWindows;
using Spar.Hosting;
using Spar.Linux.Interop;

namespace Spar.Linux;

/// <summary>
/// The window among the app's others (decision "M3d's decide-first questions", item 4): its place in the app's
/// <see cref="WindowFamily{TWindow}"/>, which closes the main window's others, and a window's own modals, before it;
/// and, for a modal, the window that owns it. GTK keeps a modal window's input from every other window of its window
/// group, so each window has a group of its own and a modal joins its owner's: the owner takes no input while the modal
/// is open, and the app's other windows stay usable. A modal is transient for its owner, so it stays above it.
/// Centring it on its owner on X11, where a window manager may place it anywhere, comes with the remembered placement.
/// </summary>
internal sealed partial class AppWindow : IFamilyWindow
{
    /// <summary>How long a teardown waits for the windows that close before it (their page's close work, then their calls).</summary>
    private const int OthersCloseMilliseconds = PageCloseWorkMilliseconds + CommandSettleMilliseconds;

    /// <summary>This window's modals that are open (loop thread).</summary>
    private readonly List<AppWindow> _modals = [];

    /// <summary>The window that owns this modal; null for any other window.</summary>
    private AppWindow? _modalOwner;

    /// <summary>The app's windows on this loop; null for a window alone (a test window without app windows).</summary>
    public WindowFamily<AppWindow>? Family { get; init; }

    OpenWindow? IFamilyWindow.Opened => _opened;

    /// <summary>
    /// Whether the window takes input: false while a modal of its own is shown, as GTK has it: modal, transient for this
    /// window, and in its window group.
    /// </summary>
    public bool TakesInput => _window != 0 && !_modals.Any(BlocksInput);

    Task IFamilyWindow.CloseNowAsync() => TeardownAsync(0);

    /// <summary>
    /// <see cref="Start"/> failed for a window of the app's kinds: its owner takes input again, and the window it may
    /// have created goes, so nothing of it is left on the screen.
    /// </summary>
    public void AbandonStart()
    {
        ReleaseModalOwner();
        _state = State.Closing;
        if (_window != 0)
        {
            Gtk.gtk_window_destroy(_window);
            _window = 0;
        }
    }

    /// <summary>
    /// The window's group, before it is shown: a group of its own, or, for a modal, its owner's, with the modal transient
    /// for it and modal. For a modal: the open window that showed it; null for any other window.
    /// </summary>
    private AppWindow? JoinGroup()
    {
        if (_opened is not { IsModal: true } opened)
        {
            var group = Gtk.gtk_window_group_new();
            Gtk.gtk_window_group_add_window(group, _window);
            GLib.g_object_unref(group);
            return null;
        }

        if (Family?.Find(opened.OpenerId) is not { IsClosing: false, Handle: not 0 } owner)
        {
            throw new InvalidOperationException($"The window that showed the modal {opened.Id} ({opened.OpenerId}) is closing.");
        }

        Gtk.gtk_window_group_add_window(Gtk.gtk_window_get_group(owner.Handle), _window);
        Gtk.gtk_window_set_transient_for(_window, owner.Handle);
        Gtk.gtk_window_set_modal(_window, 1);
        _modalOwner = owner;
        owner._modals.Add(this);
        return owner;
    }

    /// <summary>This modal is gone: its owner no longer counts it.</summary>
    private void ReleaseModalOwner()
    {
        _modalOwner?._modals.Remove(this);
        _modalOwner = null;
    }

    private bool BlocksInput(AppWindow modal) =>
        modal._window != 0
        && Gtk.gtk_window_get_modal(modal._window) != 0
        && Gtk.gtk_window_get_transient_for(modal._window) == _window
        && Gtk.gtk_window_get_group(modal._window) == Gtk.gtk_window_get_group(_window);

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
