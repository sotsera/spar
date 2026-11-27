namespace Spar;

/// <summary>
/// The bridge's limits for each document (plan section 2c, "Limits"). The page is untrusted (plan section 3c): these
/// bound what it can make the app hold and run. Raising one lets a page, or a script injected into it, take more of
/// the app's memory and threads; the defaults fit ordinary apps, and a large transfer belongs in a stream (M3).
/// </summary>
public sealed class BridgeOptions
{
    /// <summary>
    /// The largest message the page may send, in UTF-8 bytes; a larger call is refused with <c>rejected</c> /
    /// <c>too-large</c> before its payload is read. Taking a message in costs about three times its size in memory.
    /// </summary>
    public int MaxMessageBytes { get; set; } = 4 * 1024 * 1024;

    /// <summary>
    /// The largest result a command may return, in UTF-8 bytes; a larger one fails only its own call, with
    /// <c>response</c> / <c>too-large</c> (the command did run).
    /// </summary>
    public int MaxResultBytes { get; set; } = 16 * 1024 * 1024;

    /// <summary>How many commands of one document may run at once; the next ones wait in the queue.</summary>
    public int CallsInFlight { get; set; } = 16;

    /// <summary>
    /// How many calls may wait for a slot; one more is refused with <c>rejected</c> / <c>busy</c>. A waiting call holds
    /// about 600 bytes besides its payload, which <see cref="QueuedBytes"/> bounds, so a page may queue a large burst.
    /// </summary>
    public int QueuedCalls { get; set; } = 1024;

    /// <summary>How many bytes of payload the waiting calls may hold together; past it a call is refused as busy.</summary>
    public int QueuedBytes { get; set; } = 16 * 1024 * 1024;

    /// <summary>
    /// How many streams and subscriptions of one document may be open at once; one more is refused with <c>rejected</c> /
    /// <c>busy</c>. They do not take the slots of <see cref="CallsInFlight"/>, since a subscription stays open for as long
    /// as its view, and each holds at most its credits and its buffer of serialized items.
    /// </summary>
    public int StreamsOpen { get; set; } = 64;

    /// <summary>
    /// How many bytes the <c>byte[]</c> handles (<see cref="SparBlob.FromBytes"/>) a document holds may total; a result
    /// that would pass it fails its own call with <c>response</c> / <c>too-large</c>, and no handle is released to make
    /// room. File and stream handles hold no memory and do not count.
    /// </summary>
    public int MaxBlobBytes { get; set; } = 512 * 1024 * 1024;

    /// <summary>How many handles one document may hold at once; past it a result fails as for <see cref="MaxBlobBytes"/>.</summary>
    public int MaxBlobs { get; set; } = 10_000;

    /// <summary>The values as the bridge uses them; each must be at least 1.</summary>
    internal BridgeLimits Freeze() => new(
        Positive(MaxMessageBytes, nameof(MaxMessageBytes)),
        Positive(MaxResultBytes, nameof(MaxResultBytes)),
        Positive(CallsInFlight, nameof(CallsInFlight)),
        Positive(QueuedCalls, nameof(QueuedCalls)),
        Positive(QueuedBytes, nameof(QueuedBytes)),
        Positive(StreamsOpen, nameof(StreamsOpen)),
        Positive(MaxBlobBytes, nameof(MaxBlobBytes)),
        Positive(MaxBlobs, nameof(MaxBlobs)));

    private static int Positive(int value, string name) =>
        value >= 1 ? value : throw new ArgumentOutOfRangeException(name, value, $"BridgeOptions.{name} must be at least 1.");
}

/// <summary>The limits one app's bridge applies, fixed when the app is built.</summary>
internal sealed record BridgeLimits(int MaxMessageBytes, int MaxResultBytes, int CallsInFlight, int QueuedCalls, int QueuedBytes, int StreamsOpen,
    int MaxBlobBytes, int MaxBlobs)
{
    public static BridgeLimits Default { get; } = new BridgeOptions().Freeze();
}
