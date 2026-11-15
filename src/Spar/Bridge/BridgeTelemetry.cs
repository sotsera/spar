using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Spar.Runtime;

namespace Spar.Bridge;

/// <summary>When a call message arrived: the clock reading its duration starts from, and the time its span starts at.</summary>
internal readonly record struct Received(long Timestamp, DateTimeOffset Utc);

/// <summary>
/// The always-on instrumentation of one document's calls (plan section 6b; decision "Bridge instrumentation: every
/// call, from receipt, with a flood budget"), BCL only. Every call that names a command records one
/// <c>spar.call.duration</c> sample, from receipt to its outcome, queue time included. An admitted call runs in a
/// server span from receipt, with a <c>started</c> event when it gets its slot. A call refused at the door gets a span
/// and a log entry only within a per-document budget, so a flood costs counting, not exporting. Names the app's
/// catalog does not know are recorded as <c>unknown</c>: the page chooses them, and every distinct tag value is a new
/// series. With no listener, no span is created and no sample is taken.
/// </summary>
internal sealed class BridgeTelemetry
{
    /// <summary>The name of the activity source and of the meter.</summary>
    public const string Name = "Spar";

    /// <summary>Refusal spans and logs a document may produce at once, and per second after that.</summary>
    private const int Burst = 20;

    private const double PerSecond = 5;

    private const string Unknown = "unknown";

    private static readonly ActivitySource Source = new(Name);

    private static readonly Meter Meter = new(Name);

    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
        "spar.call.duration", "s", "How long a bridge call took, from its message's arrival to its outcome, queue time included.");

    private static readonly Action<ILogger, long, string, string, string, string, Exception?> CallRefused = LoggerMessage.Define<long, string, string, string, string>(
        LogLevel.Debug, new EventId(4, "CallRefused"), "Call {CallId} to {Contract}.{Command} was refused ({Code}): {Detail}");

    private static readonly Action<ILogger, long, string, string, string, string, Exception?> CallRefusedWarning = LoggerMessage.Define<long, string, string, string, string>(
        LogLevel.Warning, new EventId(3, "CallRefused"), "Call {CallId} to {Contract}.{Command} was refused ({Code}): {Detail}");

    private static readonly Action<ILogger, int, string, Exception?> RefusalsSuppressed = LoggerMessage.Define<int, string>(
        LogLevel.Information, new EventId(5, "RefusalsSuppressed"), "{Count} refused calls of document {Doc} were not traced or logged, over the budget for refusals");

    private readonly SparDispatcher _dispatcher;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly string _doc;
    private readonly Lock _gate = new();
    private double _tokens = Burst;
    private long _refilledAt;
    private int _suppressed;

    public BridgeTelemetry(SparDispatcher dispatcher, TimeProvider time, string doc)
    {
        _dispatcher = dispatcher;
        _logger = dispatcher.Logger;
        _time = time;
        _doc = doc;
        _refilledAt = time.GetTimestamp();
    }

    public Received Now() => new(_time.GetTimestamp(), _time.GetUtcNow());

    /// <summary>
    /// Starts an admitted call's span, dated from its arrival, with a <c>started</c> event now. Its parent is the
    /// page's <c>traceparent</c>, else none: never the activity of the thread that received the message, nor of the
    /// call whose end freed the slot.
    /// </summary>
    public Activity? Started(BridgeMessage call, Received received)
    {
        Activity.Current = null;
        var activity = StartSpan(call, received);
        if (activity is null)
        {
            return null;
        }

        var queued = _time.GetElapsedTime(received.Timestamp).TotalSeconds;
        activity.SetTag("spar.queue.seconds", queued);
        activity.AddEvent(new ActivityEvent("started", _time.GetUtcNow(), new ActivityTagsCollection { ["spar.queue.seconds"] = queued }));
        return activity;
    }

    /// <summary>Ends an admitted call: its outcome on the span, and its duration sample.</summary>
    public void Completed(Activity? activity, BridgeMessage call, SparOutcome outcome, Received received)
    {
        if (activity is not null)
        {
            Describe(activity, outcome);
        }

        Record(call, outcome, received);
    }

    /// <summary>
    /// A call that ended without running. Refused at the door (<paramref name="atTheDoor"/>: too large, busy, the
    /// wrong contracts, malformed), it gets its span and its log entry only within the document's budget; a call that
    /// waited in the queue and was cancelled or failed there is an admitted one and always gets both. Every one records
    /// its duration.
    /// </summary>
    public void Ended(BridgeMessage? call, long id, SparFailure failure, Received received, bool atTheDoor)
    {
        var outcome = SparOutcome.FromFailure(failure);
        Record(call, outcome, received);
        if (atTheDoor && !TakeToken())
        {
            return;
        }

        var (contract, command) = Names(call);
        var log = failure.Code == "too-large" ? CallRefusedWarning : CallRefused;
        log(_logger, id, contract, command, failure.Code, failure.Detail ?? "", null);

        var ambient = Activity.Current;
        Activity.Current = null;
        using (var activity = call is null ? null : StartSpan(call, received))
        {
            if (activity is not null)
            {
                Describe(activity, outcome);
            }
        }

        Activity.Current = ambient;
    }

    /// <summary>Logs how many refusals were left out, if any; the session calls it when the document closes.</summary>
    public void Flush()
    {
        int suppressed;
        lock (_gate)
        {
            suppressed = _suppressed;
            _suppressed = 0;
        }

        if (suppressed > 0)
        {
            RefusalsSuppressed(_logger, suppressed, _doc, null);
        }
    }

    /// <summary>
    /// A token bucket: <see cref="Burst"/> at once, then <see cref="PerSecond"/>. The first refusal let through after
    /// a run of suppressed ones first logs how many there were.
    /// </summary>
    private bool TakeToken()
    {
        int suppressed;
        lock (_gate)
        {
            var now = _time.GetTimestamp();
            _tokens = Math.Min(Burst, _tokens + (_time.GetElapsedTime(_refilledAt, now).TotalSeconds * PerSecond));
            _refilledAt = now;
            if (_tokens < 1)
            {
                _suppressed++;
                return false;
            }

            _tokens--;
            suppressed = _suppressed;
            _suppressed = 0;
        }

        if (suppressed > 0)
        {
            RefusalsSuppressed(_logger, suppressed, _doc, null);
        }

        return true;
    }

    private Activity? StartSpan(BridgeMessage call, Received received)
    {
        if (!Source.HasListeners())
        {
            return null;
        }

        var (contract, command) = Names(call);
        ActivityContext.TryParse(call.TraceParent, call.TraceState, isRemote: true, out var parent);
        var activity = Source.StartActivity($"{contract}/{command}", ActivityKind.Server, parent, startTime: received.Utc);
        if (activity is null)
        {
            return null;
        }

        activity.SetTag("rpc.system", "spar");
        activity.SetTag("rpc.service", contract);
        activity.SetTag("rpc.method", command);
        activity.SetTag("spar.call.id", call.Id);
        activity.SetTag("spar.request.bytes", call.Payload.Length);
        return activity;
    }

    private static void Describe(Activity activity, SparOutcome outcome)
    {
        activity.SetTag("spar.outcome", Outcome(outcome));
        if (outcome.Kind != SparOutcomeKind.Failure)
        {
            activity.SetTag("spar.response.bytes", outcome.Payload.Length);
            return;
        }

        activity.SetTag("error.type", outcome.Failure!.Code);
        activity.SetStatus(ActivityStatusCode.Error, outcome.Failure.Code);
        if (outcome.Failure.Exception is { } exception)
        {
            activity.AddException(exception);
        }
    }

    private void Record(BridgeMessage? call, SparOutcome outcome, Received received)
    {
        if (!Duration.Enabled)
        {
            return;
        }

        var (contract, command) = Names(call);
        var tags = new TagList
        {
            { "rpc.system", "spar" },
            { "rpc.service", contract },
            { "rpc.method", command },
            { "spar.outcome", Outcome(outcome) },
        };
        if (outcome.Kind == SparOutcomeKind.Failure)
        {
            tags.Add("error.type", outcome.Failure!.Code);
        }

        Duration.Record(_time.GetElapsedTime(received.Timestamp).TotalSeconds, tags);
    }

    /// <summary>The call's contract and command when the app's catalog has them, else <c>unknown</c>.</summary>
    private (string Contract, string Command) Names(BridgeMessage? call) =>
        call?.Contract is { } contract && call.Command is { } command && _dispatcher.Knows(contract, command)
            ? (contract, command)
            : (Unknown, Unknown);

    /// <summary><c>success</c>, <c>error</c> for a declared error, or the failure's kind (<c>failed</c>, <c>rejected</c>, …).</summary>
    private static string Outcome(SparOutcome outcome) => outcome.Kind switch
    {
        SparOutcomeKind.Value => "success",
        SparOutcomeKind.Error => "error",
        _ => outcome.Failure!.Kind,
    };
}
