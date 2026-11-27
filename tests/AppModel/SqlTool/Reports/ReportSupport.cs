// What docs/app-model.md leaves out: where finished copies' rows and the activity charts come from.
// Not copied from the document; stand-ins so that its blocks compile and its Program runs.
namespace SqlTool.Reports;

/// <summary>The rows of finished copies.</summary>
public interface ICopyHistory
{
    /// <summary>A finished copy's rows as CSV; the caller disposes the stream.</summary>
    Stream OpenCsv(string jobId);
}

/// <summary>Draws the monitored servers' activity.</summary>
public interface IActivityCharts
{
    /// <summary>A PNG of the server's activity over <paramref name="span"/>, or null for a server the tool does not monitor.</summary>
    Task<byte[]?> DrawAsync(string server, TimeSpan span, CancellationToken cancellationToken);
}

/// <summary>Stand-in: one header row for every copy.</summary>
public sealed class CopyHistory : ICopyHistory
{
    public Stream OpenCsv(string jobId) => new MemoryStream("table,rows\n"u8.ToArray());
}

/// <summary>Stand-in: no server is monitored.</summary>
public sealed class ActivityCharts : IActivityCharts
{
    public Task<byte[]?> DrawAsync(string server, TimeSpan span, CancellationToken cancellationToken) => Task.FromResult<byte[]?>(null);
}
