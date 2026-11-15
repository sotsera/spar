namespace Spar.Loopback;

/// <summary>
/// The page's files, read from folders mounted at URL prefixes. A stand-in until the packing step (plan section 1)
/// embeds the built page in the app: then the host serves the packed files and this type goes away.
/// </summary>
internal sealed class LoopbackAssets
{
    private readonly List<(string Prefix, string Folder)> _mounts = [];

    /// <summary>Serves <paramref name="folder"/> under <paramref name="prefix"/> (<c>""</c> for the root, or <c>"spar/"</c>).</summary>
    public LoopbackAssets Mount(string prefix, string folder)
    {
        if (prefix.Length > 0 && (!prefix.EndsWith('/') || !IsSafePath(prefix[..^1])))
        {
            throw new ArgumentException($"A mount prefix is empty or safe path segments ending in '/', not '{prefix}'.", nameof(prefix));
        }

        var full = Path.GetFullPath(folder);
        if (!Directory.Exists(full))
        {
            throw new DirectoryNotFoundException($"The page folder '{full}' does not exist.");
        }

        // The longest prefix wins, so "spar/" is tried before "".
        _mounts.Add((prefix, Path.TrimEndingDirectorySeparator(full) + Path.DirectorySeparatorChar));
        _mounts.Sort((left, right) => right.Prefix.Length.CompareTo(left.Prefix.Length));
        return this;
    }

    /// <summary>Reads one file by its path relative to the page root, for example <c>assets/index.js</c>.</summary>
    public byte[]? Read(string path)
    {
        if (!IsSafePath(path))
        {
            return null;
        }

        foreach (var (prefix, folder) in _mounts)
        {
            if (!path.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var file = new FileInfo(Path.GetFullPath(Path.Combine(folder, path[prefix.Length..])));

            // The path check already rules out escapes; links are refused like the packing step refuses them.
            if (file.FullName.StartsWith(folder, StringComparison.Ordinal) && file.Exists && file.LinkTarget is null)
            {
                return File.ReadAllBytes(file.FullName);
            }
        }

        return null;
    }

    /// <summary>
    /// Segments of letters, digits and <c>- _ . ~ @ +</c>, none empty and none starting with a dot, so no <c>..</c>,
    /// dotfiles (<c>.git</c>, <c>.env</c>), backslashes, drive letters or escaped characters.
    /// </summary>
    internal static bool IsSafePath(string path)
    {
        if (path.Length == 0)
        {
            return false;
        }

        foreach (var segment in path.Split('/'))
        {
            if (segment.Length == 0 || segment[0] == '.' || !segment.All(IsSafeCharacter))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSafeCharacter(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '~' or '@' or '+';
}
