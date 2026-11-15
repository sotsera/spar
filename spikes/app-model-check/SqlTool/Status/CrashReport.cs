using Spar;

namespace SqlTool.Status;

public sealed record CrashInfo(DateTimeOffset At, string Summary);

[SparContract]
public sealed class CrashReport(IAppHealth health)
{
    /// <summary>The previous run's crash, so the page can offer to send a report.</summary>
    [SparCommand]
    public Task<CrashInfo?> LastCrashAsync() =>
        Task.FromResult(health.LastCrash is { } crash ? new CrashInfo(crash.At, crash.Exception) : null);
}
