using Spar;

namespace SqlTool.Status;

[SparContract]
public sealed class StatusService(IResourceUsage usage)
{
    /// <summary>Memory for the status bar: only the newest value, no gap events.</summary>
    [SparCommand]
    [SparSubscription(Coalesce = true)]
    public IAsyncEnumerable<MemoryUsage> Memory(CancellationToken cancellationToken) =>
        usage.WatchMemory(TimeSpan.FromSeconds(1), cancellationToken);

    /// <summary>Folder sizes for the settings page, on request.</summary>
    [SparCommand]
    public Task<Result<DiskUsage, FileAccessError>> DiskAsync(CancellationToken cancellationToken) =>
        usage.GetDiskAsync(cancellationToken);
}
