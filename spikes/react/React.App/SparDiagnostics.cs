using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace React.App;

/// <summary>
/// Spike of Spar's always-on diagnostics: one Activity and one histogram sample per bridge command, using only BCL
/// primitives. With no listener attached (no telemetry layer), StartActivity returns null and Enabled is false.
/// </summary>
internal static class SparDiagnostics
{
    public const string Name = "Spar";

    private static readonly ActivitySource Source = new(Name, "0.1");
    private static readonly Meter Meter = new(Name, "0.1");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("spar.call.duration", "ms", "Bridge command duration");

    /// <summary>An optional sink for one line per call; the telemetry layer routes it to OpenTelemetry logs.</summary>
    public static Action<string, string, double, bool>? CallCompleted { get; set; }

    public static Func<string, string, ReadOnlyMemory<byte>, CancellationToken, ValueTask<byte[]>> Instrument(
        Func<string, string, ReadOnlyMemory<byte>, CancellationToken, ValueTask<byte[]>> invoke) =>
        async (contract, command, payload, cancellationToken) =>
        {
            using var activity = Source.StartActivity($"{contract}.{command}", ActivityKind.Server);
            activity?.SetTag("spar.contract", contract);
            activity?.SetTag("spar.command", command);
            activity?.SetTag("spar.request.bytes", payload.Length);
            var started = Stopwatch.GetTimestamp();
            var ok = false;
            try
            {
                var result = await invoke(contract, command, payload, cancellationToken);
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
}
