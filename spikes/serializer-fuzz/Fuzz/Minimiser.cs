using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Fuzz;

/// <summary>
/// <c>minimise seed cmp id...</c>: shrinks each case's input with byte-level delta debugging while its class and
/// outcome keys stay the same. <c>describe seed id...</c>: prints what each writer did with a value case.
/// </summary>
internal static class Minimiser
{
    public static int Minimise(string[] args)
    {
        var seed = ulong.Parse(args[1], CultureInfo.InvariantCulture);
        var cmp = args[2];
        foreach (var arg in args.Skip(3))
        {
            var id = long.Parse(arg, CultureInfo.InvariantCulture);
            var c = Cases.Create(seed, id);
            var want = Worker.Compare(cmp, c.Target, c.Input!, null);
            var minimal = Shrink(c.Input!, candidate => Worker.Compare(cmp, c.Target, candidate, null) == want);
            Console.Out.Write(FormattableString.Invariant($"M\t{cmp}\t{id}\t{c.Input!.Length}\t{Convert.ToBase64String(minimal)}\n"));
        }

        return 0;
    }

    public static int Describe(string[] args)
    {
        var seed = ulong.Parse(args[1], CultureInfo.InvariantCulture);
        foreach (var arg in args.Skip(2))
        {
            var id = long.Parse(arg, CultureInfo.InvariantCulture);
            var c = Cases.Create(seed, id);
            var parts = new List<string>();
            foreach (var kind in new[] { WriterKind.A, WriterKind.B })
            {
                var w = c.Target.Write(kind, c.Value);
                parts.Add(w.Ok ? $"{kind} wrote {Text.Display(w.Bytes, 120)}" : $"{kind} threw {w.Key}");
            }

            var text = string.Join(" ; ", parts).Replace('\t', ' ').Replace('\n', ' ');
            Console.Out.Write(FormattableString.Invariant($"D\t{id}\t{Convert.ToBase64String(Encoding.UTF8.GetBytes(text))}\n"));
        }

        return 0;
    }

    /// <summary><c>scan seed from to</c>: prints text cases where A~S is EQ but F~S is not (a fix that rejects valid input).</summary>
    public static int Scan(string[] args)
    {
        var seed = ulong.Parse(args[1], CultureInfo.InvariantCulture);
        var from = long.Parse(args[2], CultureInfo.InvariantCulture);
        var to = long.Parse(args[3], CultureInfo.InvariantCulture);
        for (var id = from; id < to; id++)
        {
            if (id % 10 < 2)
            {
                continue;
            }

            var c = Cases.Create(seed, id);
            var a = Worker.Compare("A~S", c.Target, c.Input!, null);
            var f = Worker.Compare("F~S", c.Target, c.Input!, null);
            if (a.Class == "EQ" && f.Class != "EQ")
            {
                Console.WriteLine($"{id}\t{c.Target.Name}\t{c.Ops}\t{f.Class}\t{f.Key}\t{Text.Display(c.Input, 400)}");
            }
        }

        return 0;
    }

    /// <summary><c>probe target json</c>: shows what A, F, B and S do with one hand-written input.</summary>
    public static int Probe(string[] args)
    {
        var target = new[] { Targets.Note, Targets.Notes, Targets.Wide, Targets.Int, Targets.String }.First(t => t.Name == args[1]);
        var input = Encoding.UTF8.GetBytes(args[2]);
        foreach (var kind in Enum.GetValues<ReaderKind>())
        {
            var o = target.Read(kind, input);
            var shown = !o.Ok ? o.Key : o.Message == "canon failed" ? "ok, but the value has no JSON form (canonical write failed)" : "ok " + Text.Display(o.Bytes, 160);
            Console.WriteLine($"{kind}: {shown}");
        }

        return 0;
    }

    /// <summary>Classic ddmin over bytes, bounded by evaluations and time.</summary>
    public static byte[] Shrink(byte[] input, Func<byte[], bool> keeps)
    {
        var current = input;
        var granularity = 2;
        var evaluations = 0;
        var clock = Stopwatch.StartNew();
        while (current.Length >= 2 && evaluations < 5000 && clock.Elapsed < TimeSpan.FromSeconds(15))
        {
            var chunk = (int)Math.Ceiling(current.Length / (double)granularity);
            var reduced = false;
            for (var start = 0; start < current.Length; start += chunk)
            {
                byte[] candidate = [.. current[..start], .. current[Math.Min(current.Length, start + chunk)..]];
                evaluations++;
                if (keeps(candidate))
                {
                    current = candidate;
                    granularity = Math.Max(granularity - 1, 2);
                    reduced = true;
                    break;
                }
            }

            if (!reduced)
            {
                if (granularity >= current.Length)
                {
                    break;
                }

                granularity = Math.Min(current.Length, granularity * 2);
            }
        }

        return current;
    }
}
