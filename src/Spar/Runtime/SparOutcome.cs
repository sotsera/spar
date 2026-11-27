using System.ComponentModel;

namespace Spar.Runtime;

public enum SparOutcomeKind
{
    /// <summary>The command returned a value (the payload is its JSON, empty for a command without one).</summary>
    Value,

    /// <summary>The command returned a declared error (the payload is <c>{"code":…}</c> with its fields).</summary>
    Error,

    /// <summary>An infrastructure failure (<see cref="SparOutcome.Failure"/>); the payload is empty.</summary>
    Failure,
}

/// <summary>
/// The outcome of one dispatched command, in the shape the bridge sends (plan section 2c, "The messages"): a
/// <c>result</c> with <c>ok</c> true or false, or a <c>failure</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct SparOutcome
{
    private SparOutcome(SparOutcomeKind kind, ReadOnlyMemory<byte> payload, SparFailure? failure)
    {
        Kind = kind;
        Payload = payload;
        Failure = failure;
    }

    public SparOutcomeKind Kind { get; }

    public ReadOnlyMemory<byte> Payload { get; }

    public SparFailure? Failure { get; }

    public static SparOutcome FromValue(ReadOnlyMemory<byte> json) => new(SparOutcomeKind.Value, json, null);

    public static SparOutcome FromError(ReadOnlyMemory<byte> json) => new(SparOutcomeKind.Error, json, null);

    public static SparOutcome FromFailure(SparFailure failure) => new(SparOutcomeKind.Failure, ReadOnlyMemory<byte>.Empty, failure);

    public override string ToString() => Kind == SparOutcomeKind.Failure
        ? $"failure {Failure}"
        : $"{(Kind == SparOutcomeKind.Value ? "ok" : "error")} {System.Text.Encoding.UTF8.GetString(Payload.Span)}";
}

/// <summary>
/// An infrastructure failure with the decided <c>CallError</c> fields (decision "<c>CallError</c> kinds"). The values
/// are the wire strings. <see cref="Detail"/> reaches the page only in Development builds; <see cref="Exception"/>,
/// when a command threw, never does: the host logs it under the call's correlation id.
/// </summary>
public sealed record SparFailure(string Kind, string Code, string Execution, string? Detail = null, Exception? Exception = null)
{
    public static SparFailure InvalidRequest(string detail) => new("rejected", "invalid-request", "notStarted", detail);

    public static SparFailure UnknownCommand(string contract, string command) =>
        new("rejected", "unknown-command", "notStarted", $"{contract}.{command}");

    public static SparFailure Cancelled() => new("cancelled", "cancelled", "stopped");

    public static SparFailure Failed(Exception exception) =>
        new("failed", "failed", "unknown", $"{exception.GetType().Name}: {exception.Message}", exception);

    /// <summary>A result that could not be written; one whose blobs pass the document's limits is <c>too-large</c>.</summary>
    public static SparFailure ResponseSerialization(Exception exception) => exception is BlobLimitException
        ? new("response", "too-large", "completed", exception.Message)
        : new("response", "serialization", "completed", $"{exception.GetType().Name}: {exception.Message}", exception);

    /// <summary>A refusal by the bridge limits: the command never ran.</summary>
    internal static SparFailure Busy() => new("rejected", "busy", "notStarted");

    /// <summary>A command outside the calling window's allow-list (decision "Per-window command allow-lists").</summary>
    internal static SparFailure ForbiddenCommand(string window, string contract, string command) =>
        new("rejected", "forbidden-command", "notStarted", $"window '{window}' may not call {contract}.{command}");

    internal static SparFailure TooLarge(int bytes, int limit) =>
        new("rejected", "too-large", "notStarted", $"the message is {bytes} bytes; the limit is {limit}");

    internal static SparFailure ResultTooLarge(int bytes, int limit) =>
        new("response", "too-large", "completed", $"the result is {bytes} bytes; the limit is {limit}");

    public override string ToString() => $"{Kind}/{Code} ({Execution}){(Detail is null ? "" : ": " + Detail)}";
}
