using System.Buffers;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Spar.Generated;

namespace Bench;

/// <summary>
/// One workload per instance. Each category compares (A) generated code against (B) System.Text.Json,
/// with (B) as the baseline so the Ratio column reads A/B.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public abstract class SerializerBenchmarks<T>
    where T : class
{
    private readonly ArrayBufferWriter<byte> _buffer = new(1024 * 1024);
    private Utf8JsonWriter _writer = null!;

    protected T Value { get; private set; } = null!;

    protected byte[] Json { get; private set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        Value = Create();
        Json = SerializeA(Value);
        if (!Json.AsSpan().SequenceEqual(SerializeB(Value)))
        {
            throw new InvalidOperationException("A and B produce different bytes for the workload.");
        }

        using (var check = new Utf8JsonWriter(_buffer))
        {
            WriteTunedA(check, Value);
        }

        if (!Json.AsSpan().SequenceEqual(_buffer.WrittenSpan))
        {
            throw new InvalidOperationException("The tuned writer produces different bytes.");
        }

        // A tuned bridge reuses one writer and buffer; generated code always writes valid JSON, so validation is off.
        _writer = new Utf8JsonWriter(_buffer, new JsonWriterOptions { SkipValidation = true });
    }

    [GlobalCleanup]
    public void Cleanup() => _writer.Dispose();

    [Benchmark, BenchmarkCategory("1 Deserialize")]
    public T A_Deserialize() => DeserializeA(Json);

    [Benchmark(Baseline = true), BenchmarkCategory("1 Deserialize")]
    public T B_Deserialize() => DeserializeB(Json);

    [Benchmark, BenchmarkCategory("2 Serialize, as shipped (byte[])")]
    public byte[] A_Serialize() => SerializeA(Value);

    [Benchmark(Baseline = true), BenchmarkCategory("2 Serialize, as shipped (byte[])")]
    public byte[] B_Serialize() => SerializeB(Value);

    [Benchmark, BenchmarkCategory("3 Serialize, reused writer + exact byte[]")]
    public byte[] A_SerializeReusedCopy()
    {
        Reset();
        WriteA(_writer, Value);
        _writer.Flush();
        return _buffer.WrittenSpan.ToArray();
    }

    [Benchmark, BenchmarkCategory("3 Serialize, reused writer + exact byte[]")]
    public byte[] A2_SerializeReusedCopy()
    {
        Reset();
        WriteTunedA(_writer, Value);
        _writer.Flush();
        return _buffer.WrittenSpan.ToArray();
    }

    [Benchmark(Baseline = true), BenchmarkCategory("3 Serialize, reused writer + exact byte[]")]
    public byte[] B_SerializeReusedCopy()
    {
        Reset();
        WriteB(_writer, Value);
        _writer.Flush();
        return _buffer.WrittenSpan.ToArray();
    }

    [Benchmark, BenchmarkCategory("4 Serialize, reused writer, no copy")]
    public int A_SerializeReused()
    {
        Reset();
        WriteA(_writer, Value);
        _writer.Flush();
        return _buffer.WrittenCount;
    }

    [Benchmark, BenchmarkCategory("4 Serialize, reused writer, no copy")]
    public int A2_SerializeReused()
    {
        Reset();
        WriteTunedA(_writer, Value);
        _writer.Flush();
        return _buffer.WrittenCount;
    }

    [Benchmark(Baseline = true), BenchmarkCategory("4 Serialize, reused writer, no copy")]
    public int B_SerializeReused()
    {
        Reset();
        WriteB(_writer, Value);
        _writer.Flush();
        return _buffer.WrittenCount;
    }

    protected abstract T Create();

    protected abstract T DeserializeA(ReadOnlySpan<byte> json);

    protected abstract T DeserializeB(ReadOnlySpan<byte> json);

    protected abstract byte[] SerializeA(T value);

    protected abstract byte[] SerializeB(T value);

    protected abstract void WriteA(Utf8JsonWriter writer, T value);

    /// <summary>Experiment: the tuned generated writer (pre-encoded names, indexed loops).</summary>
    protected abstract void WriteTunedA(Utf8JsonWriter writer, T value);

    protected abstract void WriteB(Utf8JsonWriter writer, T value);

    private void Reset()
    {
        _buffer.ResetWrittenCount();
        _writer.Reset(_buffer);
    }
}

/// <summary>The small request: one <see cref="Note"/> with every property populated, including a nested parent.</summary>
public class SmallRequest : SerializerBenchmarks<Note>
{
    protected override Note Create() => Workload.Small();

    protected override Note DeserializeA(ReadOnlySpan<byte> json) => BenchContractCodec.ParseSaveAsync(json);

    protected override Note DeserializeB(ReadOnlySpan<byte> json) =>
        JsonSerializer.Deserialize(json, BenchJsonContext.Default.Note)!;

    protected override byte[] SerializeA(Note value) => BenchContractCodec.SerializeSaveAsync(value);

    protected override byte[] SerializeB(Note value) =>
        JsonSerializer.SerializeToUtf8Bytes(value, BenchJsonContext.Default.Note);

    protected override void WriteA(Utf8JsonWriter writer, Note value) => BenchContractCodec.WriteSaveAsync(writer, value);

    protected override void WriteB(Utf8JsonWriter writer, Note value) =>
        JsonSerializer.Serialize(writer, value, BenchJsonContext.Default.Note);

    protected override void WriteTunedA(Utf8JsonWriter writer, Note value) =>
        BenchContractCodec.WriteTunedSaveAsync(writer, value);
}

/// <summary>The medium (50) and large (2,000) responses: an array of <see cref="Note"/>.</summary>
public class ArrayResponse : SerializerBenchmarks<Note[]>
{
    [Params(50, 2000)]
    public int Count { get; set; }

    protected override Note[] Create() => Workload.Array(Count);

    protected override Note[] DeserializeA(ReadOnlySpan<byte> json) => BenchContractCodec.ParseSaveManyAsync(json);

    protected override Note[] DeserializeB(ReadOnlySpan<byte> json) =>
        JsonSerializer.Deserialize(json, BenchJsonContext.Default.NoteArray)!;

    protected override byte[] SerializeA(Note[] value) => BenchContractCodec.SerializeSaveManyAsync(value);

    protected override byte[] SerializeB(Note[] value) =>
        JsonSerializer.SerializeToUtf8Bytes(value, BenchJsonContext.Default.NoteArray);

    protected override void WriteA(Utf8JsonWriter writer, Note[] value) =>
        BenchContractCodec.WriteSaveManyAsync(writer, value);

    protected override void WriteB(Utf8JsonWriter writer, Note[] value) =>
        JsonSerializer.Serialize(writer, value, BenchJsonContext.Default.NoteArray);

    protected override void WriteTunedA(Utf8JsonWriter writer, Note[] value) =>
        BenchContractCodec.WriteTunedSaveManyAsync(writer, value);
}
