using System.Diagnostics;
using System.Diagnostics.Metrics;
using Spar.Runtime;

namespace Spar.Bridge;

/// <summary>
/// The always-on instrumentation of bridge commands (plan section 6b), BCL only: one server <see cref="Activity"/>
/// per command, a child of the page's <c>traceparent</c>, and one <c>spar.call.duration</c> sample. With no listener
/// the activity is never created and the sample is skipped. A listener subscribes to the source and the meter named
/// <see cref="Name"/>, for example the optional OpenTelemetry layer.
/// </summary>
internal static class BridgeTelemetry
{
    /// <summary>The name of the activity source and of the meter.</summary>
    public const string Name = "Spar";

    private static readonly ActivitySource Source = new(Name);

    private static readonly Meter Meter = new(Name);

    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
        "spar.call.duration", "s", "How long a bridge command ran, from its start to its outcome; time in the queue is not counted.");

    /// <summary>
    /// Starts the command's activity when anything listens. Its parent is the page's <c>traceparent</c> when the page
    /// sent a valid one, else none: never the activity of the host thread that received the message, nor of the
    /// call that freed the slot.
    /// </summary>
    public static Activity? Start(BridgeMessage call)
    {
        Activity.Current = null;
        if (!Source.HasListeners())
        {
            return null;
        }

        ActivityContext.TryParse(call.TraceParent, call.TraceState, isRemote: true, out var parent);
        var activity = Source.StartActivity($"{call.Contract}/{call.Command}", ActivityKind.Server, parent);
        if (activity is null)
        {
            return null;
        }

        activity.SetTag("rpc.system", "spar");
        activity.SetTag("rpc.service", call.Contract);
        activity.SetTag("rpc.method", call.Command);
        activity.SetTag("spar.call.id", call.Id);
        activity.SetTag("spar.request.bytes", call.Payload.Length);
        return activity;
    }

    /// <summary>Records the outcome on the activity, and the duration, tagged with what the command was and how it ended.</summary>
    public static void End(Activity? activity, BridgeMessage call, SparOutcome outcome, long startedAt)
    {
        var result = Outcome(outcome);
        if (activity is not null)
        {
            activity.SetTag("spar.outcome", result);
            if (outcome.Kind == SparOutcomeKind.Failure)
            {
                activity.SetTag("error.type", outcome.Failure!.Code);
                activity.SetStatus(ActivityStatusCode.Error, outcome.Failure.Code);
                if (outcome.Failure.Exception is { } exception)
                {
                    activity.AddException(exception);
                }
            }
            else
            {
                activity.SetTag("spar.response.bytes", outcome.Payload.Length);
            }
        }

        if (Duration.Enabled)
        {
            Duration.Record(
                Stopwatch.GetElapsedTime(startedAt).TotalSeconds,
                new KeyValuePair<string, object?>("rpc.system", "spar"),
                new KeyValuePair<string, object?>("rpc.service", call.Contract),
                new KeyValuePair<string, object?>("rpc.method", call.Command),
                new KeyValuePair<string, object?>("spar.outcome", result));
        }
    }

    /// <summary><c>success</c>, <c>error</c> for a declared error, or the failure's kind (<c>failed</c>, <c>cancelled</c>, …).</summary>
    private static string Outcome(SparOutcome outcome) => outcome.Kind switch
    {
        SparOutcomeKind.Value => "success",
        SparOutcomeKind.Error => "error",
        _ => outcome.Failure!.Kind,
    };
}
