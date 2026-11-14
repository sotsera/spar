using System.Diagnostics;
using Microsoft.Build.Framework;
using Spar.Tool;
using Task = Microsoft.Build.Utilities.Task;

namespace Spar.Tasks;

/// <summary>Generates contracts.json and the TypeScript packages from a contract inventory.</summary>
[MSBuildMultiThreadableTask]
public sealed class GenerateContractClient : Task, IMultiThreadableTask
{
    public TaskEnvironment TaskEnvironment { get; set; } = TaskEnvironment.Fallback;

    [Required]
    public string InventoryFile { get; set; } = string.Empty;

    [Required]
    public string OutputDirectory { get; set; } = string.Empty;

    public override bool Execute()
    {
        var stopwatch = Stopwatch.StartNew();
        var (contracts, files, written) = ClientBuilder.GenerateClient(
            TaskEnvironment.GetAbsolutePath(InventoryFile), TaskEnvironment.GetAbsolutePath(OutputDirectory));
        Log.LogMessage(MessageImportance.High,
            $"spar client: {contracts} contract(s), {files} file(s), {written} written, {stopwatch.ElapsedMilliseconds} ms");
        return !Log.HasLoggedErrors;
    }
}
