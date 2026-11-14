using System.Diagnostics;
using Microsoft.Build.Framework;
using Spike.Tool;
using Task = Microsoft.Build.Utilities.Task;

namespace Spike.Tasks;

/// <summary>Generates contracts.json and the TypeScript client from a contract inventory.</summary>
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
        var (contracts, sources, references, reused) = ClientBuilder.GenerateClient(
            TaskEnvironment.GetAbsolutePath(InventoryFile), TaskEnvironment.GetAbsolutePath(OutputDirectory));
        Log.LogMessage(MessageImportance.High,
            $"spike-task client: {contracts} contract(s), {sources} source(s), {references} reference(s), {reused} reused, {stopwatch.ElapsedMilliseconds} ms, pid {Environment.ProcessId}");
        return !Log.HasLoggedErrors;
    }
}

/// <summary>Writes a deterministic zip of the given directories.</summary>
[MSBuildMultiThreadableTask]
public sealed class PackFrontend : Task, IMultiThreadableTask
{
    public TaskEnvironment TaskEnvironment { get; set; } = TaskEnvironment.Fallback;

    [Required]
    public string ZipFile { get; set; } = string.Empty;

    [Required]
    public ITaskItem[] Directories { get; set; } = [];

    public override bool Execute()
    {
        var stopwatch = Stopwatch.StartNew();
        var changed = ClientBuilder.Pack(
            TaskEnvironment.GetAbsolutePath(ZipFile),
            Directories.Select(d => (string)TaskEnvironment.GetAbsolutePath(d.ItemSpec)));
        Log.LogMessage(MessageImportance.High, $"spike-task pack: changed={changed}, {stopwatch.ElapsedMilliseconds} ms, pid {Environment.ProcessId}");
        return true;
    }
}
