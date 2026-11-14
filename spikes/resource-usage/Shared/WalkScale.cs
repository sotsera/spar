namespace Spar.ResourceUsage;

/// <summary>
/// Walk cost against file count, without a browser: creates a tree of small files under the given folder (which must
/// be inside artifacts/resource-usage/) and times each walk method over it.
/// </summary>
internal static class WalkScale
{
    public static int Run(Options options, params (string Name, Func<string, WalkResult> Walk)[] extra)
    {
        var root = Path.GetFullPath(options.Get("path") ?? throw new ArgumentException("--path is required."));
        if (!root.Replace(Path.DirectorySeparatorChar, '/').Contains("/artifacts/resource-usage/", StringComparison.Ordinal))
        {
            throw new ArgumentException("--path must be inside artifacts/resource-usage/.");
        }

        var iterations = options.Int("iterations", 10);
        var data = new byte[options.Int("bytes", 2048)];
        foreach (var count in (options.Get("counts") ?? "1000,5000,20000").Split(',').Select(int.Parse))
        {
            var folder = Path.Combine(root, "files-" + count);
            if (!Directory.Exists(folder))
            {
                for (var i = 0; i < count; i++)
                {
                    var directory = Path.Combine(folder, (i / 100).ToString("D4", System.Globalization.CultureInfo.InvariantCulture));
                    Directory.CreateDirectory(directory);
                    File.WriteAllBytes(Path.Combine(directory, i.ToString(System.Globalization.CultureInfo.InvariantCulture)), data);
                }
            }

            (string Name, Func<string, WalkResult> Walk)[] methods = [("FileInfo", Walk.FileInfos), ("FileSystemEnumerator", Walk.Entries), ("PerFile", Walk.PerFile), .. extra];
            foreach (var (name, walk) in methods)
            {
                var first = walk(folder);
                var times = new List<double>(iterations);
                var last = first;
                for (var i = 0; i < iterations; i++)
                {
                    last = walk(folder);
                    times.Add(last.Ms);
                }

                Log.Line("WALK-SCALE", $"files={count} method={name} first=({first}) {last} repeat=({Stats.Of(times, "ms")}) usPerFile={times.Order().ElementAt(times.Count / 2) * 1000 / count:F2}");
            }
        }

        return 0;
    }
}
