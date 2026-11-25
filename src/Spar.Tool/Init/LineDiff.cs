using System.Text;

namespace Spar.Tool.Init;

/// <summary>
/// A unified diff of two texts by lines (the longest common subsequence, three lines of context), as
/// <c>spar init frontend</c> shows its edits before writing them. The files it edits are small.
/// </summary>
internal static class LineDiff
{
    private const int Context = 3;

    public static string Unified(string path, string? before, string after)
    {
        var old = Lines(before ?? "");
        var updated = Lines(after);
        var edits = Edits(old, updated);
        var text = new StringBuilder();
        text.Append("--- ").Append(before is null ? "/dev/null" : "a/" + path).Append('\n');
        text.Append("+++ b/").Append(path).Append('\n');

        var index = 0;
        while (index < edits.Count)
        {
            if (edits[index].Kind == ' ')
            {
                index++;
                continue;
            }

            // One hunk: from three lines before this change to three lines after the last change that is near it.
            var start = Math.Max(0, index - Context);
            var end = index;
            var last = index;
            while (end < edits.Count && end - last <= Context * 2)
            {
                if (edits[end].Kind != ' ')
                {
                    last = end;
                }

                end++;
            }

            end = Math.Min(edits.Count, last + Context + 1);
            var hunk = edits.GetRange(start, end - start);
            var oldStart = edits[start].Old + 1;
            var newStart = edits[start].New + 1;
            var oldCount = hunk.Count(e => e.Kind != '+');
            var newCount = hunk.Count(e => e.Kind != '-');
            text.Append($"@@ -{(oldCount == 0 ? oldStart - 1 : oldStart)},{oldCount} +{(newCount == 0 ? newStart - 1 : newStart)},{newCount} @@\n");
            foreach (var edit in hunk)
            {
                text.Append(edit.Kind).Append(edit.Text).Append('\n');
            }

            index = end;
        }

        return text.ToString();
    }

    private static string[] Lines(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        return text.EndsWith('\n') ? lines[..^1] : lines;
    }

    /// <summary>Every line of both texts in order, each kept (' '), removed ('-') or added ('+'), with its line numbers.</summary>
    private static List<Edit> Edits(string[] old, string[] updated)
    {
        var common = new int[old.Length + 1, updated.Length + 1];
        for (var i = old.Length - 1; i >= 0; i--)
        {
            for (var j = updated.Length - 1; j >= 0; j--)
            {
                common[i, j] = old[i] == updated[j] ? common[i + 1, j + 1] + 1 : Math.Max(common[i + 1, j], common[i, j + 1]);
            }
        }

        var edits = new List<Edit>();
        int x = 0, y = 0;
        while (x < old.Length || y < updated.Length)
        {
            if (x < old.Length && y < updated.Length && old[x] == updated[y])
            {
                edits.Add(new Edit(' ', old[x], x, y));
                x++;
                y++;
            }
            else if (x < old.Length && (y == updated.Length || common[x + 1, y] >= common[x, y + 1]))
            {
                // A removal before the addition that replaces it, as diff shows them.
                edits.Add(new Edit('-', old[x], x, y));
                x++;
            }
            else
            {
                edits.Add(new Edit('+', updated[y], x, y));
                y++;
            }
        }

        return edits;
    }

    private readonly record struct Edit(char Kind, string Text, int Old, int New);
}
