using System.ComponentModel;

namespace Spar.Bridge;

/// <summary>The message types of protocol version 1 (plan section 2c, "The messages").</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public enum BridgeMessageType
{
    // Page to host.
    Hello,
    Call,
    Stream,
    Credits,
    Cancel,
    Ready,
    ClosingDone,

    /// <summary>The page's answer to <see cref="Ping"/>, sent from its message handler with the same <c>seq</c>.</summary>
    Pong,

    /// <summary>
    /// Content Security Policy violations, grouped and batched by the host-injected script (plan section 3b); the payload
    /// is the batch. Hosts without the injected script never receive it.
    /// </summary>
    Csp,

    /// <summary>
    /// The page's errors (console errors, uncaught exceptions, unhandled rejections), grouped and batched by the
    /// host-injected script (plan M1b, build step 7); the payload is the batch. Hosts without the injected script never
    /// receive it.
    /// </summary>
    Errors,

    /// <summary>
    /// The page's answer to <see cref="CloseRequest"/>, with its <c>seq</c> (decision "Two-stage close"; added in M1b
    /// step 8): the payload is <c>{"answer":"close"}</c>, <c>{"answer":"keep"}</c>,
    /// <c>{"answer":"ask","question":"…"}</c>, or <c>{"answer":"failed"}</c> when a guard threw. Untrusted page text.
    /// </summary>
    CloseAnswer,

    // Host to page.
    Open,
    Result,
    Items,
    End,
    Failure,
    Event,
    Closing,

    /// <summary>
    /// The heartbeat (plan section 3, "Hung pages"), sent only by a host whose injected script answers it in the page.
    /// </summary>
    Ping,

    /// <summary>
    /// A request to close the page's window, numbered by <c>seq</c>, with the payload
    /// <c>{"reason":"user"|"app"|"sessionEnd","automated":true|false}</c>. Sent only to a page that listed it in
    /// <c>hello</c>; the page's guards answer <see cref="CloseAnswer"/>.
    /// </summary>
    CloseRequest,

    /// <summary>
    /// The close request <c>seq</c> ended without the page's answer (a session-end deadline, "Close anyway", the
    /// teardown): the page aborts the request's <c>signal</c>. Sent only to a page that listed it in <c>hello</c>.
    /// </summary>
    CloseEnded,

    /// <summary>A well-formed message of a type this side does not know (a newer peer).</summary>
    Unknown,
}

/// <summary>
/// One bridge message: a JSON header, a TAB, then the payload's JSON. Fields a type does not use stay null. The
/// payload is located, never parsed, by the codec.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record BridgeMessage(BridgeMessageType Type)
{
    /// <summary>The type name as received, for <see cref="BridgeMessageType.Unknown"/>.</summary>
    public string? TypeName { get; init; }

    public long? Id { get; init; }

    public int? Protocol { get; init; }

    /// <summary>In <c>hello</c>: the host-to-page types the page understands.</summary>
    public IReadOnlyList<string>? Types { get; init; }

    /// <summary>In <c>hello</c> and <c>open</c>: the contract fingerprint.</summary>
    public string? Contracts { get; init; }

    public string? Doc { get; init; }

    public string? Contract { get; init; }

    public string? Command { get; init; }

    public string? TraceParent { get; init; }

    public string? TraceState { get; init; }

    public int? Credits { get; init; }

    public bool? Ok { get; init; }

    public long? Dropped { get; init; }

    public string? Kind { get; init; }

    public string? Code { get; init; }

    public string? Execution { get; init; }

    public long? ItemsSent { get; init; }

    /// <summary>In <c>event</c>: the event's name.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// In <c>ping</c> and <c>pong</c>, the heartbeat's number; in <c>close-request</c>, <c>close-answer</c> and
    /// <c>close-ended</c>, the close request's. A safe integer from 1 either way.
    /// </summary>
    public long? Seq { get; init; }

    /// <summary>The JSON after the TAB, as UTF-8; empty when there is none.</summary>
    public ReadOnlyMemory<byte> Payload { get; init; }

    public static string TypeNameOf(BridgeMessageType type) => type switch
    {
        BridgeMessageType.Hello => "hello",
        BridgeMessageType.Call => "call",
        BridgeMessageType.Stream => "stream",
        BridgeMessageType.Credits => "credits",
        BridgeMessageType.Cancel => "cancel",
        BridgeMessageType.Ready => "ready",
        BridgeMessageType.ClosingDone => "closing-done",
        BridgeMessageType.Pong => "pong",
        BridgeMessageType.Csp => "csp",
        BridgeMessageType.Errors => "errors",
        BridgeMessageType.Open => "open",
        BridgeMessageType.Result => "result",
        BridgeMessageType.Items => "items",
        BridgeMessageType.End => "end",
        BridgeMessageType.Failure => "failure",
        BridgeMessageType.Event => "event",
        BridgeMessageType.Closing => "closing",
        BridgeMessageType.Ping => "ping",
        BridgeMessageType.CloseRequest => "close-request",
        BridgeMessageType.CloseAnswer => "close-answer",
        BridgeMessageType.CloseEnded => "close-ended",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "An unknown message has no type name of its own."),
    };

    public static BridgeMessageType TypeOf(string name) => name switch
    {
        "hello" => BridgeMessageType.Hello,
        "call" => BridgeMessageType.Call,
        "stream" => BridgeMessageType.Stream,
        "credits" => BridgeMessageType.Credits,
        "cancel" => BridgeMessageType.Cancel,
        "ready" => BridgeMessageType.Ready,
        "closing-done" => BridgeMessageType.ClosingDone,
        "pong" => BridgeMessageType.Pong,
        "csp" => BridgeMessageType.Csp,
        "errors" => BridgeMessageType.Errors,
        "open" => BridgeMessageType.Open,
        "result" => BridgeMessageType.Result,
        "items" => BridgeMessageType.Items,
        "end" => BridgeMessageType.End,
        "failure" => BridgeMessageType.Failure,
        "event" => BridgeMessageType.Event,
        "closing" => BridgeMessageType.Closing,
        "ping" => BridgeMessageType.Ping,
        "close-request" => BridgeMessageType.CloseRequest,
        "close-answer" => BridgeMessageType.CloseAnswer,
        "close-ended" => BridgeMessageType.CloseEnded,
        _ => BridgeMessageType.Unknown,
    };
}

/// <summary>Why a message could not be read, and its id when that much could be read.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record BridgeDecodeError(string Reason, long? Id);
