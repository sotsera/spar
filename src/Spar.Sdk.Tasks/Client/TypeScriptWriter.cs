using System.Text;
using System.Text.Json;

namespace Spar.Sdk.Tasks.Client;

/// <summary>
/// Generated TypeScript and JavaScript, indented like hand-written code, with <c>\n</c> line endings only. A line can
/// carry the C# declaration its name comes from; <see cref="SourceMap"/> turns those into a declaration map, so
/// go-to-definition on the name opens the C# (plan section 2, "Generated client").
/// </summary>
internal sealed class TypeScriptWriter
{
    private const string Base64 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

    private readonly StringBuilder _text = new();
    private readonly List<(int Line, int Column, SourceSpot Source)> _mappings = [];
    private int _depth;
    private int _line;

    /// <summary>
    /// Writes one line. With <paramref name="source"/>, the first <paramref name="name"/> on the line maps to that C#
    /// declaration.
    /// </summary>
    public TypeScriptWriter Line(string line = "", SourceSpot? source = null, string? name = null)
    {
        if (line.Length > 0)
        {
            var indent = _depth * 2;
            if (source is { } spot)
            {
                var at = name is null ? 0 : Math.Max(0, line.IndexOf(name, StringComparison.Ordinal));
                _mappings.Add((_line, indent + at, spot));
            }

            _text.Append(' ', indent).Append(line);
        }

        _text.Append('\n');
        _line++;
        return this;
    }

    /// <summary>Writes a line that opens a block, and indents what follows.</summary>
    public TypeScriptWriter Open(string line, SourceSpot? source = null, string? name = null)
    {
        Line(line, source, name);
        _depth++;
        return this;
    }

    public TypeScriptWriter Close(string line = "}")
    {
        _depth--;
        return Line(line);
    }

    /// <summary>Ends an indented stretch that has no closing line, such as a <c>case</c>.</summary>
    public TypeScriptWriter Outdent()
    {
        _depth--;
        return this;
    }

    /// <summary>A one-line JSDoc comment, when there is documentation.</summary>
    public TypeScriptWriter Doc(string? text) => text is null ? this : Line($"/** {TypeScriptNames.Doc(text)} */");

    public override string ToString() => _text.ToString();

    /// <summary>
    /// The source map (version 3) of what was written as <paramref name="file"/>. Each source is the C# file's path
    /// relative to the project, behind <paramref name="sourcePrefix"/>: the way from the map's folder to the project.
    /// </summary>
    public string SourceMap(string file, string sourcePrefix)
    {
        var sources = _mappings.Select(m => m.Source.File).Distinct(StringComparer.Ordinal).ToList();
        var mappings = new StringBuilder();
        int previousSource = 0, previousSourceLine = 0, previousSourceColumn = 0;
        var byLine = _mappings.ToLookup(m => m.Line);
        for (var line = 0; line < _line; line++)
        {
            if (line > 0)
            {
                mappings.Append(';');
            }

            var previousColumn = 0;
            var first = true;
            foreach (var (_, column, source) in byLine[line].OrderBy(m => m.Column))
            {
                if (!first)
                {
                    mappings.Append(',');
                }

                first = false;
                var index = sources.IndexOf(source.File);
                Vlq(mappings, column - previousColumn);
                Vlq(mappings, index - previousSource);
                Vlq(mappings, source.Line - previousSourceLine);
                Vlq(mappings, source.Column - previousSourceColumn);
                (previousColumn, previousSource, previousSourceLine, previousSourceColumn) = (column, index, source.Line, source.Column);
            }
        }

        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteNumber("version", 3);
            json.WriteString("file", file);
            json.WriteString("sourceRoot", "");
            json.WriteStartArray("sources");
            foreach (var source in sources)
            {
                json.WriteStringValue(sourcePrefix + source);
            }

            json.WriteEndArray();
            json.WriteStartArray("names");
            json.WriteEndArray();
            json.WriteString("mappings", mappings.ToString());
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>One value in base-64 VLQ: the sign in the lowest bit, then five bits per digit, low digits first.</summary>
    private static void Vlq(StringBuilder text, int value)
    {
        var rest = value < 0 ? ((-value) << 1) | 1 : value << 1;
        do
        {
            var digit = rest & 31;
            rest >>= 5;
            text.Append(Base64[rest > 0 ? digit | 32 : digit]);
        }
        while (rest > 0);
    }
}
