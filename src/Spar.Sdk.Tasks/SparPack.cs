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

    /// <summary>Where the third-party notices go, Brotli-compressed; deleted when there are none.</summary>
    [Required]
    public string NoticesFile { get; set; } = "";

    /// <summary>
    /// <c>SparThirdPartyNotices</c>: <c>true</c> (Vite's licence file), <c>false</c> (none), or the path of the team's own
    /// notices file, relative to the project.
    /// </summary>
    public string NoticesSource { get; set; } = "true";

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

        if (!WriteNotices(frontend))
        {
            return false;
        }

        var stored = files.Sum(f => new FileInfo(f.Source).Length);
        Log.LogMessage(MessageImportance.Normal, "Spar pack: {0} file(s), {1:N0} B into {2:N0} B{3}.", files.Count, stored, pack.Length, packWritten ? "" : " (unchanged)");
        return true;
    }

    /// <summary>
    /// The notices the executable carries, and one informational line saying what they are (decision "Source maps stay
    /// out of the executable; third-party notices are reported and copyleft is flagged"). Never a warning: an app built
    /// from the template fails on warnings.
    /// </summary>
    private bool WriteNotices(AbsolutePath frontend)
    {
        var target = TaskEnvironment.GetAbsolutePath(NoticesFile);
        if (string.Equals(NoticesSource, "false", StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(target);
            Log.LogMessage(MessageImportance.High, "Spar: the executable carries no third-party notices (SparThirdPartyNotices=false).");
            return true;
        }

        var own = !string.Equals(NoticesSource, "true", StringComparison.OrdinalIgnoreCase) && NoticesSource.Length > 0;
        var source = own ? TaskEnvironment.GetAbsolutePath(NoticesSource) : new AbsolutePath(FrontendPack.LicenseFile, frontend);
        if (!File.Exists(source))
        {
            if (own)
            {
                Log.LogError("Spar.Frontend", "SPAR111", null, null, 0, 0, 0, 0,
                    "SparThirdPartyNotices names '{0}', which does not exist.", source.Value);
                return false;
            }

            File.Delete(target);
            Log.LogMessage(MessageImportance.High, "Spar: the page bundles nothing with a licence file, so the executable carries no third-party notices.");
            return true;
        }

        var text = File.ReadAllBytes(source);
        var compressed = FrontendPack.Compress(text);
        WriteIfChanged(target, compressed);

        var origin = own ? Path.GetFileName(source.Value) : "Vite's licence file";
        var packages = own ? "" : $" of {FrontendPack.CountPackages(text)} packages";
        Log.LogMessage(MessageImportance.High, "Spar: the executable carries the third-party notices{0} from {1} ({2:N0} B); spar doctor lists them.", packages, origin, compressed.Length);
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
