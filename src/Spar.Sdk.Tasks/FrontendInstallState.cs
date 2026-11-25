namespace Spar.Sdk.Tasks;

/// <summary>
/// When the frontend's <c>node_modules</c> is current (plan section 1, pipeline step 4): npm's own record of the
/// install, <c>node_modules/.package-lock.json</c>, is at least as new as the lockfile. The build's install step
/// (<c>SparFrontendInstall</c>, and its target's inputs and outputs) and <c>spar doctor</c> (linked into
/// <c>src/Spar.Tool</c>) decide it with this one rule. Times only, so each caller reads the files its own way.
/// </summary>
internal static class FrontendInstallState
{
    /// <summary>The lockfile, in the frontend folder.</summary>
    public const string Lockfile = "package-lock.json";

    /// <summary>npm's record of the last install, in the frontend folder.</summary>
    public const string InstallRecord = "node_modules/.package-lock.json";

    /// <summary>Whether an install is current: both files exist and the record is not older than the lockfile.</summary>
    public static bool IsCurrent(DateTime? lockfileWritten, DateTime? installRecordWritten) =>
        lockfileWritten is { } lockfile && installRecordWritten is { } installed && installed >= lockfile;
}
