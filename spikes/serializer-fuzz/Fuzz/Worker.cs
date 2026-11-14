using System.Globalization;

namespace Fuzz;

/// <summary>
/// Runs cases [from, to) in this process and prints <c>P id</c> after each one, so the coordinator can attribute a
/// crash or hang to the next case. Prints the batch statistics and <c>DONE</c> at the end.
/// </summary>
internal static class Worker
{
    public static readonly string[] TextComparisons = ["A~B", "F~B", "A~S", "F~S"];

    public static int Run(string[] args)
    {
        var seed = ulong.Parse(args[1], CultureInfo.InvariantCulture);
        var from = long.Parse(args[2], CultureInfo.InvariantCulture);
        var to = long.Parse(args[3], CultureInfo.InvariantCulture);
        var crashAt = Option(args, "--inject-crash");
        var hangAt = Option(args, "--inject-hang");
        var stats = new Stats();
        var jsDocs = new List<(long Id, byte[] Bytes)>();
        var output = Console.Out;
        for (var id = from; id < to; id++)
        {
            if (id == crashAt)
            {
                Environment.FailFast("Injected crash (harness self-test).");
            }

            if (id == hangAt)
            {
                Thread.Sleep(Timeout.Infinite);
            }

            var c = Cases.Create(seed, id);
            if (c.Stream == CaseStream.Value)
            {
                RunValue(c, stats, jsDocs);
            }
            else
            {
                RunText(c, stats);
            }

            output.Write(FormattableString.Invariant($"P {id}\n"));
        }

        JsCheck.Run(jsDocs, stats);
        stats.Write(output);
        output.Write("DONE\n");
        output.Flush();
        return 0;
    }

    public static (string Class, string Key) Classify(Outcome x, Outcome y, byte[]? source)
    {
        string cls;
        if (!x.Ok && !x.IsJsonException)
        {
            cls = "X-nonjson";
        }
        else if (x.Ok && x.Message == "canon failed")
        {
            // Left accepted a value that no JSON writer can write back (an infinity from an overflowing literal).
            cls = "X-unwritable";
        }
        else if (x.Ok && y.Ok)
        {
            cls = !x.Bytes.AsSpan().SequenceEqual(y.Bytes) ? "NE"
                : source is not null && !x.Bytes.AsSpan().SequenceEqual(source) ? "EQ-src" : "EQ";
        }
        else if (!x.Ok && !y.Ok)
        {
            cls = "RJ";
        }
        else
        {
            cls = x.Ok ? "XA-YR" : "XR-YA";
        }

        var yKey = y.Ok ? "ok" : cls == "RJ" && y.IsJsonException ? "JsonException" : y.Key;
        var key = cls == "EQ" ? "-" : $"x={x.Key} | y={yKey}";
        return (cls, key);
    }

    public static (string Class, string Key) Compare(string cmp, Target target, byte[] input, byte[]? source)
    {
        var (x, y) = cmp switch
        {
            "A~B" => (ReaderKind.A, ReaderKind.B),
            "F~B" => (ReaderKind.F, ReaderKind.B),
            "A~S" => (ReaderKind.A, ReaderKind.S),
            _ => (ReaderKind.F, ReaderKind.S),
        };
        return Classify(target.Read(x, input), target.Read(y, input), source);
    }

    private static void RunText(Case c, Stats stats)
    {
        var input = c.Input!;
        var a = c.Target.Read(ReaderKind.A, input);
        var f = c.Target.Read(ReaderKind.F, input);
        var b = c.Target.Read(ReaderKind.B, input);
        var s = c.Target.Read(ReaderKind.S, input);
        var source = c.Stream == CaseStream.Valid ? c.Target.Canon(c.Value) : null;
        Record(stats, "A~B", c, Classify(a, b, source), input.Length);
        Record(stats, "F~B", c, Classify(f, b, source), input.Length);
        Record(stats, "A~S", c, Classify(a, s, source), input.Length);
        Record(stats, "F~S", c, Classify(f, s, source), input.Length);
    }

    private static void Record(Stats stats, string cmp, Case c, (string Class, string Key) result, int length) =>
        stats.Add(cmp, c.Stream, result.Class, result.Key, c.Id, length);

    private static void RunValue(Case c, Stats stats, List<(long, byte[])> jsDocs)
    {
        var labels = ValueChecks(c, out var aOutput, out var length);
        if (labels.Count > 0)
        {
            stats.Add("W", c.Stream, "FAIL", string.Join("; ", labels), c.Id, length);
        }
        else if (aOutput is null)
        {
            // Every writer refused the value the same way B did (NaN, infinity, lone surrogate, depth).
            var b = c.Target.Write(WriterKind.B, c.Value);
            stats.Add("W", c.Stream, "PASS-both-throw", "B throws " + b.Key, c.Id, length);
        }
        else
        {
            stats.Add("W", c.Stream, "PASS", "-", c.Id, length);
        }
        if (aOutput is not null)
        {
            jsDocs.Add((c.Id, aOutput));
        }
    }

    public static List<string> ValueChecks(Case c, out byte[]? aOutput, out int length)
    {
        var target = c.Target;
        var writes = new Dictionary<WriterKind, Outcome>();
        foreach (var kind in Enum.GetValues<WriterKind>())
        {
            writes[kind] = target.Write(kind, c.Value);
        }

        var labels = new List<string>();
        var b = writes[WriterKind.B];
        foreach (var kind in new[] { WriterKind.A, WriterKind.A2, WriterKind.F, WriterKind.F2 })
        {
            var w = writes[kind];
            if (w.Ok != b.Ok)
            {
                labels.Add($"{kind} {(w.Ok ? "writes" : "throws " + w.Key)} / B {(b.Ok ? "writes" : "throws " + b.Key)}");
            }
            else if (!w.Ok && w.Kind != b.Kind)
            {
                labels.Add($"{kind} throws {w.Key} / B throws {b.Key}");
            }
            else if (w.Ok && !w.Bytes.AsSpan().SequenceEqual(b.Bytes))
            {
                labels.Add($"{kind} bytes != B bytes");
            }
        }

        foreach (var (writer, reader) in new[] { (WriterKind.A, ReaderKind.A), (WriterKind.A2, ReaderKind.A), (WriterKind.F, ReaderKind.F), (WriterKind.F2, ReaderKind.F), (WriterKind.A, ReaderKind.B) })
        {
            var w = writes[writer];
            if (!w.Ok)
            {
                continue;
            }

            var r = target.Read(reader, w.Bytes!);
            if (!r.Ok)
            {
                labels.Add($"{reader} cannot read {writer} output: {r.Key}");
            }
            else if (!target.ReadsBackExactly(reader, w.Bytes!, c.Value))
            {
                labels.Add($"read{reader}(write{writer}(x)) != x");
            }
        }

        aOutput = writes[WriterKind.A].Ok ? writes[WriterKind.A].Bytes : null;
        length = aOutput?.Length ?? b.Bytes?.Length ?? 0;
        return labels;
    }

    private static long Option(string[] args, string name)
    {
        var at = Array.IndexOf(args, name);
        return at >= 0 && at + 1 < args.Length ? long.Parse(args[at + 1], CultureInfo.InvariantCulture) : -1;
    }
}
