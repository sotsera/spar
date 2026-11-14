using System.Diagnostics;
using System.IO.Enumeration;

namespace Spar.ResourceUsage;

/// <summary>One folder walk: counts and sizes. Allocated is -1 when the method cannot see it.</summary>
internal readonly record struct WalkResult(long Files, long Directories, long Logical, long Allocated, long Errors, double Ms)
{
    public override string ToString() =>
        $"files={Files} dirs={Directories} logicalKB={Logical / 1024} allocatedKB={(Allocated < 0 ? -1 : Allocated / 1024)} errors={Errors} ms={Ms:F2}";
}

/// <summary>Folder walks that work on both OSes, through .NET's own enumeration.</summary>
internal static class Walk
{
    /// <summary>
    /// The default EnumerationOptions skip Hidden and System entries; a size walk must not. Directory reparse points
    /// (junctions, symlinks) are not followed.
    /// </summary>
    private static readonly EnumerationOptions s_options = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = false,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
    };

    public static WalkResult FileInfos(string root)
    {
        var started = Stopwatch.GetTimestamp();
        long files = 0, bytes = 0, errors = 0;
        try
        {
            foreach (var file in new DirectoryInfo(root).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 }))
            {
                files++;
                bytes += file.Length;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            errors++;
        }

        return new WalkResult(files, -1, bytes, -1, errors, Log.Ms(started));
    }

    /// <summary>
    /// Enumerates paths, then asks each file for its size (GetFileAttributesEx on Windows, stat on Linux). On NTFS this
    /// sees the current size of files still open for writing, which the directory entries may not show yet.
    /// </summary>
    public static WalkResult PerFile(string root)
    {
        var started = Stopwatch.GetTimestamp();
        long files = 0, bytes = 0, errors = 0;
        foreach (var path in Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 }))
        {
            try
            {
                bytes += new FileInfo(path).Length;
                files++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                errors++;
            }
        }

        return new WalkResult(files, -1, bytes, -1, errors, Log.Ms(started));
    }

    /// <summary>FileSystemEnumerable with a value-type transform: no FileInfo per entry.</summary>
    public static WalkResult Entries(string root)
    {
        var started = Stopwatch.GetTimestamp();
        using var enumerator = new CountingEnumerator(root);
        long files = 0, directories = 0, bytes = 0;
        while (enumerator.MoveNext())
        {
            var (isDirectory, length) = enumerator.Current;
            if (isDirectory)
            {
                directories++;
            }
            else
            {
                files++;
                bytes += length;
            }
        }

        return new WalkResult(files, directories, bytes, -1, enumerator.Errors, Log.Ms(started));
    }

    private sealed class CountingEnumerator(string root) : FileSystemEnumerator<(bool IsDirectory, long Length)>(root, s_options)
    {
        public long Errors { get; private set; }

        protected override (bool IsDirectory, long Length) TransformEntry(ref FileSystemEntry entry) => (entry.IsDirectory, entry.Length);

        protected override bool ShouldRecurseIntoEntry(ref FileSystemEntry entry) => (entry.Attributes & FileAttributes.ReparsePoint) == 0;

        protected override bool ContinueOnError(int error)
        {
            Errors++;
            return true;
        }
    }
}
