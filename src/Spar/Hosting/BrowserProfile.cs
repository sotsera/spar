namespace Spar.Hosting;

/// <summary>
/// The browser profile folders no file grant or helper may reach (plan section 3c): the one Spar asked for, and the one
/// the browser actually uses when a policy or an allowed environment variable moved it. A singleton in the app's
/// services; the host records the folder in use once the environment exists, and M3's file grants refuse any path
/// inside either folder.
/// </summary>
internal sealed class BrowserProfile(string requested)
{
    private volatile string? _inUse;

    /// <summary>The folder Spar asked the browser to use (<see cref="SparFolders.Profile"/>).</summary>
    public string Requested { get; } = requested;

    /// <summary>The folder the browser reported once its environment existed; null until then.</summary>
    public string? InUse => _inUse;

    /// <summary>
    /// Records the folder the browser reported, and returns true when it is not the one Spar asked for: an
    /// administrator's policy or an allowed environment variable moved the profile.
    /// </summary>
    public bool RecordInUse(string folder)
    {
        _inUse = folder;
        return !SameFolder(folder, Requested);
    }

    /// <summary>Whether <paramref name="path"/> is one of the profile folders or inside one.</summary>
    public bool Contains(string path)
    {
        var full = Path.GetFullPath(path);
        return IsInside(full, Requested) || (_inUse is { } inUse && IsInside(full, inUse));
    }

    /// <summary>Two folders compared as Windows compares them: full paths, any case, without a trailing separator.</summary>
    internal static bool SameFolder(string first, string second) =>
        string.Equals(Normalize(first), Normalize(second), StringComparison.OrdinalIgnoreCase);

    private static bool IsInside(string path, string folder)
    {
        var root = Normalize(folder);
        var candidate = Normalize(path);
        return candidate.Equals(root, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string folder) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
}
