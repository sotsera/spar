namespace Sotsera.Spar.Bridge;

internal sealed class BridgeCall : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _source = new();
    private readonly TaskCompletionSource _cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _cancellation;
    private bool _completing;

    internal CancellationToken Token => _source.Token;

    internal Task CancellationRequested => _cancelled.Task;

    internal void RequestCancellation()
    {
        lock (_gate)
        {
            if (_completing || _cancellation is not null)
            {
                return;
            }

            _cancellation = _source.CancelAsync();
            _cancelled.TrySetResult();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? cancellation;
        lock (_gate)
        {
            _completing = true;
            cancellation = _cancellation;
        }

        try
        {
            if (cancellation is not null)
            {
                await cancellation.ConfigureAwait(false);
            }
        }
        catch (AggregateException)
        {
            // A throwing cancellation callback does not prevent other callbacks or ownership cleanup.
        }
        finally
        {
            _source.Dispose();
        }
    }
}
