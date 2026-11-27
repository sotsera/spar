using Spar;

namespace SqlTool.Reports;

/// <summary>A server's activity chart for the last minutes, by its key: what the page's address carries.</summary>
public sealed record ChartKey(string Server, int Minutes);

/// <summary>Reports the page shows or saves. Wire name: reports.</summary>
[SparContract]
public sealed class ReportsService(ICopyHistory history, IActivityCharts charts, SparBlobs blobs)
{
    /// <summary>A finished copy's rows as CSV, for the page to show or save: a handle it releases when it is done.</summary>
    [SparCommand]
    public async Task<SparBlob> ExportCopyAsync(string jobId, CancellationToken cancellationToken)
    {
        await using var csv = history.OpenCsv(jobId);
        return await blobs.FromStreamAsync(csv, "text/csv", $"copy-{jobId}.csv", cancellationToken);
    }

    /// <summary>The chart as PNG, drawn at each load of its address; none for a server the tool does not monitor.</summary>
    [SparCommand]
    [SparResource]
    public async Task<SparBlob?> ActivityChartAsync(ChartKey key, CancellationToken cancellationToken)
    {
        var png = await charts.DrawAsync(key.Server, TimeSpan.FromMinutes(key.Minutes), cancellationToken);
        return png is null ? null : SparBlob.FromBytes(png, "image/png");
    }
}
