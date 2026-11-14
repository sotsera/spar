namespace Fuzz;

/// <summary>value: writer and round-trip checks; valid: a valid document in varied formatting; mutant: a mutated document.</summary>
internal enum CaseStream
{
    Value,
    Valid,
    Mutant,
}

internal sealed record Case(long Id, CaseStream Stream, Target Target, object? Value, byte[]? Input, string Ops);

/// <summary>Case <c>id</c> is a pure function of (seed, id), so any case can be regenerated for minimisation.</summary>
internal static class Cases
{
    public static Case Create(ulong seed, long id)
    {
        var rng = new Random(Mix(seed, id));
        var stream = (id % 10) switch { 0 or 1 => CaseStream.Value, 2 or 3 => CaseStream.Valid, _ => CaseStream.Mutant };
        var target = Targets.Pick(rng);
        var invalid = stream == CaseStream.Value && rng.Next(8) == 0;
        var large = rng.Next(40) == 0;
        var gen = new ValueGen(rng, invalid, large) { NoteDepth = rng.Next(6) };
        var value = target.Generate(gen);
        if (stream == CaseStream.Value)
        {
            return new Case(id, stream, target, value, null, invalid ? "value:invalid" : large ? "value:large" : "value");
        }

        var root = target.ToTree(value);
        var ops = new List<string>();
        if (stream == CaseStream.Valid)
        {
            if (rng.Next(3) == 0)
            {
                AddUnknown(rng, root);
                ops.Add("unknown-property");
            }

            var style = Style.Random(rng);
            ops.Add($"ws{style.Whitespace} esc{style.Escapes}{(style.AltNumbers ? " altnum" : string.Empty)}{(style.Shuffle ? " shuffle" : string.Empty)}");
            return new Case(id, stream, target, value, new Renderer(rng, style).Render(root), string.Join(' ', ops));
        }

        var treeOps = rng.Next(4);
        for (var i = 0; i < treeOps; i++)
        {
            root = Mutator.Tree(rng, root, ops);
        }

        var renderer = new Renderer(rng, Style.Random(rng));
        var bytes = renderer.Render(root);
        if (treeOps == 0 || rng.Next(2) == 0)
        {
            bytes = Mutator.Bytes(rng, bytes, renderer, ops);
        }

        return new Case(id, stream, target, value, bytes, string.Join(' ', ops));
    }

    private static void AddUnknown(Random rng, J root)
    {
        if (root is JObj obj)
        {
            var value = new Renderer(rng, Style.Plain).Render(new JObj(("a", new JArr()), ("b", new JStr("é\"")), ("c", JLit.Null())));
            obj.Props.Insert(rng.Next(obj.Props.Count + 1), new JProp("x_unknown", new JStr(string.Empty) { Raw = value }));
        }
    }

    private static int Mix(ulong seed, long id)
    {
        var z = seed * 0x9E3779B97F4A7C15UL + (ulong)id;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return (int)(z ^ (z >> 31));
    }
}
