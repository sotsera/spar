namespace Spar;

/// <summary>Power and session events of the machine.</summary>
public closed record SystemEvent
{
    /// <summary>Sleep or hibernate is starting.</summary>
    public sealed record Suspending : SystemEvent;

    public sealed record Resumed : SystemEvent;

    public sealed record Locked : SystemEvent;

    public sealed record Unlocked : SystemEvent;

    /// <summary>Sign-out or shutdown. Not cancellable; the normal shutdown follows the deadline.</summary>
    public sealed record SessionEnding(DateTimeOffset Deadline) : SystemEvent;
}

/// <summary>Power and session events; injectable. The page gets the same through <c>system.on</c>.</summary>
public interface ISystemEvents
{
    IAsyncEnumerable<SystemEvent> Watch(CancellationToken cancellationToken);
}

/// <summary>Keeps the machine awake during long work; injectable.</summary>
public interface IPower
{
    /// <summary>Released on Dispose. SetThreadExecutionState on Windows, a logind inhibitor showing the reason on Linux.</summary>
    IDisposable KeepAwake(string reason);
}
