namespace Spar.NativeSample;

internal sealed class SampleBackend : ISampleBackend
{
    private int _counter;

    public Task<GreetingReply> GreetAsync(GreetingRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new GreetingReply("Hello, " + request.Name, Volatile.Read(ref _counter)));
    }

    public Task<CounterReply> CounterAsync(CounterRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new CounterReply(Interlocked.Add(ref _counter, request.Delta)));
    }
}
