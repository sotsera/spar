using System.Text;
using Bench;

namespace Fuzz;

/// <summary>
/// Random DTO values. With <c>invalid</c>, it also produces values no JSON writer can represent faithfully:
/// NaN and infinities, lone surrogates, and parent chains deeper than the reader's limit.
/// </summary>
internal sealed class ValueGen(Random rng, bool invalid, bool large)
{
    private static readonly int[] SpecialBmp =
    [
        0x00A0, 0x0085, 0x00AD, 0x061C, 0x200B, 0x200D, 0x2028, 0x2029, 0xE000, 0xF8FF, 0xFEFF, 0xFFFD, 0xFFFE, 0xFFFF,
    ];

    private static readonly int[] SpecialAstral = [0x10000, 0x1F600, 0x1F9D1, 0x20000, 0x2FFFF, 0x30000, 0xE0001, 0xF0000, 0x10FFFF];

    private static readonly int[] SpecialInts = [0, 1, -1, int.MaxValue, int.MinValue, int.MaxValue - 1, int.MinValue + 1, 255, 65536];

    private static readonly long[] SpecialLongs =
    [
        0, 1, -1, long.MaxValue, long.MinValue, long.MaxValue - 1, long.MinValue + 1, 9007199254740991, 9007199254740992,
        9007199254740993, -9007199254740991, -9007199254740992, -9007199254740993, int.MaxValue + 1L, int.MinValue - 1L,
        1_000_000_000_000_000_000,
    ];

    private static readonly double[] SpecialDoubles =
    [
        0.0, -0.0, double.Epsilon, -double.Epsilon, double.MaxValue, double.MinValue, 2.2250738585072014E-308,
        2.225073858507201E-308, 0.1, 0.2, 0.30000000000000004, 1.0 / 3, 2.0 / 3, 1e21, 1e-7, 1e-6, 1e20, 1e22, 1e23,
        9007199254740993.0, 9007199254740992.0, 123456789012345680000.0, 1.7976931348623157e308, 4.9e-324, 100, -1,
        0.5, 1e300, 1e-300, 5e-324, 1.5, 12345.678,
    ];

    private const string Sensitive = "\"\\/<>&'+`\u007F";

    private int _itemBudget = 5_000;
    private int _longStrings = 2;

    public Random Rng => rng;

    public int NoteDepth { get; init; } = 4;

    public bool Bool() => rng.Next(2) == 0;

    public T? MaybeNull<T>(Func<T> make)
        where T : class => rng.Next(4) == 0 ? null : make();

    public T? MaybeValue<T>(Func<T> make)
        where T : struct => rng.Next(4) == 0 ? null : make();

    public int Int() => rng.Next(3) switch
    {
        0 => SpecialInts[rng.Next(SpecialInts.Length)],
        1 => rng.Next(-1000, 1000),
        _ => (int)rng.NextInt64(int.MinValue, (long)int.MaxValue + 1),
    };

    public long Long() => rng.Next(3) switch
    {
        0 => SpecialLongs[rng.Next(SpecialLongs.Length)],
        1 => rng.Next(-1_000_000, 1_000_000),
        _ => rng.NextInt64(long.MinValue, long.MaxValue),
    };

    public double Double()
    {
        if (invalid && rng.Next(4) == 0)
        {
            return rng.Next(3) switch { 0 => double.NaN, 1 => double.PositiveInfinity, _ => double.NegativeInfinity };
        }

        switch (rng.Next(5))
        {
            case 0:
                return SpecialDoubles[rng.Next(SpecialDoubles.Length)];
            case 1:
                return Math.Round(rng.NextDouble() * 1000, rng.Next(0, 16));
            case 2:
                return rng.Next(-100, 100);
            default:
                while (true)
                {
                    var bits = BitConverter.Int64BitsToDouble(rng.NextInt64(long.MinValue, long.MaxValue));
                    if (double.IsFinite(bits))
                    {
                        return bits;
                    }
                }
        }
    }

    public string Str()
    {
        var k = rng.Next(100);
        if (invalid && k < 25)
        {
            return Lone();
        }

        return k switch
        {
            < 8 => string.Empty,
            < 30 => Ascii(rng.Next(1, 16)),
            < 32 when large && _longStrings-- > 0 => Mixed(new[] { 1_000, 10_000, 100_000, 300_000 }[rng.Next(4)]),
            _ => Mixed(rng.Next(1, 24)),
        };
    }

    public T[] Array<T>(Func<T> make)
    {
        var count = rng.Next(10) switch
        {
            < 3 => 0,
            < 9 => rng.Next(1, 6),
            _ => large ? rng.Next(50, 2000) : rng.Next(6, 12),
        };

        // Bound the whole value: nested large arrays would otherwise grow multiplicatively.
        count = Math.Min(count, Math.Max(0, _itemBudget));
        _itemBudget -= count;
        var items = new T[count];
        for (var i = 0; i < count; i++)
        {
            items[i] = make();
        }

        return items;
    }

    public Note Note(int depth)
    {
        if (invalid && depth == 0 && rng.Next(4) == 0)
        {
            return DeepNote(new[] { 63, 64, 65, 100, 999, 1000, 1001, 5000 }[rng.Next(8)]);
        }

        var parent = depth < NoteDepth && rng.Next(3) == 0 ? Note(depth + 1) : null;
        return new Note(Str(), Array(Str), parent, Double(), Bool(), MaybeValue(Long));
    }

    public Note[] Notes() => Array(() => Note(1));

    public Wide Wide(int depth) => new(
        Str(),
        MaybeNull(Str),
        Int(),
        MaybeValue(Int),
        Long(),
        MaybeValue(Long),
        Double(),
        MaybeValue(Double),
        Bool(),
        MaybeValue(Bool),
        Array(Str),
        Array(Int),
        Array(() => MaybeValue(Long)),
        Array(() => Array(Double)),
        MaybeNull(() => Array(() => MaybeNull(Str))),
        Note(2),
        MaybeNull(() => Note(2)),
        Array(() => Note(2)),
        depth < 2 ? MaybeNull(() => Array(() => MaybeNull(() => Wide(depth + 1)))) : null);

    /// <summary>A parent chain whose JSON nesting depth is <paramref name="depth"/>, built without recursion.</summary>
    public static Note DeepNote(int depth)
    {
        var note = new Note("d", [], null, 0, false, null);
        for (var i = 1; i < depth; i++)
        {
            note = new Note("d", [], note, 0, false, null);
        }

        return note;
    }

    private string Ascii(int length)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = (char)rng.Next(0x20, 0x7F);
        }

        return new string(chars);
    }

    private string Mixed(int length)
    {
        var sb = new StringBuilder(length + 8);
        while (sb.Length < length)
        {
            sb.Append(char.ConvertFromUtf32(CodePoint()));
        }

        return sb.ToString();
    }

    private int CodePoint() => rng.Next(10) switch
    {
        0 => rng.Next(0, 0x20),
        1 => Sensitive[rng.Next(Sensitive.Length)],
        2 => rng.Next(0x80, 0x800),
        3 => Bmp(),
        4 => SpecialBmp[rng.Next(SpecialBmp.Length)],
        5 => rng.Next(0x10000, 0x110000),
        6 => SpecialAstral[rng.Next(SpecialAstral.Length)],
        _ => rng.Next(0x20, 0x7F),
    };

    private int Bmp()
    {
        while (true)
        {
            var c = rng.Next(0x800, 0x10000);
            if (c is < 0xD800 or > 0xDFFF)
            {
                return c;
            }
        }
    }

    private string Lone()
    {
        var text = Mixed(rng.Next(0, 6));
        var at = rng.Next(text.Length + 1);
        while (at > 0 && at < text.Length && char.IsLowSurrogate(text[at]))
        {
            at--;
        }

        var bad = rng.Next(4) switch
        {
            0 => ((char)rng.Next(0xD800, 0xDC00)).ToString(),
            1 => ((char)rng.Next(0xDC00, 0xE000)).ToString(),
            2 => "\uDE00\uD83D",
            _ => "\uD83D",
        };
        return text.Insert(at, bad);
    }
}

/// <summary>Converts values to the mutable JSON tree, with the generator's camelCase names.</summary>
internal static class Trees
{
    public static J Note(Note n) => new JObj(
        ("text", Str(n.Text)),
        ("tags", Arr(n.Tags, Str)),
        ("parent", n.Parent is null ? JLit.Null() : Note(n.Parent)),
        ("score", Dbl(n.Score)),
        ("pinned", JLit.Bool(n.Pinned)),
        ("version", n.Version is long v ? Lng(v) : JLit.Null()));

    public static J Notes(Note[] notes) => Arr(notes, Note);

    public static J Wide(Wide w) => new JObj(
        ("name", Str(w.Name)),
        ("nick", w.Nick is null ? JLit.Null() : Str(w.Nick)),
        ("count", Int(w.Count)),
        ("limit", w.Limit is int l ? Int(l) : JLit.Null()),
        ("id", Lng(w.Id)),
        ("stamp", w.Stamp is long s ? Lng(s) : JLit.Null()),
        ("ratio", Dbl(w.Ratio)),
        ("weight", w.Weight is double d ? Dbl(d) : JLit.Null()),
        ("on", JLit.Bool(w.On)),
        ("maybe", w.Maybe is bool b ? JLit.Bool(b) : JLit.Null()),
        ("words", Arr(w.Words, Str)),
        ("ints", Arr(w.Ints, Int)),
        ("stamps", Arr(w.Stamps, x => x is long v ? Lng(v) : JLit.Null())),
        ("matrix", Arr(w.Matrix, row => Arr(row, Dbl))),
        ("labels", w.Labels is null ? JLit.Null() : Arr(w.Labels, x => x is null ? JLit.Null() : Str(x))),
        ("head", Note(w.Head)),
        ("tail", w.Tail is null ? JLit.Null() : Note(w.Tail)),
        ("items", Arr(w.Items, Note)),
        ("children", w.Children is null ? JLit.Null() : Arr(w.Children, x => x is null ? JLit.Null() : Wide(x))));

    public static J MaybeString(string? s) => s is null ? JLit.Null() : Str(s);

    public static J Int(int v) => new JNum(NumKind.Int, v, v);

    private static J Lng(long v) => new JNum(NumKind.Long, v, v);

    private static J Dbl(double v) => new JNum(NumKind.Double, 0, v);

    private static J Str(string s) => new JStr(s);

    private static J Arr<T>(IEnumerable<T> items, Func<T, J> make)
    {
        var array = new JArr();
        foreach (var item in items)
        {
            array.Items.Add(make(item));
        }

        return array;
    }
}
