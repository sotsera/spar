namespace Spar.Tool.Tests;

/// <summary>
/// The rule for code in the app-author skills: each fenced block in a checked language has a
/// <c>&lt;!-- source: path --&gt;</c> marker right above it, naming a file under one of the allowed folders, and its
/// lines are consecutive lines of that file, compared without their leading and trailing spaces. A line that is only
/// <c>// …</c> or <c>&lt;!-- … --&gt;</c> stands for any number of lines left out.
/// </summary>
internal static class Excerpts
{
    private const string Marker = "<!-- source: ";

    public static IEnumerable<string> Check(IReadOnlyList<string> lines, string root, IReadOnlyList<string> sources, IReadOnlySet<string> languages)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index].Trim();
            if (!line.StartsWith("```", StringComparison.Ordinal) || line.Length == 3)
            {
                continue;
            }

            var language = line[3..].Trim();
            var close = index + 1;
            while (close < lines.Count && lines[close].Trim() != "```")
            {
                close++;
            }

            var block = lines.Skip(index + 1).Take(close - index - 1).Select(text => text.Trim()).ToList();
            var at = index + 1;
            var marker = index > 0 ? lines[index - 1].Trim() : "";
            index = close;
            if (!languages.Contains(language))
            {
                continue;
            }

            if (!marker.StartsWith(Marker, StringComparison.Ordinal) || !marker.EndsWith(" -->", StringComparison.Ordinal))
            {
                yield return $"the {language} block at line {at} has no <!-- source: … --> marker above it.";
                continue;
            }

            var path = marker[Marker.Length..^4].Trim();
            if (!sources.Any(folder => path.StartsWith(folder, StringComparison.Ordinal)) || path.Contains("..", StringComparison.Ordinal))
            {
                yield return $"the block at line {at} names {path}, not a file Spar builds ({string.Join(", ", sources)}).";
                continue;
            }

            var file = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(file))
            {
                yield return $"the block at line {at} names {path}, which does not exist.";
                continue;
            }

            var source = File.ReadAllLines(file).Select(text => text.Trim()).ToList();
            if (!Matches(block, source))
            {
                yield return $"the block at line {at} is not consecutive lines of {path}.";
            }
        }
    }

    /// <summary>Whether the block's pieces, split at its gap lines, are found in the source in order.</summary>
    private static bool Matches(List<string> block, List<string> source)
    {
        var pieces = new List<List<string>> { new() };
        foreach (var line in block)
        {
            if (line is "// …" or "<!-- … -->" or "# …")
            {
                pieces.Add([]);
            }
            else
            {
                pieces[^1].Add(line);
            }
        }

        // The first piece may start anywhere; each later one anywhere after the end of the one before it.
        var from = 0;
        for (var piece = 0; piece < pieces.Count; piece++)
        {
            var found = Find(source, pieces[piece], from);
            if (found < 0)
            {
                return false;
            }

            from = found + pieces[piece].Count;
        }

        return true;
    }

    private static int Find(List<string> source, List<string> piece, int from)
    {
        for (var start = from; start + piece.Count <= source.Count; start++)
        {
            var all = true;
            for (var offset = 0; offset < piece.Count && all; offset++)
            {
                all = source[start + offset] == piece[offset];
            }

            if (all)
            {
                return start;
            }
        }

        return -1;
    }
}
