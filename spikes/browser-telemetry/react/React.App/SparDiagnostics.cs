using System.Diagnostics;
using System.Diagnostics.Metrics;
using Spike;

namespace React.App;

/// <summary>
/// Spike of Spar's always-on diagnostics: one Activity and one histogram sample per bridge command, using only BCL
/// primitives. With no listener attached (no telemetry layer), StartActivity returns null and Enabled is false.
/// When the page sent a W3C trace context, the command's Activity is its child, so one trace spans page and backend.
/// </summary>
internal static class SparDiagnostics
{
    public const string Name = "Spar";

    private static readonly ActivitySource Source = new(Name, "0.1");
    private static readonly Meter Meter = new(Name, "0.1");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("spar.call.duration", "ms", "Bridge command duration");

    /// <summary>An optional sink for one line per call; the telemetry layer routes it to OpenTelemetry logs.</summary>
    public static Action<string, string, double, bool>? CallCompleted { get; set; }

    public static SparInvoke Instrument(SparInvoke invoke) =>
        async (contract, command, payload, trace, cancellationToken) =>
        {
            using var activity = StartActivity(contract, command, trace);
            activity?.SetTag("rpc.system", "spar");
            activity?.SetTag("rpc.service", contract);
            activity?.SetTag("rpc.method", command);
            activity?.SetTag("spar.request.bytes", payload.Length);
            var started = Stopwatch.GetTimestamp();
            var ok = false;
            try
            {
                var result = await invoke(contract, command, payload, trace, cancellationToken);
                activity?.SetTag("spar.response.bytes", result.Length);
                ok = true;
                return result;
            }
            catch (Exception exception)
            {
                activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
                throw;
            }
            finally
            {
                var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                if (Duration.Enabled)
                {
                    Duration.Record(elapsed, new("spar.contract", contract), new("spar.command", command), new("spar.ok", ok));
                }

                CallCompleted?.Invoke(contract, command, elapsed, ok);
            }
        };

    private static Activity? StartActivity(string contract, string command, SparTraceContext trace)
    {
        // Cheap exit first: no listener means no parsing either.
        if (!Source.HasListeners())
        {
            return null;
        }

        // A malformed or absent context starts a new trace; it never fails the call.
        var parent = !trace.IsEmpty && ActivityContext.TryParse(trace.TraceParent, trace.TraceState, isRemote: true, out var context)
            ? context
            : default;

        // An explicit parent wins over any ambient Activity (the loopback host's ASP.NET Core request, for instance).
        return Source.StartActivity($"{contract}/{command}", ActivityKind.Server, parent);
    }
}
