using System.Diagnostics;
using System.Text.Json;
using Spar.Runtime;

namespace Spar.Devtools;

/// <summary>
/// The devtools' <c>calls</c>: the last <see cref="Capacity"/> calls the dispatcher ran, from every caller, each from its
/// start, so a call still running is listed too. Each has a sequence number, which a page action uses to name the calls
/// it caused.
/// </summary>
internal sealed class CallRecorder(TimeProvider time)
{
    public const int Capacity = 200;

    private readonly Lock _gate = new();
    private readonly Queue<CallRecord> _calls = new(Capacity);
    private long _sequence;

    /// <summary>The sequence number the next call gets.</summary>
    public long Next
    {
        get
        {
            lock (_gate)
            {
                return _sequence + 1;
            }
        }
    }

    public CallRecord Begin(long callId, string contract, string command, SparCaller caller)
    {
        lock (_gate)
        {
            var record = new CallRecord(++_sequence, callId, contract, command, caller, time.GetUtcNow(), Stopwatch.GetTimestamp(), _gate);
            if (_calls.Count == Capacity)
            {
                _calls.Dequeue();
            }

            _calls.Enqueue(record);
            return record;
        }
    }

    /// <summary>The recorded calls from sequence <paramref name="from"/> on, oldest first, at most <paramref name="limit"/>.</summary>
    public IReadOnlyList<CallRecord> Since(long from, int limit = Capacity)
    {
        lock (_gate)
        {
            return [.. _calls.Where(call => call.Sequence >= from).TakeLast(limit)];
        }
    }

    /// <summary>When the last call ended or began, as a timestamp; 0 when none ran yet. For an action's settling.</summary>
    public long LastActivity
    {
        get
        {
            lock (_gate)
            {
                return _calls.Count == 0 ? 0 : _calls.Max(call => call.LastActivity);
            }
        }
    }

    /// <summary>Whether a call from sequence <paramref name="from"/> on is still running.</summary>
    public bool Running(long from)
    {
        lock (_gate)
        {
            return _calls.Any(call => call.Sequence >= from && call.Outcome is null);
        }
    }
}

/// <summary>One recorded call; its outcome is filled when it ends.</summary>
internal sealed class CallRecord(
    long sequence, long callId, string contract, string command, SparCaller caller, DateTimeOffset started, long startedAt, Lock gate)
{
    private long _endedAt;

    public long Sequence { get; } = sequence;

    public long CallId { get; } = callId;

    public string Contract { get; } = contract;

    public string Command { get; } = command;

    public SparCaller Caller { get; } = caller;

    public DateTimeOffset Started { get; } = started;

    /// <summary><c>success</c>, <c>error</c> (a declared error, with its code) or the failure's kind; null while running.</summary>
    public string? Outcome { get; private set; }

    /// <summary>The declared error's code or the failure's code.</summary>
    public string? Code { get; private set; }

    public double Milliseconds => Stopwatch.GetElapsedTime(startedAt, _endedAt == 0 ? Stopwatch.GetTimestamp() : _endedAt).TotalMilliseconds;

    public long LastActivity => _endedAt == 0 ? startedAt : _endedAt;

    public void End(SparOutcome outcome)
    {
        lock (gate)
        {
            _endedAt = Stopwatch.GetTimestamp();
            (Outcome, Code) = outcome.Kind switch
            {
                SparOutcomeKind.Value => ("success", (string?)null),
                SparOutcomeKind.Error => ("error", ErrorCode(outcome.Payload.Span)),
                _ => (outcome.Failure?.Kind ?? "failed", outcome.Failure?.Code),
            };
        }
    }

    public void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteNumber("sequence", Sequence);
        writer.WriteNumber("callId", CallId);
        writer.WriteString("command", $"{Contract}.{Command}");
        writer.WriteString("caller", Caller switch { SparCaller.Test => "test", SparCaller.Devtools => "devtools", _ => "page" });
        writer.WriteString("started", Started);
        writer.WriteNumber("milliseconds", Math.Round(Milliseconds, 1));
        writer.WriteString("outcome", Outcome ?? "running");
        if (Code is not null)
        {
            writer.WriteString("code", Code);
        }

        writer.WriteEndObject();
    }

    private static string? ErrorCode(ReadOnlySpan<byte> payload)
    {
        try
        {
            var reader = new Utf8JsonReader(payload);
            using var document = JsonDocument.ParseValue(ref reader);
            return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
