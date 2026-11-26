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

    /// <summary>The version of Spar.Sdk that generates the package, written as its <c>sparSdk</c> field.</summary>
    public string SdkVersion { get; set; } = "0.0.0";

    /// <summary>The project's name, for the snapshot's header.</summary>
    [Required]
    public string ProjectName { get; set; } = "";

    /// <summary>
    /// Where the expected contract snapshot goes, in the variant's <c>obj/</c> folder. The committed copy next to the project is compared
    /// with it on every build by <c>Sdk.targets</c>, so a stale copy is found even when this step is up to date.
    /// </summary>
    [Required]
    public string SnapshotFile { get; set; } = "";

    /// <summary>The libraries whose contracts the app serves (<c>SparContracts</c>), by assembly name, separated by semicolons.</summary>
    public string ContractAssemblies { get; set; } = "";

    /// <summary>The app's project file, where a problem with no place in the source is reported.</summary>
    public string ProjectFile { get; set; } = "";

    /// <summary>Whether the page bundles its telemetry chunk: <c>true</c> when the app references Spar.OpenTelemetry.</summary>
    public bool PageTelemetry { get; set; }

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
            string.Equals(Nullable, "enable", StringComparison.OrdinalIgnoreCase),
            Spar.Generator.ContractProblems.ParseList(ContractAssemblies));

        var read = ContractSource.Read(inputs, CancellationToken.None);
        if (read.Problems.Count > 0)
        {
            // The build stops here, before the frontend steps and the C# compile, so a contract or page security problem
            // is the first and only error: a type-check against a client without the new names would otherwise fail
            // first. Nothing is written, so the last good client and the contract snapshot stay as they were.
            foreach (var problem in read.Problems)
            {
                LogProblem(problem);
            }

            return false;
        }

        var layout = ClientLayout.Plan(read.Contracts);
        WriteSnapshot(layout);

        var output = TaskEnvironment.GetAbsolutePath(OutputDirectory);
        Directory.CreateDirectory(output);
        var sourcePrefix = Path.GetRelativePath(output, project).Replace('\\', '/') + "/";
        var files = ClientPackage.Files(layout, sdk, sdkSources, SdkVersion, sourcePrefix, read.PageSecurity, read.WindowColors, PageTelemetry);
        // The app's variants build at the same time and share this folder, which the frontend's package.json names.
        var leasePath = new AbsolutePath(Path.TrimEndingDirectorySeparator(output.Value) + ".lease");
        using var lease = FileLease.Acquire(leasePath, Log, "Spar: waiting for another build to finish writing the client.");
        if (lease is null)
        {
            Log.LogError("Another build has held the client lease for {0} minutes: {1}.", FileLease.Timeout.TotalMinutes, leasePath);
            return false;
        }

        var written = ClientPackage.Sync(output, files);

        var summary = written.Count == 0 ? "unchanged" : $"{written.Count} file(s) written";
        Log.LogMessage(MessageImportance.Normal, "Spar client: {0} contract(s) in {1}, {2}.", layout.Contracts.Count, output, summary);
        return true;
    }

    /// <summary>
    /// One contract problem as a build error, with the analyzer's ID, message and place (lines from 1). One inside a
    /// referenced library, or about the <c>SparContracts</c> list, has no place in the source: it is charged to the
    /// app's project file, which holds the list, and its message names the library.
    /// </summary>
    private void LogProblem(Microsoft.CodeAnalysis.Diagnostic problem)
    {
        var message = problem.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        if (!problem.Location.IsInSource)
        {
            var project = ProjectFile.Length == 0 ? null : TaskEnvironment.GetAbsolutePath(ProjectFile).Value;
            Log.LogError(problem.Descriptor.Category, problem.Id, null, project, 0, 0, 0, 0, message);
            return;
        }

        var span = problem.Location.GetLineSpan();
        Log.LogError(
            problem.Descriptor.Category,
            problem.Id,
            null,
            span.Path,
            span.StartLinePosition.Line + 1,
            span.StartLinePosition.Character + 1,
            span.EndLinePosition.Line + 1,
            span.EndLinePosition.Character + 1,
            message);
    }

    /// <summary>
    /// Writes the expected snapshot on every run: it is one of the step's Outputs, so its time must be this run's. It is
    /// the variant's own file, so no other build writes it.
    /// </summary>
    private void WriteSnapshot(ClientLayout layout)
    {
        var path = TaskEnvironment.GetAbsolutePath(SnapshotFile);
        Directory.CreateDirectory(new AbsolutePath(Path.GetDirectoryName(path.Value)!));
        File.WriteAllText(path, ContractSnapshot.Text(ProjectName, layout.Contracts));
    }
}
