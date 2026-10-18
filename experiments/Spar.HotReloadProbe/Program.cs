using System.Diagnostics;
using System.Text.Json;

namespace Sotsera.Spar.HotReloadProbe;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        using var stop = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            stop.Cancel();
        };

        Console.CancelKeyPress += onCancel;

        try
        {
            var state = new ProbeState();
            using var process = Process.GetCurrentProcess();
            var startedAt = process.StartTime.ToUniversalTime();
            var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));

            do
            {
                var sample = new ProbeSample(
                    DateTimeOffset.UtcNow,
                    Environment.ProcessId,
                    startedAt,
                    state.InstanceId,
                    state.NextCounter(),
                    GreetingProvider.GetGreeting(),
                    UpdateObserver.Read());

                Console.WriteLine(JsonSerializer.Serialize(sample, jsonOptions));

                if (args.Contains("--once", StringComparer.Ordinal))
                {
                    break;
                }
            }
            while (await timer.WaitForNextTickAsync(stop.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // A normal terminal Ctrl+C ends the probe; it is not a failed code update.
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }
}

internal sealed record ProbeSample(
    DateTimeOffset ObservedAt,
    int ProcessId,
    DateTime ProcessStartedAtUtc,
    Guid InstanceId,
    int Counter,
    string Greeting,
    UpdateSnapshot Update);
