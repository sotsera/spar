namespace Spar.Files;

/// <summary>
/// The paths a folder grant reaches: a relative path is resolved against the folder, and refused when it leaves it,
/// whether by being absolute, by a <c>..</c> segment, or through a link (a symbolic link, or a junction on Windows)
/// whose final target is outside the folder's own real path. Each use is checked again, since a link can be made
/// after the grant.
/// </summary>
internal static class GrantPaths
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static readonly string[] DeviceNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    /// <summary>The folder's real path, its own links followed; the grant keeps it, so a later link at the root changes nothing.</summary>
    public static string RealPath(string folder)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var target = new DirectoryInfo(full).ResolveLinkTarget(returnFinalTarget: true);
        return target is null ? full : Path.TrimEndingDirectorySeparator(Path.GetFullPath(target.FullName));
    }

    /// <summary>The full path of <paramref name="relative"/> inside <paramref name="root"/>, or why it is refused.</summary>
    public static Result<string, FileAccessError> Resolve(string root, string relative)
    {
        relative ??= "";
        if (Refused(relative))
        {
            return new FileAccessError.OutsideFolder(relative);
        }

        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(root, relative)));
        if (!IsInside(root, full))
        {
            return new FileAccessError.OutsideFolder(relative);
        }

        return LeavesThroughLink(root, full) ? new FileAccessError.OutsideFolder(relative) : full;
    }

    /// <summary>Whether <paramref name="path"/> is <paramref name="root"/> itself or below it.</summary>
    public static bool IsInside(string root, string path)
    {
        if (string.Equals(root, path, PathComparison))
        {
            return true;
        }

        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, PathComparison);
    }

    private static bool Refused(string relative)
    {
        if (relative.Contains('\0') || Path.IsPathRooted(relative))
        {
            return true;
        }

        // A colon names a drive or an alternate data stream on Windows; on Linux it is an ordinary character.
        if (OperatingSystem.IsWindows() && relative.Contains(':'))
        {
            return true;
        }

        var segments = relative.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            if (segment == "..")
            {
                return true;
            }

            if (OperatingSystem.IsWindows() && IsDeviceName(segment))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Windows opens a device, not a file, for <c>NUL</c>, <c>COM1.txt</c> and the like.</summary>
    private static bool IsDeviceName(string segment)
    {
        var dot = segment.IndexOf('.');
        var stem = (dot < 0 ? segment : segment[..dot]).TrimEnd(' ');
        return DeviceNames.Contains(stem, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Whether a link on the way from the root to <paramref name="full"/> points outside the root.</summary>
    private static bool LeavesThroughLink(string root, string full)
    {
        var current = root;
        var rest = full.Length > root.Length ? full[(root.Length + 1)..] : "";
        foreach (var segment in rest.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileSystemInfo entry = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            // A link that points nowhere yet is still checked: writing through it would create its target.
            if (entry.LinkTarget is null)
            {
                continue;
            }

            var target = entry.ResolveLinkTarget(returnFinalTarget: true);
            if (target is null || !IsInside(root, Path.TrimEndingDirectorySeparator(Path.GetFullPath(target.FullName))))
            {
                return true;
            }
        }

        return false;
    }
}
