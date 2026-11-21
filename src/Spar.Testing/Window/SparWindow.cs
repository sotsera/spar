using Spar.Hosting;

namespace Spar.Testing;

/// <summary>What a close request came to.</summary>
public enum CloseOutcome
{
    /// <summary>The guards agreed, or the answer closed: the window ran its teardown and is gone.</summary>
    Closed,

    /// <summary>A guard kept the window open, or the test's answer did.</summary>
    Kept,
}

/// <summary>What a window test answers in place of Spar's native dialogs during a close request.</summary>
public enum CloseAnswer
{
    /// <summary>"Close anyway": to a guard's question, and to "Wait / Close anyway" for a page that does not answer.</summary>
    CloseAnyway,

    /// <summary>
    /// "Keep open" to a guard's question; "Wait" to a page that does not answer, which keeps the request open until the
    /// page answers (asked again on the hung-page schedule).
    /// </summary>
    KeepOpen,
}

/// <summary>The colour scheme of a window test's window and page.</summary>
public enum SparColorScheme
{
    /// <summary>Light, whatever the machine's theme: the default, so screenshots match on every machine.</summary>
    Light,

    Dark,

    /// <summary>The machine's theme, or the app's saved choice, as the app's own window would show.</summary>
    System,
}

/// <summary>How <see cref="SparWindow.CloseAsync"/> closes the window.</summary>
public sealed class SparCloseOptions
{
    /// <summary>
    /// Whether the guards see the automated mode (true by default): <c>automated</c> on the page and
    /// <c>IsAutomated</c> in C#, where an ask counts as close and nothing is shown. False tests a guard's prompt: its
    /// question is answered by <see cref="Answer"/>, which must then be set.
    /// </summary>
    public bool Automated { get; set; } = true;

    /// <summary>What the test answers to a question, in place of the native dialog; required when <see cref="Automated"/> is false.</summary>
    public CloseAnswer? Answer { get; set; }

    /// <summary>
    /// Why the window closes, as the guards read it: <see cref="CloseReason.User"/> (the close button) by default;
    /// <see cref="CloseReason.App"/> or <see cref="CloseReason.SessionEnd"/> to test those guards. A session end runs
    /// its rules (no question, nothing keeps the window, the guards' 3 s), but not Windows' hold of <c>WM_ENDSESSION</c>.
    /// </summary>
    public CloseReason Reason { get; set; } = CloseReason.User;

    /// <summary>Milliseconds the request may take, its teardown included; 30 s when null.</summary>
    public float? Timeout { get; set; }
}

/// <summary>What <see cref="SparWindow.CloseAsync"/> came to.</summary>
public sealed class SparCloseResult
{
    internal SparCloseResult(CloseOutcome outcome, string? question, bool pageDidNotAnswer)
    {
        Outcome = outcome;
        Question = question;
        PageDidNotAnswer = pageDidNotAnswer;
    }

    public CloseOutcome Outcome { get; }

    /// <summary>
    /// The last question a guard asked, the page's or the backend's; null when nobody asked. In the automated mode it is
    /// the question that was not shown, since an ask counts as close there.
    /// </summary>
    public string? Question { get; }

    /// <summary>The page did not answer its close request in time, and "Wait / Close anyway" was answered.</summary>
    public bool PageDidNotAnswer { get; }

    public override string ToString() => $"{Outcome}{(Question is null ? "" : $", asked \"{Question}\"")}{(PageDidNotAnswer ? ", the page did not answer" : "")}";
}

/// <summary>
/// The app's window in a window test (<c>app.Window</c>): closing it as a person would, through its close request, so
/// the page's guard and the backend's run (decision "Window tests: closing, session-end budget, test theme and
/// any-character keys"). A window test never shows a dialog: an ask counts as close in the automated mode, and
/// otherwise takes the test's <see cref="SparCloseOptions.Answer"/>.
/// </summary>
public sealed class SparWindow
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private readonly ISparTestWindow _window;

    internal SparWindow(ISparTestWindow window)
    {
        _window = window;
    }

    /// <summary>Completes when the first navigation has completed, or fails with why the window could not open.</summary>
    internal Task Opened => _window.Opened;

    /// <summary>Completes with the window's exit code once its teardown has finished and its renderer is gone.</summary>
    internal Task<int> Closed => _window.Closed;

    /// <summary>
    /// Asks the window to close, as the close button does, and waits for the answer: the page's guard, then the
    /// backend's. <see cref="CloseOutcome.Closed"/> comes once the window is gone (its renderer processes too), after
    /// which <c>app.Page</c> can no longer be driven; <see cref="CloseOutcome.Kept"/> leaves everything as it was. A
    /// request while another is open joins it.
    /// </summary>
    public async Task<SparCloseResult> CloseAsync(SparCloseOptions? options = null)
    {
        options ??= new SparCloseOptions();
        if (!options.Automated && options.Answer is null)
        {
            throw new ArgumentException(
                "A close with Automated = false needs Answer: a guard's question would otherwise show a native dialog, which a window test never does.",
                nameof(options));
        }

        if (!Enum.IsDefined(options.Reason))
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.Reason, "Reason must be User, App or SessionEnd.");
        }

        var timeout = options.Timeout is { } milliseconds ? TimeSpan.FromMilliseconds(milliseconds) : DefaultTimeout;
        var closeAnyway = options.Answer is { } answer ? answer == CloseAnswer.CloseAnyway : (bool?)null;
        var request = _window.RequestCloseAsync(new SparTestClose(options.Reason, options.Automated, closeAnyway));
        var report = await Within(request, timeout, "its close request to end");
        if (report.Closed)
        {
            await Within(_window.Closed, timeout, "the window and its renderer processes to be gone");
        }

        return new SparCloseResult(report.Closed ? CloseOutcome.Closed : CloseOutcome.Kept, report.Question, report.Unanswered);
    }

    /// <summary>The page's origin, <c>https://app.localhost</c> on Windows and <c>spar://app</c> on Linux, for Spar's own host tests.</summary>
    internal Uri Origin => _window.Origin;

    /// <summary>A close of the window by the user (<c>WM_CLOSE</c>, posted), for Spar's own host tests.</summary>
    internal Task RequestCloseAsync() => _window.PostCloseMessageAsync();

    /// <summary>Sends a window message to the app's window on its UI thread, for Spar's own host tests.</summary>
    internal Task<nint> SendMessageAsync(uint message, nint wParam, nint lParam) => _window.SendMessageAsync(message, wParam, lParam);

    /// <summary>The end of the OS session as the host receives it (Windows' <c>WM_ENDSESSION</c>, Linux's <c>SIGTERM</c> path), for Spar's own host tests.</summary>
    internal Task<bool> EndSessionAsync() => _window.EndSessionAsync();

    /// <summary>The window's title bar scheme and its own colour, as Windows has them, for Spar's own host tests.</summary>
    internal Task<(bool DarkTitleBar, uint WindowColour)> NativeThemeAsync() => _window.NativeThemeAsync();

    /// <summary>The ids of the renderer processes that render the page now.</summary>
    internal Task<IReadOnlyList<int>> RendererProcessesAsync() => _window.RendererProcessesAsync();

    private static async Task<T> Within<T>(Task<T> task, TimeSpan timeout, string what)
    {
        try
        {
            return await task.WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"app.Window.CloseAsync waited {timeout.TotalSeconds:0.#} s for {what}.");
        }
    }
}
