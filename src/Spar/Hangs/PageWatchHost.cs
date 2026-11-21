namespace Spar.Hosting;

/// <summary>
/// A two-button native prompt of a host (plan section 3, "Hung pages"; the shutdown hang policy): the hung-page dialog
/// ("Wait / Reload page", "Wait / Close anyway") and the shutdown prompt ("Keep waiting / Exit now"). The first button
/// is the default, and closing the prompt (Esc, its close button) chooses it, so a prompt dismissed by the user never
/// reloads, closes or exits. The host adds its window's title.
/// </summary>
/// <param name="Kind">Which prompt it is, so a host can keep stable button ids for automation.</param>
/// <param name="Instruction">The prompt's main line.</param>
/// <param name="Content">What the choice does.</param>
/// <param name="First">The default button: Wait or Keep waiting.</param>
/// <param name="Second">The other button: Reload page, Close anyway or Exit now.</param>
internal readonly record struct HostPromptRequest(HostPromptKind Kind, string Instruction, string Content, string First, string Second);

/// <summary>Which prompt a <see cref="HostPromptRequest"/> is.</summary>
internal enum HostPromptKind
{
    /// <summary>"Wait / Reload page".</summary>
    HungPage,

    /// <summary>"Wait / Close anyway": a close met a hung page.</summary>
    HungPageClose,

    /// <summary>"Keep waiting / Exit now".</summary>
    Shutdown,
}

/// <summary>What the user chose in a <see cref="IHostPrompt"/>.</summary>
internal enum PromptAnswer
{
    /// <summary>The host took the prompt down before the user answered.</summary>
    Dismissed,

    /// <summary>The first button, or the prompt closed by the user.</summary>
    First,

    /// <summary>The second button.</summary>
    Second,
}

/// <summary>A prompt that is showing, which the host can take down.</summary>
internal interface IHostPrompt
{
    /// <summary>The user's answer; <see cref="PromptAnswer.Dismissed"/> after <see cref="Dismiss"/>.</summary>
    Task<PromptAnswer> Answer { get; }

    /// <summary>Takes the prompt down unless the user already answered. Safe from any thread.</summary>
    void Dismiss();
}

/// <summary>
/// What <see cref="PageWatch"/> needs from a host's window, its bridge and its view. Every member runs on the window's UI
/// thread except <see cref="Post"/>, which is safe from any thread. The host reports its side the other way, through
/// <see cref="PageWatch"/>'s methods: a document opened or closed, each answer, the engine's own report.
/// </summary>
internal interface IPageWatchHost
{
    /// <summary>Runs <paramref name="work"/> on the UI thread, after what is queued there, in a callback named <paramref name="name"/>.</summary>
    void Post(Action work, string name);

    /// <summary>Whether the page can be seen: its window shown and not minimised, and the view not hidden.</summary>
    bool IsPageVisible { get; }

    /// <summary>
    /// Sends the heartbeat's <c>ping</c> numbered <paramref name="seq"/> to the current document; false when there is
    /// none, or it does not answer pings.
    /// </summary>
    bool Ping(long seq);

    /// <summary>
    /// Asks the current document's close guards with <c>close-request</c>; false when there is no document, or its page
    /// did not list <c>close-request</c>.
    /// </summary>
    bool RequestClose(long seq, CloseReason reason, bool automated);

    /// <summary>The close request <paramref name="seq"/> ended without the page's answer: its guards' signal is aborted.</summary>
    void EndClose(long seq);

    /// <summary>"Reload page": ends the page's process, then reloads the page once, without using up the automatic reload.</summary>
    void ReloadHungPage();

    /// <summary>Shows a prompt owned by the window; it does not take the foreground.</summary>
    IHostPrompt ShowPrompt(HostPromptRequest request);
}
