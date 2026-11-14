using System.Buffers;
using System.Diagnostics;
using System.Text.Json;
using Bench;
using Spar.Generated;

// Cross-check harness with the shipping build settings (repo props, warnings as errors, default ILC options).
// Each round takes one ~100 ms sample of every case, so background load drifts across A and B alike.
var rounds = args.Length > 0 ? int.Parse(args[0], System.Globalization.CultureInfo.InvariantCulture) : 15;
// PublishAot sets IsDynamicCodeSupported=false in runtimeconfig for CoreCLR runs too, so the caller names the runtime.
var runtime = args.Length > 1 ? args[1] : "unnamed runtime";

var buffer = new ArrayBufferWriter<byte>(1024 * 1024);
var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { SkipValidation = true });

var small = Workload.Small();
var medium = Workload.Array(50);
var large = Workload.Array(2000);
var smallJson = BenchContractCodec.SerializeSaveAsync(small);
var mediumJson = BenchContractCodec.SerializeSaveManyAsync(medium);
var largeJson = BenchContractCodec.SerializeSaveManyAsync(large);

// Writer delegates are created once so the reused-writer cases allocate nothing of their own.
Action<Utf8JsonWriter> writeSmallA = w => BenchContractCodec.WriteSaveAsync(w, small);
Action<Utf8JsonWriter> writeSmallB = w => JsonSerializer.Serialize(w, small, BenchJsonContext.Default.Note);
Action<Utf8JsonWriter> writeMediumA = w => BenchContractCodec.WriteSaveManyAsync(w, medium);
Action<Utf8JsonWriter> writeMediumB = w => JsonSerializer.Serialize(w, medium, BenchJsonContext.Default.NoteArray);
Action<Utf8JsonWriter> writeLargeA = w => BenchContractCodec.WriteSaveManyAsync(w, large);
Action<Utf8JsonWriter> writeLargeB = w => JsonSerializer.Serialize(w, large, BenchJsonContext.Default.NoteArray);
Action<Utf8JsonWriter> writeSmallA2 = w => BenchContractCodec.WriteTunedSaveAsync(w, small);
Action<Utf8JsonWriter> writeMediumA2 = w => BenchContractCodec.WriteTunedSaveManyAsync(w, medium);
Action<Utf8JsonWriter> writeLargeA2 = w => BenchContractCodec.WriteTunedSaveManyAsync(w, large);

var cases = new List<(string Name, Func<object> Run)>
{
    ("small   deserialize      A", () => BenchContractCodec.ParseSaveAsync(smallJson)),
    ("small   deserialize      B", () => JsonSerializer.Deserialize(smallJson, BenchJsonContext.Default.Note)!),
    ("small   serialize        A", () => BenchContractCodec.SerializeSaveAsync(small)),
    ("small   serialize        B", () => JsonSerializer.SerializeToUtf8Bytes(small, BenchJsonContext.Default.Note)),
    ("small   serialize reused A", () => Reused(writeSmallA)),
    ("small   serialize reused B", () => Reused(writeSmallB)),
    ("small   serialize tuned A2", () => Reused(writeSmallA2)),
    ("50      deserialize      A", () => BenchContractCodec.ParseSaveManyAsync(mediumJson)),
    ("50      deserialize      B", () => JsonSerializer.Deserialize(mediumJson, BenchJsonContext.Default.NoteArray)!),
    ("50      serialize        A", () => BenchContractCodec.SerializeSaveManyAsync(medium)),
    ("50      serialize        B", () => JsonSerializer.SerializeToUtf8Bytes(medium, BenchJsonContext.Default.NoteArray)),
    ("50      serialize reused A", () => Reused(writeMediumA)),
    ("50      serialize reused B", () => Reused(writeMediumB)),
    ("50      serialize tuned A2", () => Reused(writeMediumA2)),
    ("2000    deserialize      A", () => BenchContractCodec.ParseSaveManyAsync(largeJson)),
    ("2000    deserialize      B", () => JsonSerializer.Deserialize(largeJson, BenchJsonContext.Default.NoteArray)!),
    ("2000    serialize        A", () => BenchContractCodec.SerializeSaveManyAsync(large)),
    ("2000    serialize        B", () => JsonSerializer.SerializeToUtf8Bytes(large, BenchJsonContext.Default.NoteArray)),
    ("2000    serialize reused A", () => Reused(writeLargeA)),
    ("2000    serialize reused B", () => Reused(writeLargeB)),
    ("2000    serialize tuned A2", () => Reused(writeLargeA2)),
};

// Warm up (tiered JIT reaches tier 1 on CoreCLR) and calibrate operations per ~100 ms sample.
var operations = new long[cases.Count];
for (var i = 0; i < cases.Count; i++)
{
    var run = cases[i].Run;
    var watch = Stopwatch.StartNew();
    long count = 0;
    while (watch.ElapsedMilliseconds < 1000)
    {
        run();
        count++;
    }

    operations[i] = Math.Max(1, count / 10);
}

var nanoseconds = new double[cases.Count][];
var allocated = new double[cases.Count];
for (var i = 0; i < cases.Count; i++)
{
    nanoseconds[i] = new double[rounds];
}

for (var round = 0; round < rounds; round++)
{
    for (var i = 0; i < cases.Count; i++)
    {
        var (ns, bytes) = Sample(cases[i].Run, operations[i]);
        nanoseconds[i][round] = ns;
        allocated[i] = bytes;
    }
}

Console.WriteLine($"{runtime}, {rounds} rounds, {Environment.ProcessorCount} logical CPUs");
Console.WriteLine("| Case | Median ns/op | Min | Max | Spread (max-min)/median | Allocated B/op | A/B (median) |");
Console.WriteLine("|---|---:|---:|---:|---:|---:|---:|");
for (var i = 0; i < cases.Count; i++)
{
    var sorted = nanoseconds[i].Order().ToArray();
    var median = sorted[sorted.Length / 2];
    var ratio = string.Empty;
    if (cases[i].Name.EndsWith('A') || cases[i].Name.EndsWith("A2", StringComparison.Ordinal))
    {
        var other = nanoseconds[cases[i].Name.EndsWith('A') ? i + 1 : i - 1].Order().ToArray();
        ratio = (median / other[other.Length / 2]).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
    }

    Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"| {cases[i].Name} | {median:N1} | {sorted[0]:N1} | {sorted[^1]:N1} | {(sorted[^1] - sorted[0]) / median:P1} | {allocated[i]:N0} | {ratio} |"));
}

writer.Dispose();
return 0;

object Reused(Action<Utf8JsonWriter> write)
{
    buffer.ResetWrittenCount();
    writer.Reset(buffer);
    write(writer);
    writer.Flush();
    return buffer;
}

static (double Nanoseconds, double Bytes) Sample(Func<object> run, long operations)
{
    var before = GC.GetAllocatedBytesForCurrentThread();
    var start = Stopwatch.GetTimestamp();
    object? last = null;
    for (long n = 0; n < operations; n++)
    {
        last = run();
    }

    var elapsed = Stopwatch.GetElapsedTime(start);
    var after = GC.GetAllocatedBytesForCurrentThread();
    GC.KeepAlive(last);
    return (elapsed.TotalNanoseconds / operations, (double)(after - before) / operations);
}
