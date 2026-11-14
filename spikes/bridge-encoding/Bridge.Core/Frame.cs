using System.Buffers;

namespace Bridge;

/// <summary>Every message kind of the bridge protocol, independent of how it is encoded.</summary>
public enum Kind : byte
{
    Invalid = 0,

    // Page to host.
    Hello,
    Call,
    Stream,
    Credits,
    Cancel,
    ClosingDone,

    // Host to page.
    Open,
    Result,
    Items,
    End,
    Cancelled,
    NotExecuted,
    OutcomeUnknown,
    Closing,

    /// <summary>A well-formed message of a kind this decoder does not know (a newer peer).</summary>
    Unrecognized,
}

/// <summary>One decoded or to-be-encoded message. Fields a kind does not use stay default.</summary>
public struct Frame
{
    public Kind Kind;

    /// <summary>The call id, or -1 when the message has none (or it could not be read).</summary>
    public long Id;
    public string? Doc;
    public int Protocol;
    public string? Contract;
    public string? Command;
    public string? Fingerprint;
    public string? TraceParent;
    public string? TraceState;
    public int Batch;
    public int Credits;
    public long Dropped;
    public string? Code;
    public string? Detail;

    /// <summary>Raw JSON: the request argument, the result envelope, an item array or an error body. Empty when absent.</summary>
    public ReadOnlyMemory<byte> Payload;

    /// <summary>For <see cref="Kind.Unrecognized"/>: the kind name or letter as received.</summary>
    public string? UnrecognizedKind;

    public static Frame Of(Kind kind, long id = -1) => new() { Kind = kind, Id = id };

    public readonly bool IsTerminal => Kind is Kind.Result or Kind.End or Kind.Cancelled or Kind.NotExecuted or Kind.OutcomeUnknown;
}

public enum DecodeStatus
{
    Ok,

    /// <summary>The frame is not a valid message. <see cref="Frame.Id"/> is set when the id could still be read.</summary>
    Malformed,
}

public readonly record struct DecodeResult(DecodeStatus Status, string? Error)
{
    public static readonly DecodeResult Success = new(DecodeStatus.Ok, null);

    public static DecodeResult Fail(string error) => new(DecodeStatus.Malformed, error);

    public bool Ok => Status == DecodeStatus.Ok;
}

/// <summary>One candidate wire encoding.</summary>
public interface ICodec
{
    string Name { get; }

    void Write(in Frame frame, IBufferWriter<byte> output);

    /// <summary>Decodes the header and locates the payload. The payload itself is not validated here.</summary>
    DecodeResult TryRead(ReadOnlyMemory<byte> data, out Frame frame);
}

public static class Codecs
{
    public static readonly ICodec A = new LetterCodec();
    public static readonly ICodec B = new EnvelopeCodec();
    public static readonly ICodec C = new HeaderCodec();

    public static ICodec Get(string name) => name switch
    {
        "A" => A,
        "B" or "Bj" => B,
        "C" => C,
        _ => throw new ArgumentException("Unknown encoding " + name, nameof(name)),
    };

    public static readonly ICodec[] All = [A, B, C];

    /// <summary>Largest id the page can count to without losing precision (a JavaScript number).</summary>
    public const long MaxId = (1L << 53) - 1;
}
