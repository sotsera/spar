using System.Diagnostics;
using Microsoft.Build.Framework;
using Task = Microsoft.Build.Utilities.Task;

namespace Spar.AssetPacking;

/// <summary>Packs the built frontend in one of the compared formats (see <see cref="FrontendPacker"/>).</summary>
[MSBuildMultiThreadableTask]
public sealed class PackFrontend : Task, IMultiThreadableTask
{
    public TaskEnvironment TaskEnvironment { get; set; } = TaskEnvironment.Fallback;

    [Required]
    public string InputDirectory { get; set; } = string.Empty;

    [Required]
    public string OutputDirectory { get; set; } = string.Empty;

    [Required]
    public string Format { get; set; } = string.Empty;

    public int Quality { get; set; } = 11;

    /// <summary>Brotli window bits; 0 fits the window to each file.</summary>
    public int Window { get; set; }

    public override bool Execute()
    {
        var stopwatch = Stopwatch.StartNew();
        var report = FrontendPacker.Pack(
            TaskEnvironment.GetAbsolutePath(InputDirectory),
            TaskEnvironment.GetAbsolutePath(OutputDirectory),
            Format,
            Quality,
            Window);
        foreach (var line in report.Lines)
        {
            Log.LogMessage(MessageImportance.Normal, "spar-pack " + line);
        }

        Log.LogMessage(
            MessageImportance.High,
            $"spar-pack {Format}: {report.Entries} files, {report.OriginalBytes} B -> {report.StoredBytes} B stored, " +
            $"{report.EmbeddedBytes} B embedded in {report.Outputs} file(s), {report.Compressed} compressed, " +
            $"written={report.Written} deleted={report.Deleted}, {stopwatch.ElapsedMilliseconds} ms");
        return !Log.HasLoggedErrors;
    }
}
