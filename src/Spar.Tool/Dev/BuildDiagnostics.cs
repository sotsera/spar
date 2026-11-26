using System.Globalization;
using System.Text.RegularExpressions;

namespace Spar.Tool.Dev;

/// <summary>
/// One error or warning of a build, at its file, line and column when it has them: the C# compiler's, Spar's own
/// (SPAR001 and the others) and the frontend's type-check (decision "M2b's decide-first questions", item 6), all as
/// MSBuild reports them.
/// </summary>
internal sealed record BuildDiagnostic(string Severity, string Code, string Message, string? File, int? Line, int? Column, string? Project)
{
    /// <summary><c>path(line,col): message</c>, or the message alone, as an editor's problem list reads it.</summary>
    public string Location => File is null
        ? ""
        : Line is { } line
            ? Column is { } column ? $"{File}({line},{column})" : $"{File}({line})"
            : File;
}

/// <summary>
/// MSBuild's canonical diagnostic lines, as its file logger writes them with <c>ErrorsOnly</c> or <c>WarningsOnly</c>:
/// <c>origin(line,col[,endLine,endCol]): error CODE: message [project]</c>, where the origin is a file or a tool
/// (<c>CSC</c>, <c>MSBUILD</c>), optionally after a node prefix such as <c>1:7&gt;</c>.
/// </summary>
internal static partial class BuildDiagnostics
{
    /// <summary>The diagnostics of <paramref name="lines"/>, each once, in their order.</summary>
    public static IReadOnlyList<BuildDiagnostic> Parse(IEnumerable<string> lines)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var found = new List<BuildDiagnostic>();
        foreach (var line in lines)
        {
            if (ParseLine(line) is { } diagnostic && seen.Add(diagnostic.ToString()))
            {
                found.Add(diagnostic);
            }
        }

        return found;
    }

    public static BuildDiagnostic? ParseLine(string line)
    {
        var match = Canonical().Match(line);
        if (!match.Success)
        {
            return null;
        }

        var origin = match.Groups["origin"].Value.Trim();
        var hasPlace = match.Groups["line"].Success;
        var isFile = hasPlace || LooksLikePath(origin);
        return new BuildDiagnostic(
            match.Groups["severity"].Value,
            match.Groups["code"].Value,
            match.Groups["message"].Value.Trim(),
            isFile ? origin : null,
            hasPlace ? int.Parse(match.Groups["line"].Value, CultureInfo.InvariantCulture) : null,
            match.Groups["column"].Success ? int.Parse(match.Groups["column"].Value, CultureInfo.InvariantCulture) : null,
            match.Groups["project"].Success ? match.Groups["project"].Value : null);
    }

    /// <summary>A tool's name (<c>CSC</c>, <c>MSBUILD</c>, <c>EXEC</c>) has no folder separator; a file's path does.</summary>
    private static bool LooksLikePath(string origin) => origin.Contains('\\', StringComparison.Ordinal) || origin.Contains('/', StringComparison.Ordinal);

    [GeneratedRegex(
        @"^\s*(?:\d+(?::\d+)?>)?(?<origin>.*?)(?:\((?<line>\d+)(?:,(?<column>\d+))?(?:,\d+,\d+)?\))?\s*:\s*(?<severity>error|warning)\s+(?<code>[A-Za-z]+\d+)?\s*:\s*(?<message>.*?)(?:\s+\[(?<project>[^\]]+)\])?\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex Canonical();
}
