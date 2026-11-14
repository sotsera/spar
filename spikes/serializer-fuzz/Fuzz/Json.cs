using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Fuzz;

internal enum NumKind
{
    Int,
    Long,
    Double,
}

/// <summary>A mutable JSON tree. <see cref="Raw"/>, when set, is written verbatim instead of the node.</summary>
internal abstract class J
{
    public byte[]? Raw { get; set; }
}

internal sealed class JProp(string name, J value)
{
    public string Name { get; set; } = name;

    public byte[]? RawName { get; set; }

    public J Value { get; set; } = value;
}

internal sealed class JObj : J
{
    public JObj(params (string Name, J Value)[] props)
    {
        foreach (var (name, value) in props)
        {
            Props.Add(new JProp(name, value));
        }
    }

    public List<JProp> Props { get; } = [];
}

internal sealed class JArr : J
{
    public List<J> Items { get; } = [];
}

internal sealed class JStr(string value) : J
{
    public string Value { get; } = value;
}

internal sealed class JNum(NumKind kind, long integer, double real) : J
{
    public NumKind Kind { get; } = kind;

    public long Integer { get; } = integer;

    public double Real { get; } = real;
}

internal sealed class JLit(string text) : J
{
    public string Text { get; } = text;

    public static JLit Null() => new("null");

    public static JLit Bool(bool value) => new(value ? "true" : "false");
}

/// <summary>Formatting choices that keep a document valid and its value unchanged.</summary>
internal sealed class Style
{
    /// <summary>0 none, 1 single spaces, 2 random JSON whitespace.</summary>
    public int Whitespace { get; init; }

    /// <summary>0 like the default encoder (\u for non-ASCII), 1 raw UTF-8, 2 \u for everything, 3 mixed per character.</summary>
    public int Escapes { get; init; }

    /// <summary>Spell numbers in equivalent alternative forms.</summary>
    public bool AltNumbers { get; init; }

    public bool Shuffle { get; init; }

    public static Style Plain { get; } = new();

    public static Style Random(Random rng) => new()
    {
        Whitespace = rng.Next(3),
        Escapes = rng.Next(4),
        AltNumbers = rng.Next(2) == 0,
        Shuffle = rng.Next(3) == 0,
    };
}

/// <summary>Renders a tree, recording where whitespace may go and where string contents are.</summary>
internal sealed class Renderer(Random rng, Style style)
{
    private static readonly byte[] WhitespaceBytes = " \t\n\r"u8.ToArray();

    private readonly List<byte> _out = [];

    public List<int> Slots { get; } = [];

    public List<(int Start, int End, bool IsName)> Strings { get; } = [];

    public byte[] Render(J root)
    {
        Space();
        Node(root);
        Space();
        return [.. _out];
    }

    private void Space()
    {
        Slots.Add(_out.Count);
        var count = style.Whitespace switch { 0 => 0, 1 => rng.Next(2), _ => rng.Next(4) == 0 ? rng.Next(1, 5) : 0 };
        for (var i = 0; i < count; i++)
        {
            _out.Add(style.Whitespace == 1 ? (byte)' ' : WhitespaceBytes[rng.Next(WhitespaceBytes.Length)]);
        }
    }

    private void Node(J node)
    {
        if (node.Raw is { } raw)
        {
            _out.AddRange(raw);
            return;
        }

        switch (node)
        {
            case JObj obj:
                _out.Add((byte)'{');
                var props = style.Shuffle ? [.. obj.Props.OrderBy(_ => rng.Next())] : obj.Props;
                for (var i = 0; i < props.Count; i++)
                {
                    Space();
                    if (i > 0)
                    {
                        _out.Add((byte)',');
                        Space();
                    }

                    if (props[i].RawName is { } rawName)
                    {
                        _out.AddRange(rawName);
                    }
                    else
                    {
                        String(props[i].Name, isName: true);
                    }

                    Space();
                    _out.Add((byte)':');
                    Space();
                    Node(props[i].Value);
                }

                Space();
                _out.Add((byte)'}');
                break;
            case JArr arr:
                _out.Add((byte)'[');
                for (var i = 0; i < arr.Items.Count; i++)
                {
                    Space();
                    if (i > 0)
                    {
                        _out.Add((byte)',');
                        Space();
                    }

                    Node(arr.Items[i]);
                }

                Space();
                _out.Add((byte)']');
                break;
            case JStr str:
                String(str.Value, isName: false);
                break;
            case JNum num:
                Ascii(Number(num));
                break;
            case JLit lit:
                Ascii(lit.Text);
                break;
        }
    }

    private void String(string value, bool isName)
    {
        _out.Add((byte)'"');
        var start = _out.Count;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            var pair = char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]);
            var mode = style.Escapes == 3 ? rng.Next(3) : style.Escapes;
            if (c < 0x20)
            {
                var shortForm = c switch { '\b' => "\\b", '\f' => "\\f", '\n' => "\\n", '\r' => "\\r", '\t' => "\\t", _ => null };
                Ascii(shortForm is not null && rng.Next(2) == 0 ? shortForm : Hex(c));
            }
            else if (c == '"' || c == '\\')
            {
                Ascii(rng.Next(2) == 0 ? "\\" + c : Hex(c));
            }
            else if (c == '/')
            {
                Ascii(rng.Next(3) switch { 0 => "/", 1 => "\\/", _ => Hex(c) });
            }
            else if (c < 0x80)
            {
                if (mode == 2)
                {
                    Ascii(Hex(c));
                }
                else
                {
                    _out.Add((byte)c);
                }
            }
            else if (mode == 1)
            {
                var text = pair ? value.Substring(i, 2) : c.ToString();
                _out.AddRange(Encoding.UTF8.GetBytes(text));
                if (pair)
                {
                    i++;
                }
            }
            else
            {
                Ascii(Hex(c));
                if (pair)
                {
                    Ascii(Hex(value[++i]));
                }
            }
        }

        Strings.Add((start, _out.Count, isName));
        _out.Add((byte)'"');
    }

    private string Hex(char c)
    {
        var hex = ((int)c).ToString("X4", CultureInfo.InvariantCulture);
        return "\\u" + (rng.Next(2) == 0 ? hex : hex.ToLowerInvariant());
    }

    private string Number(JNum num)
    {
        if (num.Kind != NumKind.Double)
        {
            return num.Integer == 0 && style.AltNumbers && rng.Next(3) == 0
                ? "-0"
                : num.Integer.ToString(CultureInfo.InvariantCulture);
        }

        var d = num.Real;
        var shortest = d.ToString("R", CultureInfo.InvariantCulture);
        if (!style.AltNumbers)
        {
            return shortest;
        }

        var text = rng.Next(7) switch
        {
            0 => d.ToString("G17", CultureInfo.InvariantCulture),
            1 => d.ToString("E" + rng.Next(16, 40).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture),
            2 => d.ToString("E" + rng.Next(40, 800).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture),
            3 when d == Math.Floor(d) && Math.Abs(d) < 1e300 => d.ToString("F" + rng.Next(0, 4).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture),
            4 when !shortest.Contains('E', StringComparison.Ordinal) => shortest + (rng.Next(2) == 0 ? "e0" : "E+00"),
            5 => d.ToString("E2", CultureInfo.InvariantCulture) is var rounded && double.Parse(rounded, CultureInfo.InvariantCulture) == d ? rounded : shortest,
            _ => shortest,
        };
        return rng.Next(2) == 0 ? text.Replace('E', 'e') : text;
    }

    private void Ascii(string text)
    {
        foreach (var c in text)
        {
            _out.Add((byte)c);
        }
    }
}

internal static partial class Text
{
    /// <summary>Removes positions, paths and literal values from an exception message so causes group together.</summary>
    public static string Normalize(string message)
    {
        foreach (var marker in new[] { " Path:", " LineNumber:", " | " })
        {
            var at = message.IndexOf(marker, StringComparison.Ordinal);
            if (at >= 0)
            {
                message = message[..at];
            }
        }

        message = Quoted().Replace(message, "'?'");
        message = Digits().Replace(message, "#");
        message = message.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
        return message.Length > 110 ? message[..110] : message;
    }

    /// <summary>Shows bytes as text: printable ASCII as is, everything else as \xHH.</summary>
    public static string Display(ReadOnlySpan<byte> bytes, int max = 200)
    {
        var sb = new StringBuilder();
        foreach (var b in bytes)
        {
            if (sb.Length >= max)
            {
                sb.Append($"… ({bytes.Length:N0} bytes)");
                break;
            }

            if (b is >= 0x20 and < 0x7F && b != (byte)'`' && b != (byte)'|')
            {
                sb.Append((char)b);
            }
            else
            {
                sb.Append(CultureInfo.InvariantCulture, $"\\x{b:X2}");
            }
        }

        return sb.ToString();
    }

    // Keeps quoted identifiers (property and type names); replaces quoted input characters and values.
    [GeneratedRegex("'(?![A-Za-z][A-Za-z0-9_.]+')[^']*'")]
    private static partial Regex Quoted();

    [GeneratedRegex("[0-9]+")]
    private static partial Regex Digits();
}
