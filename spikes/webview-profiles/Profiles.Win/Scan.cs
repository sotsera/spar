using System.Text;

namespace Spar.WebViewProfiles;

/// <summary>Looks for a page-written token in a UDF's files (ASCII and UTF-16LE), to see where data lands on disk.</summary>
internal static class Scan
{
    public static List<string> FilesContaining(string root, string token)
    {
        var hits = new List<string>();
        if (!Directory.Exists(root))
        {
            return hits;
        }

        var ascii = Encoding.ASCII.GetBytes(token);
        var wide = Encoding.Unicode.GetBytes(token);
        foreach (var file in new DirectoryInfo(root).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
        {
            if (file.Length > 64 * 1024 * 1024)
            {
                continue;
            }

            try
            {
                using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var content = new byte[stream.Length];
                stream.ReadExactly(content);
                if (content.AsSpan().IndexOf(ascii) >= 0 || content.AsSpan().IndexOf(wide) >= 0)
                {
                    hits.Add(Path.GetRelativePath(root, file.FullName));
                }
            }
            catch (IOException)
            {
                hits.Add("locked:" + Path.GetRelativePath(root, file.FullName));
            }
            catch (UnauthorizedAccessException)
            {
                hits.Add("denied:" + Path.GetRelativePath(root, file.FullName));
            }
        }

        return hits;
    }

    public static List<string> WrittenSince(string root, DateTime sinceUtc)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        return new DirectoryInfo(root).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
            .Where(f => f.LastWriteTimeUtc >= sinceUtc)
            .Select(f => $"{Path.GetRelativePath(root, f.FullName)}:{f.Length / 1024}kB")
            .ToList();
    }
}
