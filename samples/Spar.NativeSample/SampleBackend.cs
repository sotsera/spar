namespace Spar.NativeSample;

// Probe reports belong to this consumer, not the shipping host or bridge runtime.
internal sealed class SampleBackend(Action<string> log, Action<string> control) : ISampleBackend
{
    private int _counter;
    private int _activeDelays;
    private int _phase;
    private int _deniedEntries;

    public Task<GreetingReply> GreetAsync(GreetingRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new GreetingReply("Hello, " + request.Name, Volatile.Read(ref _counter),
            Volatile.Read(ref _activeDelays), Volatile.Read(ref _phase), Volatile.Read(ref _deniedEntries)));
    }

    public Task<CounterReply> CounterAsync(CounterRequest request, CancellationToken cancellationToken)
    {
        log("counter-entered");
        return Task.FromResult(new CounterReply(Interlocked.Add(ref _counter, request.Delta)));
    }

    public async Task<DelayReply> DelayAsync(DelayRequest request, CancellationToken cancellationToken)
    {
        if (request.Milliseconds is < 1 or > 5000)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        Interlocked.Increment(ref _activeDelays);
        log("delay-entered");
        try
        {
            await Task.Delay(request.Milliseconds, request.IgnoreCancellation ? CancellationToken.None : cancellationToken)
                .ConfigureAwait(false);
            return new DelayReply("finished");
        }
        finally
        {
            Interlocked.Decrement(ref _activeDelays);
            log("delay-finished");
        }
    }

    public Task<DeniedReply> DeniedAsync(DeniedRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _deniedEntries);
        log("denied-entered");
        return Task.FromResult(new DeniedReply(true));
    }

    public Task<ReportReply> ReportAsync(ReportRequest request, CancellationToken cancellationToken)
    {
        log("report-" + request.Stage + ":" + request.Detail);
        if (request.Stage == "reload")
        {
            Interlocked.Exchange(ref _phase, 1);
            control("reload");
        }
        else if (request.Stage == "close")
        {
            control("close");
        }

        return Task.FromResult(new ReportReply(true));
    }

    public void NavigationDenied()
    {
        Interlocked.Exchange(ref _phase, 2);
    }
}
