using Microsoft.Build.Framework;
using Spar.Sdk.Tasks.Client;
using MSBuildTask = Microsoft.Build.Utilities.Task;

namespace Spar.Sdk.Tasks;

/// <summary>
/// The client step (plan section 1, pipeline step 3): reads the app's contracts with the generator's analysis and writes
/// the <c>@spar/app</c> package under <c>obj/spar/client</c>, changing only files whose bytes differ. Never runs at
/// design time; <c>Sdk.targets</c> guards it.
/// </summary>
[MSBuildMultiThreadableTask]
public sealed class SparClient : MSBuildTask, IMultiThreadableTask
{
    public SparClient()
        : this(TaskEnvironment.Fallback)
    {
    }

    public SparClient(TaskEnvironment environment)
    {
        TaskEnvironment = environment;
    }

    public TaskEnvironment TaskEnvironment { get; set; }

    /// <summary>The project's C# sources (<c>@(Compile)</c>).</summary>
    [Required]
    public ITaskItem[] Sources { get; set; } = [];

    /// <summary>Its resolved references (<c>@(ReferencePath)</c>).</summary>
    [Required]
    public ITaskItem[] References { get; set; } = [];

    public string DefineConstants { get; set; } = "";

    /// <summary>The project's <c>Nullable</c> setting, which decides whether <c>string?</c> is a nullable wire type.</summary>
    public string Nullable { get; set; } = "";

    [Required]
    public string ProjectDirectory { get; set; } = "";

    /// <summary>The package folder, <c>obj/spar/client</c>.</summary>
    [Required]
    public string OutputDirectory { get; set; } = "";

    /// <summary>The compiled TypeScript SDK (its <c>.js</c>, <c>.d.ts</c> and maps).</summary>
    [Required]
    public string SdkDirectory { get; set; } = "";

    /// <summary>The SDK's TypeScript sources, shipped under <c>sdk/src/</c> for go-to-definition.</summary>
    [Required]
    public string SdkSourceDirectory { get; set; } = "";

    /// <summary>The package's version: the SDK's.</summary>
    public string Version { get; set; } = "0.0.0";

    /// <summary>The project's name, for the snapshot's header.</summary>
    [Required]
    public string ProjectName { get; set; } = "";

    /// <summary>
    /// Where the expected contract snapshot goes, under <c>obj/</c>. The committed copy next to the project is compared
    /// with it on every build by <c>Sdk.targets</c>, so a stale copy is found even when this step is up to date.
    /// </summary>
    [Required]
    public string SnapshotFile { get; set; } = "";

    public override bool Execute()
    {
        var project = TaskEnvironment.GetAbsolutePath(ProjectDirectory);
        var sdk = TaskEnvironment.GetAbsolutePath(SdkDirectory);
        var sdkSources = TaskEnvironment.GetAbsolutePath(SdkSourceDirectory);
        if (!File.Exists(new AbsolutePath("sdk.js", sdk)))
        {
            Log.LogError("The TypeScript SDK is not built: no sdk.js in '{0}'. Run 'npm run build' in src/Spar.Client.", sdk);
            return false;
        }

        var inputs = new ClientInputs(
            project,
            [.. Sources.Select(s => TaskEnvironment.GetAbsolutePath(s.ItemSpec))],
            [.. References.Select(r => TaskEnvironment.GetAbsolutePath(r.ItemSpec))],
            [.. DefineConstants.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
            string.Equals(Nullable, "enable", StringComparison.OrdinalIgnoreCase));

        var layout = ClientLayout.Plan(ContractSource.Read(inputs, CancellationToken.None));
        foreach (var problem in layout.Problems)
        {
            var file = problem.SourceFile.Length == 0 ? null : new AbsolutePath(problem.SourceFile, project).Value;
            Log.LogError("Spar.Contracts", problem.Code, null, file, problem.Line, 0, 0, 0, problem.Message);
        }

        if (layout.Problems.Count > 0)
        {
            return false;
        }

        WriteSnapshot(layout);

        var output = TaskEnvironment.GetAbsolutePath(OutputDirectory);
        Directory.CreateDirectory(output);
        var sourcePrefix = Path.GetRelativePath(output, project).Replace('\\', '/') + "/";
        var files = ClientPackage.Files(layout, sdk, sdkSources, Version, sourcePrefix);
        var written = ClientPackage.Sync(output, files);

        var summary = written.Count == 0 ? "unchanged" : $"{written.Count} file(s) written";
        Log.LogMessage(MessageImportance.Normal, "Spar client: {0} contract(s) in {1}, {2}.", layout.Contracts.Count, output, summary);
        return true;
    }

    /// <summary>Writes the expected snapshot when its text changed.</summary>
    private void WriteSnapshot(ClientLayout layout)
    {
        var path = TaskEnvironment.GetAbsolutePath(SnapshotFile);
        var expected = System.Text.Encoding.UTF8.GetBytes(ContractSnapshot.Text(ProjectName, layout.Contracts));
        if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(expected))
        {
            Directory.CreateDirectory(new AbsolutePath(Path.GetDirectoryName(path.Value)!));
            ClientPackage.Write(path, expected);
        }
    }
}
