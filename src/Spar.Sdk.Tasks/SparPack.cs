using Microsoft.Build.Framework;
using Spar.Sdk.Tasks.Client;
using Spar.Sdk.Tasks.Pack;
using MSBuildTask = Microsoft.Build.Utilities.Task;

namespace Spar.Sdk.Tasks;

/// <summary>
/// The packing step (plan section 1): the built page in one resource, after the asset checks of section 3c, and the
/// frontend's third-party licences (Vite's <c>.vite/license.md</c>) as the notices the executable embeds. Each output is
/// written only when its bytes change.
/// </summary>
[MSBuildMultiThreadableTask]
public sealed class SparPack : MSBuildTask, IMultiThreadableTask
{
    public SparPack()
        : this(TaskEnvironment.Fallback)
    {
    }

    public SparPack(TaskEnvironment environment)
    {
        TaskEnvironment = environment;
    }

    public TaskEnvironment TaskEnvironment { get; set; }

    /// <summary>The page as built (Vite's output folder).</summary>
    [Required]
    public string FrontendDirectory { get; set; } = "";

    [Required]
    public string PackFile { get; set; } = "";

    /// <summary>Where the third-party notices go, Brotli-compressed; written empty when the build produced none.</summary>
    [Required]
    public string NoticesFile { get; set; } = "";

    public override bool Execute()
    {
        var frontend = TaskEnvironment.GetAbsolutePath(FrontendDirectory);
        var files = new List<PageFile>();
        foreach (var file in Directory.EnumerateFiles(frontend, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(frontend, file).Replace('\\', '/');
            if (!relative.StartsWith(FrontendPack.ViteFolder, StringComparison.Ordinal))
            {
                files.Add(new PageFile(relative, new AbsolutePath(file)));
            }
        }

        var problems = FrontendPack.Check(files);
        foreach (var problem in problems)
        {
            Log.LogError("Spar.Frontend", "SPAR110", null, new AbsolutePath(problem.Path.Split(" and ")[0], frontend).Value, 0, 0, 0, 0,
                "The page cannot ship '{0}': {1}.", problem.Path, problem.Reason);
        }

        if (problems.Count > 0)
        {
            return false;
        }

        var pack = FrontendPack.Write(files);
        var packWritten = WriteIfChanged(TaskEnvironment.GetAbsolutePath(PackFile), pack);

        var license = new AbsolutePath(FrontendPack.LicenseFile, frontend);
        var notices = File.Exists(license) ? FrontendPack.Compress(File.ReadAllBytes(license)) : [];
        WriteIfChanged(TaskEnvironment.GetAbsolutePath(NoticesFile), notices);

        var stored = files.Sum(f => new FileInfo(f.Source).Length);
        Log.LogMessage(MessageImportance.Normal, "Spar pack: {0} file(s), {1:N0} B into {2:N0} B{3}.", files.Count, stored, pack.Length, packWritten ? "" : " (unchanged)");
        return true;
    }

    private static bool WriteIfChanged(AbsolutePath path, byte[] bytes)
    {
        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
        {
            return false;
        }

        Directory.CreateDirectory(new AbsolutePath(Path.GetDirectoryName(path.Value)!));
        ClientPackage.Write(path, bytes);
        return true;
    }
}
