namespace Spar;

/// <summary>Taskbar progress states besides a value.</summary>
public enum TaskbarProgress
{
    Indeterminate,
    Paused,
    Error,
}

/// <summary>Progress and a badge on the window's taskbar button (Windows) or launcher entry (Linux); injectable.
/// Where unsupported the calls do nothing and <see cref="IsAvailable"/> is false: it is decoration, not a result.</summary>
public interface ITaskbar
{
    bool IsAvailable { get; }

    /// <summary>A value from 0 to 1.</summary>
    void SetProgress(double value);

    void SetProgress(TaskbarProgress state);

    void ClearProgress();

    /// <summary>A count, drawn as a small overlay on Windows and shown by the launcher on Linux; null clears it.</summary>
    void SetBadge(int? count);
}
