namespace Spar.Testing;

/// <summary>
/// A launched app's process tree, as the platform-neutral fixture sees it. On Windows it is <see cref="AppProcess"/>, a
/// job object; Linux's comes with its host (M1c).
/// </summary>
internal interface IBlackBoxProcess : IDisposable
{
    int ProcessId { get; }

    string StandardOutput { get; }

    string StandardError { get; }

    /// <summary>Completes with the exit code when the app's own process exits.</summary>
    Task<int> Exited { get; }

    /// <summary>The ids of the tree's running processes: the app and everything it started.</summary>
    IReadOnlyList<int> ProcessIds { get; }

    /// <summary>Ends the app and everything it started, at once.</summary>
    void Kill();

    /// <summary>Waits until no process of the tree is left, at most <paramref name="limit"/>; true when none is left.</summary>
    Task<bool> WaitForTreeAsync(TimeSpan limit);

    /// <summary>Waits until standard output and error have ended, at most <paramref name="limit"/>.</summary>
    Task<bool> WaitForOutputAsync(TimeSpan limit);

    /// <summary>Suspends every thread of the app's own process, as a hung app: for Spar's own tests.</summary>
    void SuspendProcess();
}
