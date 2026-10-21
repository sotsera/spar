using Sotsera.Spar.Bridge;

namespace Spar.NativeSample;

[SparContract]
public interface ISampleBackend
{
    [SparOperation("greet", "greeting.read")]
    Task<GreetingReply> GreetAsync(GreetingRequest request, CancellationToken cancellationToken);

    [SparOperation("counter", "counter.change")]
    Task<CounterReply> CounterAsync(CounterRequest request, CancellationToken cancellationToken);
}

public sealed record GreetingRequest(string Name);
public sealed record GreetingReply(string Greeting, int Counter);
public sealed record CounterRequest(int Delta);
public sealed record CounterReply(int Value);
