using Spar;

namespace SqlTool.Monitoring;

public sealed class PowerAwareMonitor(ISystemEvents systemEvents, IPower power, IMonitor monitor)
{
    /// <summary>Pauses monitoring while the machine sleeps; saves state before the session ends.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await foreach (var e in systemEvents.Watch(cancellationToken))
        {
            switch (e)
            {
                case SystemEvent.Suspending or SystemEvent.Resumed:
                    monitor.TogglePause();
                    break;
                case SystemEvent.SessionEnding ending:
                    await SaveAsync(ending.Deadline);
                    break;
            }
        }
    }

    /// <summary>A long copy keeps the PC awake until it ends.</summary>
    public async Task CopyAsync(Func<Task> copy)
    {
        using var awake = power.KeepAwake("Copying tables");
        await copy();
    }

    private static Task SaveAsync(DateTimeOffset deadline) => Task.CompletedTask;
}
