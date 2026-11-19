namespace Spar;

/// <summary>Why a window is asked to close.</summary>
public enum CloseReason
{
    /// <summary>The close button, Alt+F4, or the taskbar's Close window.</summary>
    User,

    /// <summary>The app's own code, or Quit in the tray menu.</summary>
    App,

    /// <summary>Windows is logging off or shutting down: guards may save, but cannot keep the window open or ask.</summary>
    SessionEnd,
}

/// <summary>A request to close a window, which the page's guard and then the backend's may keep open.</summary>
/// <param name="Window">The window kind; <c>main</c> for the main window.</param>
/// <param name="Reason">Why it is closing.</param>
/// <param name="IsAutomated">
/// True when tests or an agent close the app: a guard should not wait for a person, and an <c>Ask</c> counts as close.
/// </param>
public sealed record CloseRequest(string Window, CloseReason Reason, bool IsAutomated);

/// <summary>A close guard's answer.</summary>
public sealed class CloseDecision
{
    private CloseDecision(string? question, bool close)
    {
        Question = question;
        IsClose = close;
    }

    /// <summary>The window may close.</summary>
    public static CloseDecision Close { get; } = new(null, close: true);

    /// <summary>The window stays open.</summary>
    public static CloseDecision Keep { get; } = new(null, close: false);

    /// <summary>The question Spar shows for <see cref="Ask"/>; null otherwise.</summary>
    public string? Question { get; }

    internal bool IsClose { get; }

    /// <summary>Spar asks the user, in a native confirmation owned by the window: "Close anyway" or "Keep open".</summary>
    public static CloseDecision Ask(string question)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        return new CloseDecision(question, close: false);
    }
}
