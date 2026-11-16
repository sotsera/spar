namespace SqlTool.CopyJobs;

/// <summary>How many copies are running, for the close guard in Program.cs.</summary>
public sealed class RunningCopies
{
    private int _running;

    public bool Any => Volatile.Read(ref _running) > 0;

    public void Started() => Interlocked.Increment(ref _running);

    public void Ended() => Interlocked.Decrement(ref _running);
}
