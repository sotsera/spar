using System.Text;
using System.Text.Json;
using Spar.Generated;

namespace Bench;

/// <summary>Checks that (A) and (B) agree on the workloads, then probes edge cases where they may differ.</summary>
internal static class Correctness
{
    private const string Rest = "\"tags\":[],\"parent\":null,\"score\":1,\"pinned\":false,\"version\":null";

    public static int Run()
    {
        var failures = 0;
        failures += CheckWorkload("small", Workload.Small(), BenchContractCodec.SerializeSaveAsync,
            v => JsonSerializer.SerializeToUtf8Bytes(v, BenchJsonContext.Default.Note),
            j => BenchContractCodec.ParseSaveAsync(j), j => JsonSerializer.Deserialize(j, BenchJsonContext.Default.Note)!,
            NoteEquals);
        foreach (var count in new[] { 50, 2000 })
        {
            failures += CheckWorkload($"array[{count}]", Workload.Array(count), BenchContractCodec.SerializeSaveManyAsync,
                v => JsonSerializer.SerializeToUtf8Bytes(v, BenchJsonContext.Default.NoteArray),
                j => BenchContractCodec.ParseSaveManyAsync(j),
                j => JsonSerializer.Deserialize(j, BenchJsonContext.Default.NoteArray)!,
                (x, y) => x.Length == y.Length && x.Zip(y).All(p => NoteEquals(p.First, p.Second)));
        }

        Console.WriteLine();
        Console.WriteLine("Edge cases (single Note). A = generated, B = System.Text.Json context:");
        Console.WriteLine("| Case | A | B |");
        Console.WriteLine("|---|---|---|");
        Probe("all properties present", "{\"text\":\"t\"," + Rest + "}");
        Probe("nullable `version` omitted", "{\"text\":\"t\",\"tags\":[],\"parent\":null,\"score\":1,\"pinned\":false}");
        Probe("nullable `parent` and `version` omitted", "{\"text\":\"t\",\"tags\":[],\"score\":1,\"pinned\":false}");
        Probe("required `text` omitted", "{" + Rest + "}");
        Probe("`text` is null", "{\"text\":null," + Rest + "}");
        Probe("`tags` is null", "{\"text\":\"t\",\"tags\":null,\"parent\":null,\"score\":1,\"pinned\":false,\"version\":null}");
        Probe("`tags` contains null", "{\"text\":\"t\",\"tags\":[null],\"parent\":null,\"score\":1,\"pinned\":false,\"version\":null}");
        Probe("`pinned` is null", "{\"text\":\"t\",\"tags\":[],\"parent\":null,\"score\":1,\"pinned\":null,\"version\":null}");
        Probe("unknown property", "{\"text\":\"t\",\"extra\":{\"a\":[1,2]}," + Rest + "}");
        Probe("PascalCase `Text`", "{\"Text\":\"t\"," + Rest + "}");
        Probe("`score` as string \"2.5\"", "{\"text\":\"t\",\"tags\":[],\"parent\":null,\"score\":\"2.5\",\"pinned\":false,\"version\":null}");
        Probe("`version` is 1.5", "{\"text\":\"t\",\"tags\":[],\"parent\":null,\"score\":1,\"pinned\":false,\"version\":1.5}");
        Probe("`version` above long.MaxValue",
            "{\"text\":\"t\",\"tags\":[],\"parent\":null,\"score\":1,\"pinned\":false,\"version\":9223372036854775808}");
        Probe("duplicate `text`", "{\"text\":\"first\",\"text\":\"second\"," + Rest + "}");
        Probe("escaped property name `\\u0074ext`", "{\"\\u0074ext\":\"t\"," + Rest + "}");
        Probe("trailing comma", "{\"text\":\"t\"," + Rest + ",}");
        Probe("comment", "{/*c*/\"text\":\"t\"," + Rest + "}");
        Probe("trailing content after the object", "{\"text\":\"t\"," + Rest + "} 42");
        Probe("top-level null", "null");
        Probe("empty payload", "");
        Probe("truncated object", "{\"text\":\"t\",\"tags\":[");

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "WORKLOADS: A and B agree." : $"WORKLOADS: {failures} mismatch(es).");
        return failures == 0 ? 0 : 1;
    }

    private static int CheckWorkload<T>(
        string name,
        T value,
        Func<T, byte[]> serializeA,
        Func<T, byte[]> serializeB,
        Func<byte[], T> deserializeA,
        Func<byte[], T> deserializeB,
        Func<T, T, bool> equals)
    {
        var a = serializeA(value);
        var b = serializeB(value);
        var bytesEqual = a.AsSpan().SequenceEqual(b);
        var fromA = deserializeA(a);
        var fromB = deserializeB(a);
        var aEquals = equals(value, fromA);
        var bEquals = equals(value, fromB);
        var roundTrip = serializeA(fromB).AsSpan().SequenceEqual(a) && serializeB(fromA).AsSpan().SequenceEqual(a);
        Console.WriteLine($"{name}: {a.Length:N0} bytes; serialized bytes identical={bytesEqual}; " +
            $"A read equals source={aEquals}; B read equals source={bEquals}; cross round-trip identical={roundTrip}");
        if (name == "small")
        {
            Console.WriteLine("  " + Encoding.UTF8.GetString(a));
        }

        return bytesEqual && aEquals && bEquals && roundTrip ? 0 : 1;
    }

    private static void Probe(string name, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var a = Outcome(() => BenchContractCodec.ParseSaveAsync(bytes));
        var b = Outcome(() => JsonSerializer.Deserialize(bytes, BenchJsonContext.Default.Note));
        Console.WriteLine($"| {name} | {a} | {b} |");
    }

    private static string Outcome(Func<Note?> read)
    {
        try
        {
            var note = read();
            return note is null
                ? "accepted: null"
                : $"accepted: text={Show(note.Text)}, tags=[{string.Join(",", note.Tags.Select(Show))}] " +
                  $"({note.Tags.GetType().Name}), parent={(note.Parent is null ? "null" : "set")}, " +
                  $"score={note.Score}, version={note.Version?.ToString() ?? "null"}";
        }
        catch (Exception exception)
        {
            var message = exception.Message.Split(". ")[0].TrimEnd('.');
            return $"rejected: {exception.GetType().Name} ({message})";
        }
    }

    private static string Show(string? value) => value is null ? "null" : $"\"{value}\"";

    private static bool NoteEquals(Note? x, Note? y) =>
        ReferenceEquals(x, y)
        || (x is not null && y is not null
            && x.Text == y.Text
            && x.Tags.SequenceEqual(y.Tags)
            && NoteEquals(x.Parent, y.Parent)
            && BitConverter.DoubleToInt64Bits(x.Score) == BitConverter.DoubleToInt64Bits(y.Score)
            && x.Pinned == y.Pinned
            && x.Version == y.Version);
}
