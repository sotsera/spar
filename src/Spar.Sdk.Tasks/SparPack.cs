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

    /// <summary>
    /// `SparFrontendCompression`: `auto` (Brotli when it saves more than the decoder costs), `brotli` or `none`.
    /// </summary>
    public string Compression { get; set; } = "auto";

    /// <summary>Where the decision goes, `brotli` or `stored`, so a build that skips this step still applies it.</summary>
    [Required]
    public string ModeFile { get; set; } = "";

    public override bool Execute()
    {
        var frontend = TaskEnvironment.GetAbsolutePath(FrontendDirectory);
        var files = new List<PageFile>();
        foreach (var file in Directory.EnumerateFiles(frontend, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(frontend, file).Replace(Path.DirectorySeparatorChar, '/');
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

        var mode = Compression.Trim().ToLowerInvariant();
        if (mode is not ("auto" or "brotli" or "none"))
        {
            Log.LogError("Spar.Frontend", "SPAR112", null, null, 0, 0, 0, 0,
                "SparFrontendCompression is '{0}'; it must be auto, brotli or none.", Compression);
            return false;
        }

        if (problems.Count > 0 || !ReadNotices(frontend, out var notices))
        {
            return false;
        }

        var entries = FrontendPack.Encode(files);
        var compressedNotices = notices is null ? null : FrontendPack.Compress(notices.Text);
        var saving = entries.Sum(e => (long)e.Saving) + (notices is null ? 0 : notices.Text.Length - compressedNotices!.Length);
        var compress = Decide(mode, saving);
        if (!compress)
        {
            entries = [.. entries.Select(e => e.AsStored())];
        }

        var pack = FrontendPack.Write(entries);
        var packWritten = WriteIfChanged(TaskEnvironment.GetAbsolutePath(PackFile), pack);
        WriteIfChanged(TaskEnvironment.GetAbsolutePath(ModeFile), System.Text.Encoding.ASCII.GetBytes(compress ? "brotli" : "stored"));
        WriteNotices(notices, compress, compressedNotices);

        var original = entries.Sum(e => (long)e.OriginalLength);
        Log.LogMessage(MessageImportance.Normal, "Spar pack: {0} file(s), {1:N0} B into {2:N0} B{3}.", files.Count, original, pack.Length, packWritten ? "" : " (unchanged)");
        return true;
    }

    /// <summary>
    /// Brotli when it saves more than its decoder costs the executable (decision "Frontend packing: stored below the
    /// decoder's cost"); otherwise everything is stored and the decoder is trimmed. One line says which, and why.
    /// </summary>
    private bool Decide(string mode, long saving)
    {
        var compress = FrontendPack.ShouldCompress(mode, saving);

        var why = mode is "brotli" or "none" ? $"SparFrontendCompression={mode}" : compress
            ? $"Brotli saves {saving:N0} B, more than its decoder costs"
            : $"Brotli would save {saving:N0} B, less than its decoder costs, so the decoder is left out";
        Log.LogMessage(MessageImportance.High, "Spar: the page is packed {0} ({1}).", compress ? "with Brotli" : "stored", why);
        return compress;
    }

    /// <summary>The notices' text and where it came from, or none; false when a named file does not exist (SPAR111).</summary>
    private bool ReadNotices(AbsolutePath frontend, out NoticesText? notices)
    {
        notices = null;
        if (string.Equals(NoticesSource, "false", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var own = !string.Equals(NoticesSource, "true", StringComparison.OrdinalIgnoreCase) && NoticesSource.Length > 0;
        var source = own ? TaskEnvironment.GetAbsolutePath(NoticesSource) : new AbsolutePath(FrontendPack.LicenseFile, frontend);
        if (File.Exists(source))
        {
            notices = new NoticesText(File.ReadAllBytes(source), own ? Path.GetFileName(source.Value) : null);
            return true;
        }

        if (!own)
        {
            return true;
        }

        Log.LogError("Spar.Frontend", "SPAR111", null, null, 0, 0, 0, 0, "SparThirdPartyNotices names '{0}', which does not exist.", source.Value);
        return false;
    }

    /// <summary>
    /// The notices the executable carries, and one informational line saying what they are (decision "Source maps stay
    /// out of the executable; third-party notices are reported and copyleft is flagged"). Never a warning: an app built
    /// from the template fails on warnings.
    /// </summary>
    private void WriteNotices(NoticesText? notices, bool compress, byte[]? compressed)
    {
        var target = TaskEnvironment.GetAbsolutePath(NoticesFile);
        if (notices is null)
        {
            File.Delete(target);
            var why = string.Equals(NoticesSource, "false", StringComparison.OrdinalIgnoreCase)
                ? "SparThirdPartyNotices=false"
                : "the page bundles nothing with a licence file";
            Log.LogMessage(MessageImportance.High, "Spar: the executable carries no third-party notices ({0}).", why);
            return;
        }

        var bytes = compress ? [(byte)1, .. compressed!] : FrontendPack.Notices(notices.Text, compress: false);
        WriteIfChanged(target, bytes);

        var origin = notices.OwnFile ?? "Vite's licence file";
        var packages = notices.OwnFile is null ? $" of {FrontendPack.CountPackages(notices.Text)} packages" : "";
        Log.LogMessage(MessageImportance.High, "Spar: the executable carries the third-party notices{0} from {1} ({2:N0} B); spar doctor lists them.", packages, origin, bytes.Length);
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

    /// <summary>The notices' text, and the team's file name when it is not Vite's licence file.</summary>
    private sealed record NoticesText(byte[] Text, string? OwnFile);
}
