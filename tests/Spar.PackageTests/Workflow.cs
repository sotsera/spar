using System.Text;

namespace Spar.PackageTests;

/// <summary>
/// A GitHub Actions workflow read from its file, as far as the tests need it: the workflow's <c>env</c>, and each job's
/// matrix of runners and its steps. The file is read with <see cref="Yaml"/>, a reader of the YAML subset workflows are
/// written in (block mappings and sequences, plain and quoted scalars, flow sequences, <c>|</c> blocks, comments), so a
/// workflow that leaves that subset fails to read rather than being misread.
/// </summary>
internal sealed class Workflow
{
    private Workflow(IReadOnlyDictionary<string, object?> root) => Root = root;

    public IReadOnlyDictionary<string, object?> Root { get; }

    /// <summary>The workflow's own <c>env</c>, the variables every step gets.</summary>
    public IReadOnlyDictionary<string, string> Env =>
        Root.TryGetValue("env", out var env) ? Strings((IReadOnlyDictionary<string, object?>)env!) : new Dictionary<string, string>();

    public IReadOnlyDictionary<string, object?> Job(string name) =>
        (IReadOnlyDictionary<string, object?>)((IReadOnlyDictionary<string, object?>)Root["jobs"]!)[name]!;

    /// <summary>The runners a job's matrix names (<c>strategy.matrix.os</c>).</summary>
    public IReadOnlyList<string> MatrixOs(string job)
    {
        var strategy = (IReadOnlyDictionary<string, object?>)Job(job)["strategy"]!;
        var matrix = (IReadOnlyDictionary<string, object?>)strategy["matrix"]!;
        return [.. ((IReadOnlyList<object?>)matrix["os"]!).Cast<string>()];
    }

    public IReadOnlyList<WorkflowStep> Steps(string job) =>
        [.. ((IReadOnlyList<object?>)Job(job)["steps"]!).Select(step => new WorkflowStep(Strings((IReadOnlyDictionary<string, object?>)step!)))];

    public static Workflow Read(string path) =>
        new((IReadOnlyDictionary<string, object?>)Yaml.Read(File.ReadAllText(path))!);

    /// <summary>A mapping's scalar values; nested values (a step's <c>with</c>) are left out.</summary>
    private static Dictionary<string, string> Strings(IReadOnlyDictionary<string, object?> mapping) =>
        mapping.Where(entry => entry.Value is string).ToDictionary(entry => entry.Key, entry => (string)entry.Value!, StringComparer.Ordinal);
}

/// <summary>One step of a job: its name, its action or its commands, its condition and its folder.</summary>
internal sealed record WorkflowStep(IReadOnlyDictionary<string, string> Values)
{
    public string? Name => Values.GetValueOrDefault("name");

    public string? Uses => Values.GetValueOrDefault("uses");

    public string? Run => Values.GetValueOrDefault("run");

    public string? If => Values.GetValueOrDefault("if");

    public string? WorkingDirectory => Values.GetValueOrDefault("working-directory");

    public override string ToString() => Name ?? Uses ?? Run ?? "a step";
}

/// <summary>
/// Reads the YAML subset of the workflow files the template ships: block mappings and sequences by indentation, plain
/// and quoted scalars, flow sequences of scalars, literal blocks (<c>|</c>), and comments. Mappings are
/// <see cref="IReadOnlyDictionary{TKey,TValue}"/>, sequences <see cref="IReadOnlyList{T}"/>, scalars strings. Anything
/// else (anchors, folded blocks, flow mappings, tags, several documents) throws.
/// </summary>
internal static class Yaml
{
    public static object? Read(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var position = 0;
        SkipBlank(lines, ref position);
        if (position == lines.Length)
        {
            return null;
        }

        var value = Block(lines, ref position, Indent(lines[position]));
        SkipBlank(lines, ref position);
        if (position != lines.Length)
        {
            throw new FormatException($"Line {position + 1} is outside the document's structure: {lines[position]}");
        }

        return value;
    }

    /// <summary>The block (mapping or sequence) whose lines start at <paramref name="indent"/>.</summary>
    private static object Block(string[] lines, ref int position, int indent)
    {
        var line = lines[position][indent..];
        return line.StartsWith("- ", StringComparison.Ordinal) || line == "-"
            ? Sequence(lines, ref position, indent)
            : Mapping(lines, ref position, indent, null);
    }

    private static List<object?> Sequence(string[] lines, ref int position, int indent)
    {
        var items = new List<object?>();
        while (Next(lines, ref position, indent))
        {
            var line = lines[position][indent..];
            if (!line.StartsWith('-'))
            {
                throw new FormatException($"Line {position + 1} is not an item of the sequence above it: {lines[position]}");
            }

            var rest = line.Length > 1 ? line[2..] : "";
            if (rest.Length == 0)
            {
                position++;
                SkipBlank(lines, ref position);
                items.Add(position < lines.Length && Indent(lines[position]) > indent ? Block(lines, ref position, Indent(lines[position])) : null);
            }
            else if (IsKey(rest))
            {
                // A mapping whose first key shares the item's line: its keys are indented to that key's column.
                items.Add(Mapping(lines, ref position, indent + 2, rest));
            }
            else
            {
                items.Add(Scalar(rest, position));
                position++;
            }
        }

        return items;
    }

    private static Dictionary<string, object?> Mapping(string[] lines, ref int position, int indent, string? first)
    {
        var mapping = new Dictionary<string, object?>(StringComparer.Ordinal);
        while (first is not null || Next(lines, ref position, indent))
        {
            var line = first ?? lines[position][indent..];
            first = null;
            if (!IsKey(line))
            {
                throw new FormatException($"Line {position + 1} is not a key of the mapping above it: {lines[position]}");
            }

            var colon = KeyEnd(line);
            var key = Unquote(line[..colon].Trim(), position);
            var rest = StripComment(line[(colon + 1)..]).Trim();
            object? value;
            if (rest == "|" || rest == "|-")
            {
                value = Literal(lines, ref position, indent, rest == "|-");
            }
            else if (rest.Length > 0)
            {
                value = Scalar(rest, position);
                position++;
            }
            else
            {
                position++;
                SkipBlank(lines, ref position);
                // A sequence may sit at the key's own indentation, as workflows often write it.
                value = position < lines.Length && (Indent(lines[position]) > indent
                    || (Indent(lines[position]) == indent && lines[position][indent..].StartsWith("- ", StringComparison.Ordinal)))
                    ? Block(lines, ref position, Indent(lines[position]))
                    : null;
            }

            if (!mapping.TryAdd(key, value))
            {
                throw new FormatException($"The key '{key}' appears twice (line {position}).");
            }
        }

        return mapping;
    }

    /// <summary>A literal block: the lines indented deeper than its key, without that indentation.</summary>
    private static string Literal(string[] lines, ref int position, int indent, bool strip)
    {
        position++;
        var text = new StringBuilder();
        int? own = null;
        while (position < lines.Length && (lines[position].Trim().Length == 0 || Indent(lines[position]) > indent))
        {
            var line = lines[position];
            if (line.Trim().Length == 0)
            {
                text.Append('\n');
            }
            else
            {
                own ??= Indent(line);
                if (Indent(line) < own)
                {
                    throw new FormatException($"Line {position + 1} is less indented than its block's first line.");
                }

                text.Append(line[own.Value..]).Append('\n');
            }

            position++;
        }

        var value = text.ToString().TrimEnd('\n');
        return strip ? value : value + "\n";
    }

    private static object Scalar(string text, int position)
    {
        text = StripComment(text).Trim();
        if (text.StartsWith('['))
        {
            if (!text.EndsWith(']'))
            {
                throw new FormatException($"Line {position + 1}: a flow sequence must end on its line.");
            }

            return text[1..^1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(item => (object?)Unquote(item, position)).ToList();
        }

        if (text.Length > 0 && "{&*!>%@`".Contains(text[0], StringComparison.Ordinal))
        {
            throw new FormatException($"Line {position + 1} uses YAML this reader does not read: {text}");
        }

        return Unquote(text, position);
    }

    private static string Unquote(string text, int position)
    {
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
        {
            if (text.Contains('\\', StringComparison.Ordinal))
            {
                throw new FormatException($"Line {position + 1}: escapes in double-quoted scalars are not read.");
            }

            return text[1..^1];
        }

        return text.Length >= 2 && text[0] == '\'' && text[^1] == '\'' ? text[1..^1].Replace("''", "'", StringComparison.Ordinal) : text;
    }

    /// <summary>The text before a comment: a <c>#</c> at the start or after a space, outside quotes.</summary>
    private static string StripComment(string text)
    {
        char? quote = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quote is not null)
            {
                quote = c == quote ? null : quote;
            }
            else if (c is '"' or '\'' && (i == 0 || text[i - 1] == ' '))
            {
                quote = c;
            }
            else if (c == '#' && (i == 0 || text[i - 1] == ' '))
            {
                return text[..i];
            }
        }

        return text;
    }

    private static bool IsKey(string line) => KeyEnd(line) > 0;

    /// <summary>The colon that ends a key (followed by a space or the line's end), outside quotes; -1 when there is none.</summary>
    private static int KeyEnd(string line)
    {
        var text = StripComment(line);
        char? quote = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quote is not null)
            {
                quote = c == quote ? null : quote;
            }
            else if (c is '"' or '\'' && i == 0)
            {
                quote = c;
            }
            else if (c == ':' && (i + 1 == text.Length || text[i + 1] == ' '))
            {
                return i;
            }
            else if (c is '[' or '{' && i == 0)
            {
                return -1;
            }
        }

        return -1;
    }

    /// <summary>Moves to the next line of content; true when it belongs to the block at <paramref name="indent"/>.</summary>
    private static bool Next(string[] lines, ref int position, int indent)
    {
        SkipBlank(lines, ref position);
        if (position == lines.Length || Indent(lines[position]) < indent)
        {
            return false;
        }

        if (Indent(lines[position]) > indent)
        {
            throw new FormatException($"Line {position + 1} is indented deeper than its block: {lines[position]}");
        }

        return true;
    }

    /// <summary>Skips empty lines and lines that are only a comment.</summary>
    private static void SkipBlank(string[] lines, ref int position)
    {
        while (position < lines.Length && (lines[position].Trim().Length == 0 || lines[position].TrimStart().StartsWith('#')))
        {
            position++;
        }
    }

    private static int Indent(string line)
    {
        var indent = 0;
        while (indent < line.Length && line[indent] == ' ')
        {
            indent++;
        }

        if (indent < line.Length && line[indent] == '\t')
        {
            throw new FormatException("A tab indents a line: YAML indents with spaces.");
        }

        return indent;
    }
}
