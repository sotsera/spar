namespace Spar;

/// <summary>What changed about a window's page that stopped answering (<c>docs/app-model.md</c> section 3.16).</summary>
public enum PageHangState
{
    /// <summary>
    /// The page stopped answering: three heartbeats in a row went unanswered (about 15 to 20 s), the web engine reported
    /// it unresponsive, or it did not answer a close request within about 5 s. Spar shows its dialog.
    /// </summary>
    Unresponsive,

    /// <summary>The page answered again; Spar closed its dialog.</summary>
    Recovered,

    /// <summary>
    /// The page was loaded again: the user chose "Reload page", which ends the page's process first, or that process
    /// ended on its own and Spar reloaded the page.
    /// </summary>
    Reloaded,

    /// <summary>The window closed while its page did not answer, after the user chose "Close anyway".</summary>
    ClosedAnyway,
}

/// <summary>
/// A change of a window's page that stopped answering, as <c>spar.OnPageUnresponsive</c> receives it
/// (<c>docs/app-model.md</c> section 3.16), for logs and telemetry. The hook only observes: Spar's own dialog always
/// shows, and only the user's answer to it reloads the page or closes the window.
/// </summary>
/// <param name="Window">The window kind; <c>main</c> for the main window, as in <see cref="CloseRequest"/>.</param>
/// <param name="State">What changed.</param>
/// <param name="Duration">
/// How long the page had not answered at that moment, from the first heartbeat it missed (or from the engine's report
/// or the close request, when that came first). The hang itself began up to one heartbeat (about 5 s) earlier.
/// </param>
public sealed record PageHang(string Window, PageHangState State, TimeSpan Duration);
