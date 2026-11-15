using System.Text;

namespace Spar.Generator.Emit;

/// <summary>Writes generated C# that reads as hand-written: four-space indentation and <c>\n</c> line endings only.</summary>
internal sealed class CodeWriter
{
    private readonly StringBuilder _text = new();
    private int _indent;

    public CodeWriter Line(string line = "")
    {
        if (line.Length > 0)
        {
            _text.Append(' ', _indent * 4).Append(line);
        }

        _text.Append('\n');
        return this;
    }

    public CodeWriter Open(string line)
    {
        Line(line);
        Line("{");
        _indent++;
        return this;
    }

    public CodeWriter Close(string suffix = "")
    {
        _indent--;
        Line("}" + suffix);
        return this;
    }

    public override string ToString() => _text.ToString();
}
