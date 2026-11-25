using System.Text.RegularExpressions;

namespace Spar.Tool.Frontend;

/// <summary>
/// <c>SparFrontendPath</c> in the app's project file, as text, so the file keeps its layout: an existing value replaced
/// in place, or the property added at the end of the first unconditional <c>PropertyGroup</c>, with a comment.
/// </summary>
internal static partial class ProjectFileEdit
{
    /// <summary>The project text with <c>SparFrontendPath</c> set to <paramref name="value"/>, or null when it has no place for it.</summary>
    public static string? SetFrontendPath(string text, string value)
    {
        var existing = Property().Match(text);
        if (existing.Success)
        {
            var inner = existing.Groups["value"];
            return text[..inner.Index] + value + text[(inner.Index + inner.Length)..];
        }

        var group = FirstGroup().Match(text);
        if (!group.Success)
        {
            return null;
        }

        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var groupIndent = group.Groups["indent"].Value;
        var itemIndent = groupIndent + (groupIndent.Contains('\t') ? "\t" : "  ");
        var close = text.IndexOf("</PropertyGroup>", group.Index, StringComparison.Ordinal);
        if (close < 0)
        {
            return null;
        }

        var lineStart = text.LastIndexOf('\n', close) + 1;
        var addition = $"{newline}{itemIndent}<!-- The page's folder (spar init frontend); frontend/ when it is not set. -->{newline}{itemIndent}<SparFrontendPath>{value}</SparFrontendPath>{newline}";
        var before = text[..lineStart].TrimEnd('\r', '\n');
        return before + newline + addition + text[lineStart..];
    }

    [GeneratedRegex(@"<SparFrontendPath(?:\s[^>]*)?>(?<value>[^<]*)</SparFrontendPath>")]
    private static partial Regex Property();

    [GeneratedRegex(@"(?m)^(?<indent>[ \t]*)<PropertyGroup\s*>")]
    private static partial Regex FirstGroup();
}
