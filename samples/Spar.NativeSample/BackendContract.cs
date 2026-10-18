using Sotsera.Spar.Bridge;

namespace Spar.NativeSample;

[SparContract]
public interface ISampleBackend
{
    [SparOperation("greet", "greeting.read")]
    Task<GreetingReply> GreetAsync(GreetingRequest request, CancellationToken cancellationToken);

    [SparOperation("counter", "counter.change")]
    Task<CounterReply> CounterAsync(CounterRequest request, CancellationToken cancellationToken);

    [SparOperation("delay", "delay.run")]
    Task<DelayReply> DelayAsync(DelayRequest request, CancellationToken cancellationToken);

    [SparOperation("denied", "sample.denied")]
    Task<DeniedReply> DeniedAsync(DeniedRequest request, CancellationToken cancellationToken);

    [SparOperation("report", "sample.report")]
    Task<ReportReply> ReportAsync(ReportRequest request, CancellationToken cancellationToken);
}

public sealed record GreetingRequest(string Name);
public sealed record GreetingReply(string Greeting, int Counter, int ActiveDelays, int Phase, int DeniedEntries);
public sealed record CounterRequest(int Delta);
public sealed record CounterReply(int Value);
public sealed record DelayRequest(int Milliseconds, bool IgnoreCancellation);
public sealed record DelayReply(string Outcome);
public sealed record DeniedRequest(string Value);
public sealed record DeniedReply(bool Called);
public sealed record ReportRequest(string Stage, string Detail);
public sealed record ReportReply(bool Accepted);
