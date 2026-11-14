using System.Text;

namespace Fuzz;

/// <summary>Tree and byte mutations of valid documents. Each applied operation is recorded by name.</summary>
internal static class Mutator
{
    private static readonly string[] TypeSwaps =
    [
        "\"x\"", "\"\"", "0", "1.5", "-1", "true", "false", "null", "[]", "{}", "[1]", "[\"a\"]", "{\"a\":1}", "\"2.5\"",
        "\"1\"", "\"true\"", "\"null\"", "[null]", "[[]]", "2147483648", "1e2", "\"-0\"", "\" 1\"", "\"0x10\"", "\"NaN\"",
        "\"Infinity\"",
    ];

    private static readonly string[] Numbers =
    [
        "01", "-01", "+1", ".5", "1.", "1e", "1e+", "-", "--1", "1e999", "-1e999", "1e-999", "1.5", "2147483648",
        "-2147483649", "9223372036854775808", "-9223372036854775809", "-0", "0e0", "1E2", "1e2", "NaN", "Infinity",
        "-Infinity", "0x10", "1_000", "1,5", "\uFF11", "0.0000000000000000000000000000001", "1" + new string('0', 400),
        "4.9e-324", "2.4e-324", "1.7976931348623159e308", "18446744073709551616", "-0.0", "1.0", "100e-2", "0.1e1",
        "1E400", "0." + new string('0', 400) + "1", "1." + new string('9', 1000), "9007199254740993", "-", "1e2.5",
    ];

    private static readonly string[] Strings =
    [
        "\"\u0001\"", "\"\\x\"", "\"\\u12\"", "\"\\u12G4\"", "\"\\ud800\"", "\"\\udc00\"", "\"\\udc00\\ud800\"",
        "\"\\ud800x\"", "\"\\ud83d\\ude00\"", "'x'", "\"abc", "\"\\\"", "\"\t\"", "\"\u007F\"", "\"\\/\"", "\"\\U0041\"",
        "\"\\u0000\"", "\"\\ud800\\u0041\"", "\"\\uDBFF\\uDFFF\"", "\"\n\"", "\"\\\u00e9\"", "\"\\a\"",
    ];

    private static readonly string[] Literals = ["True", "FALSE", "nul", "tru", "nulll", "undefined", "NaN", "Infinity", "None", "t", "n"];

    private static readonly string[] Trailing =
    [
        " 42", "{}", "x", "\0", ",", "]", "}", " ", "\r\n\t ", "/*c*/", "//c", "\uFEFF", "null", "\"s\"", " \n", "[]",
    ];

    private static readonly string[] Comments = ["/*c*/", "//c\n", "/**/", "/* unterminated", "#c\n", "// no newline"];

    private static readonly byte[][] BadUtf8 =
    [
        [0xFF], [0xC0, 0x80], [0x80], [0xE2, 0x82], [0xED, 0xA0, 0x80], [0xED, 0xBF, 0xBF], [0xF4, 0x90, 0x80, 0x80],
        [0xF8, 0x88, 0x80, 0x80, 0x80], [0xC3], [0xE0, 0x80, 0x80], [0xF0, 0x80, 0x80, 0x80], [0xFE],
    ];

    private static readonly string[] ValidWhitespace = [" ", "\t", "\n", "\r", "\r\n"];

    private static readonly string[] InvalidWhitespace = ["\v", "\f", "\u00A0", "\u3000", "\u2028", "\0", "\uFEFF"];

    private static readonly int[] WrapDepths = [1, 2, 10, 62, 63, 64, 65, 100, 1000, 100_000];

    private static readonly int[] ChainDepths = [1, 30, 60, 61, 62, 63, 64, 65, 100, 1000, 10_000];

    /// <summary>Applies one tree mutation. Returns the new root (some mutations replace it).</summary>
    public static J Tree(Random rng, J root, List<string> ops)
    {
        var objects = new List<JObj>();
        var arrays = new List<JArr>();
        var slots = new List<(J Node, Action<J> Set)>();
        var newRoot = root;
        Collect(root, v => newRoot = v, objects, arrays, slots);

        switch (rng.Next(18))
        {
            case 0 when Any(objects, o => o.Props.Count > 0, rng, out var o):
                o.Props.RemoveAt(rng.Next(o.Props.Count));
                ops.Add("drop-property");
                break;
            case 1 when Any(objects, o => o.Props.Count > 0, rng, out var o):
                var original = o.Props[rng.Next(o.Props.Count)];
                var duplicate = new JProp(original.Name, rng.Next(2) == 0 ? original.Value : Raw(TypeSwaps[rng.Next(TypeSwaps.Length)]));
                o.Props.Insert(rng.Next(o.Props.Count + 1), duplicate);
                ops.Add("duplicate-property");
                break;
            case 2 when Any(objects, o => o.Props.Count > 1, rng, out var o):
                var shuffled = o.Props.OrderBy(_ => rng.Next()).ToList();
                o.Props.Clear();
                o.Props.AddRange(shuffled);
                ops.Add("reorder");
                break;
            case 3 when Any(objects, o => o.Props.Count > 0, rng, out var o):
                var prop = o.Props[rng.Next(o.Props.Count)];
                ops.Add("rename:" + Rename(rng, prop));
                break;
            case 4 when slots.Count > 0:
                slots[rng.Next(slots.Count)].Set(Raw(TypeSwaps[rng.Next(TypeSwaps.Length)]));
                ops.Add("type-swap");
                break;
            case 5 when slots.Count > 0:
                slots[rng.Next(slots.Count)].Set(JLit.Null());
                ops.Add("null-out");
                break;
            case 6 when Any(slots, s => s.Node is JNum, rng, out var s):
                s.Set(Raw(Numbers[rng.Next(Numbers.Length)]));
                ops.Add("number-text");
                break;
            case 7 when Any(slots, s => s.Node is JStr, rng, out var s):
                s.Set(Raw(Strings[rng.Next(Strings.Length)]));
                ops.Add("string-text");
                break;
            case 8 when Any(slots, s => s.Node is JLit, rng, out var s):
                s.Set(Raw(Literals[rng.Next(Literals.Length)]));
                ops.Add("literal-text");
                break;
            case 9 when objects.Count + arrays.Count > 0:
                TrailingComma(rng, objects, arrays);
                ops.Add("trailing-comma");
                break;
            case 10 when objects.Count > 0:
                var target2 = objects[rng.Next(objects.Count)];
                target2.Props.Insert(rng.Next(target2.Props.Count + 1), new JProp("x_" + rng.Next(100), Raw(TypeSwaps[rng.Next(TypeSwaps.Length)])));
                ops.Add("unknown-property");
                break;
            case 11 when slots.Count > 0:
                var depth = WrapDepths[rng.Next(WrapDepths.Length)];
                var arraysWrap = rng.Next(2) == 0;
                var closed = rng.Next(8) != 0;
                slots[rng.Next(slots.Count)].Set(new JStr("") { Raw = Wrap(depth, arraysWrap, closed) });
                ops.Add($"deep-wrap:{(arraysWrap ? "array" : "object")}:{depth}{(closed ? string.Empty : ":unclosed")}");
                break;
            case 12 when Any(objects, o => o.Props.Any(p => p.Name == "parent"), rng, out var o):
                var chain = ChainDepths[rng.Next(ChainDepths.Length)];
                o.Props.First(p => p.Name == "parent").Value = new JStr("") { Raw = Chain(chain) };
                ops.Add($"deep-chain:{chain}");
                break;
            case 13 when Any(objects, o => o.Props.Any(p => p.Value is JLit { Text: "null" }), rng, out var o):
                o.Props.Remove(o.Props.First(p => p.Value is JLit { Text: "null" }));
                ops.Add("omit-null-property");
                break;
            case 14:
                var kind = rng.Next(8);
                newRoot = kind switch
                {
                    0 => JLit.Null(),
                    1 => new JArr(),
                    2 => new JObj(),
                    3 => new JStr("x"),
                    4 => new JNum(NumKind.Int, 1, 1),
                    5 => WrapArray(root),
                    6 => new JObj(("value", root)),
                    _ => Raw(string.Empty),
                };
                ops.Add("root-" + kind);
                break;
            case 15 when arrays.Count + objects.Count > 0:
                Separators(rng, objects, arrays, ops);
                break;
            case 16 when Any(arrays, a => a.Items.Count > 0, rng, out var a):
                a.Items.Insert(rng.Next(a.Items.Count + 1), Raw(TypeSwaps[rng.Next(TypeSwaps.Length)]));
                ops.Add("array-extra-item");
                break;
            case 17 when Any(objects, o => o.Props.Count > 0, rng, out var o):
                o.Props[rng.Next(o.Props.Count)].RawName = Encoding.UTF8.GetBytes(new[] { "text", "Text", "\"\"", "\"te\"\"xt\"", "1", "\"text\" \"x\"", "'text'" }[rng.Next(7)]);
                ops.Add("bad-name");
                break;
            default:
                ops.Add("none");
                break;
        }

        return newRoot;
    }

    /// <summary>Applies one byte-level mutation using the renderer's whitespace slots and string spans.</summary>
    public static byte[] Bytes(Random rng, byte[] doc, Renderer renderer, List<string> ops)
    {
        var slot = renderer.Slots.Count > 0 ? renderer.Slots[rng.Next(renderer.Slots.Count)] : 0;
        switch (rng.Next(9))
        {
            case 0:
                ops.Add("truncate");
                return doc[..rng.Next(doc.Length)];
            case 1:
                var tail = Trailing[rng.Next(Trailing.Length)];
                ops.Add("trailing:" + Text.Display(Encoding.UTF8.GetBytes(tail)));
                return [.. doc, .. Encoding.UTF8.GetBytes(tail)];
            case 2:
                ops.Add("bom");
                return [0xEF, 0xBB, 0xBF, .. doc];
            case 3:
                ops.Add("comment");
                return Insert(doc, slot, Encoding.UTF8.GetBytes(Comments[rng.Next(Comments.Length)]));
            case 4 when renderer.Strings.Count > 0:
                var span = renderer.Strings[rng.Next(renderer.Strings.Count)];
                ops.Add(span.IsName ? "bad-utf8-name" : "bad-utf8-string");
                return Insert(doc, rng.Next(span.Start, span.End + 1), BadUtf8[rng.Next(BadUtf8.Length)]);
            case 5:
                ops.Add("whitespace-valid");
                return Insert(doc, slot, Encoding.UTF8.GetBytes(ValidWhitespace[rng.Next(ValidWhitespace.Length)]));
            case 6:
                var ws = InvalidWhitespace[rng.Next(InvalidWhitespace.Length)];
                ops.Add("whitespace-invalid:" + Text.Display(Encoding.UTF8.GetBytes(ws)));
                return Insert(doc, slot, Encoding.UTF8.GetBytes(ws));
            case 7 when doc.Length > 0:
                var at = rng.Next(doc.Length);
                var copy = (byte[])doc.Clone();
                switch (rng.Next(3))
                {
                    case 0:
                        copy[at] ^= (byte)(1 << rng.Next(8));
                        ops.Add("bit-flip");
                        return copy;
                    case 1:
                        ops.Add("byte-delete");
                        return [.. doc[..at], .. doc[(at + 1)..]];
                    default:
                        ops.Add("byte-insert");
                        return Insert(doc, at, [(byte)rng.Next(256)]);
                }

            default:
                ops.Add("leading-trailing-whitespace");
                return [.. " \n"u8, .. doc, .. "\t "u8];
        }
    }

    private static void Collect(J node, Action<J> set, List<JObj> objects, List<JArr> arrays, List<(J, Action<J>)> slots)
    {
        if (node.Raw is not null)
        {
            return;
        }

        slots.Add((node, set));
        switch (node)
        {
            case JObj obj:
                objects.Add(obj);
                foreach (var prop in obj.Props)
                {
                    Collect(prop.Value, v => prop.Value = v, objects, arrays, slots);
                }

                break;
            case JArr arr:
                arrays.Add(arr);
                for (var i = 0; i < arr.Items.Count; i++)
                {
                    var index = i;
                    Collect(arr.Items[i], v => arr.Items[index] = v, objects, arrays, slots);
                }

                break;
        }
    }

    private static bool Any<T>(List<T> items, Func<T, bool> predicate, Random rng, out T picked)
    {
        var matches = items.Where(predicate).ToList();
        picked = matches.Count > 0 ? matches[rng.Next(matches.Count)] : default!;
        return matches.Count > 0;
    }

    private static J Raw(string text) => new JStr(string.Empty) { Raw = Encoding.UTF8.GetBytes(text) };

    private static JArr WrapArray(J root)
    {
        var array = new JArr();
        array.Items.Add(root);
        return array;
    }

    private static string Rename(Random rng, JProp prop)
    {
        var name = prop.Name;
        if (name.Length < 2)
        {
            prop.Name = "text";
            return "to-text";
        }

        switch (rng.Next(7))
        {
            case 0:
                prop.Name = char.ToUpperInvariant(name[0]) + name[1..];
                return "pascal";
            case 1:
                prop.Name = name.ToUpperInvariant();
                return "upper";
            case 2:
                prop.Name = name + " ";
                return "space";
            case 3:
                prop.Name = name.Replace('e', '\u0435');
                return "homoglyph";
            case 4:
                prop.Name = name[..^1];
                return "prefix";
            case 5:
                prop.Name = name + "\0";
                return "nul-suffix";
            default:
                prop.Name = string.Empty;
                return "empty";
        }
    }

    private static void TrailingComma(Random rng, List<JObj> objects, List<JArr> arrays)
    {
        var pick = rng.Next(objects.Count + arrays.Count);
        if (pick < objects.Count)
        {
            var obj = objects[pick];
            if (obj.Props.Count == 0)
            {
                obj.Raw = "{,}"u8.ToArray();
            }
            else
            {
                var last = obj.Props[^1];
                last.Value = new JStr(string.Empty) { Raw = [.. Render(last.Value), (byte)','] };
            }
        }
        else
        {
            var arr = arrays[pick - objects.Count];
            if (arr.Items.Count == 0)
            {
                arr.Raw = "[,]"u8.ToArray();
            }
            else
            {
                arr.Items[^1] = new JStr(string.Empty) { Raw = [.. Render(arr.Items[^1]), (byte)','] };
            }
        }
    }

    private static void Separators(Random rng, List<JObj> objects, List<JArr> arrays, List<string> ops)
    {
        var pick = rng.Next(objects.Count + arrays.Count);
        var bad = rng.Next(4);
        if (pick < objects.Count && objects[pick].Props.Count > 0)
        {
            var prop = objects[pick].Props[rng.Next(objects[pick].Props.Count)];
            var value = Render(prop.Value);
            prop.Value = new JStr(string.Empty)
            {
                Raw = bad switch { 0 => [.. ",,"u8, .. value], 1 => [.. "::"u8, .. value], 2 => [.. value, .. ",,"u8], _ => [.. value, .. " "u8, .. value] },
            };
            ops.Add("separator-object-" + bad);
        }
        else if (pick >= objects.Count && arrays[pick - objects.Count].Items.Count > 0)
        {
            var arr = arrays[pick - objects.Count];
            var index = rng.Next(arr.Items.Count);
            var value = Render(arr.Items[index]);
            arr.Items[index] = new JStr(string.Empty)
            {
                Raw = bad switch { 0 => [.. ",,"u8, .. value], 1 => [.. value, .. " "u8, .. value], 2 => [.. value, .. ":"u8, .. value], _ => [.. ","u8, .. value] },
            };
            ops.Add("separator-array-" + bad);
        }
        else
        {
            ops.Add("none");
        }
    }

    private static byte[] Render(J node) => new Renderer(new Random(0), Style.Plain).Render(node);

    private static byte[] Wrap(int depth, bool arrays, bool closed)
    {
        var open = arrays ? "["u8 : "{\"a\":"u8;
        var close = arrays ? "]"u8 : "}"u8;
        var bytes = new List<byte>(depth * (open.Length + 1) + 1);
        for (var i = 0; i < depth; i++)
        {
            bytes.AddRange(open);
        }

        bytes.Add((byte)'0');
        if (closed)
        {
            for (var i = 0; i < depth; i++)
            {
                bytes.AddRange(close);
            }
        }

        return [.. bytes];
    }

    /// <summary>A Note parent chain of the given depth, as text, built without recursion.</summary>
    public static byte[] Chain(int depth)
    {
        var open = "{\"text\":\"d\",\"tags\":[],\"score\":0,\"pinned\":false,\"version\":null,\"parent\":"u8;
        var leaf = "{\"text\":\"d\",\"tags\":[],\"parent\":null,\"score\":0,\"pinned\":false,\"version\":null}"u8;
        var bytes = new List<byte>((depth * (open.Length + 1)) + leaf.Length);
        for (var i = 1; i < depth; i++)
        {
            bytes.AddRange(open);
        }

        bytes.AddRange(leaf);
        for (var i = 1; i < depth; i++)
        {
            bytes.Add((byte)'}');
        }

        return [.. bytes];
    }

    private static byte[] Insert(byte[] doc, int at, byte[] bytes) => [.. doc[..at], .. bytes, .. doc[at..]];
}
