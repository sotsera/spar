namespace Spar;

/// <summary>One web-engine process and its private memory.</summary>
public sealed record ProcessMemory(string Kind, long Bytes);

/// <summary>Private memory: resident memory only the app's processes hold, that no file backs. The same meaning on both OSes.</summary>
public sealed record MemoryUsage(long TotalBytes, long EngineBytes, long AppBytes, IReadOnlyList<ProcessMemory> Engine);

/// <summary>Sizes of the app's data folders.</summary>
public sealed record DiskUsage(long ProfileBytes, long AppDataBytes, long CacheBytes);

/// <summary>Memory and disk use of the app; injectable. The page sees it only through the app's own contracts.</summary>
public interface IResourceUsage
{
    Task<MemoryUsage> GetMemoryAsync(CancellationToken cancellationToken);

    /// <summary>Samples off the UI thread while enumerated; pushes only changes over 1 % or 1 MiB. Minimum interval 250 ms.</summary>
    IAsyncEnumerable<MemoryUsage> WatchMemory(TimeSpan interval, CancellationToken cancellationToken);

    /// <summary>Walks the data folders, on request.</summary>
    Task<Result<DiskUsage, FileAccessError>> GetDiskAsync(CancellationToken cancellationToken);
}
