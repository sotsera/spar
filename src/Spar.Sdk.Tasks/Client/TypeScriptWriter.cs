using System.Text;

namespace Spar.Sdk.Tasks.Client;

/// <summary>Generated TypeScript and JavaScript, indented like hand-written code, with <c>\n</c> line endings only.</summary>
internal sealed class TypeScriptWriter
{
    private readonly StringBuilder _text = new();
    private int _depth;

    public TypeScriptWriter Line(string line = "")
    {
        if (line.Length > 0)
        {
            _text.Append(' ', _depth * 2).Append(line);
        }

        _text.Append('\n');
        return this;
    }

    /// <summary>Writes a line that opens a block, and indents what follows.</summary>
    public TypeScriptWriter Open(string line)
    {
        Line(line);
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
}
