using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Spar.Hosting;

/// <summary>
/// Runs the app's <c>spar.OnPageUnresponsive</c> hook (decision "Hangs: the page's hook, the Production stall probe and
/// "Exit now""; <c>docs/app-model.md</c> 3.16) the same way for every host: off the window's UI thread, one change at a
/// time in the order they happened, awaited before the next, with the app's root services. The hook only observes; one
/// that throws is logged and the next change still reaches it. At exit the changes already queued (a "closed anyway"
/// comes just before it) still reach the hook within a short wait; when the wait ends, the hook's token is cancelled
/// and what is still queued is dropped.
/// </summary>
internal sealed class PageHangHook(Func<PageHang, IServiceProvider, CancellationToken, ValueTask>? handler, ILogger logger)
{
    private static readonly Action<ILogger, string, Exception?> HookFailed = LoggerMessage.Define<string>(
        LogLevel.Error, new EventId(35, "PageHangHookFailed"), "The app's page hang hook failed for a change to {State}");

    private readonly Channel<PageHang> _changes = Channel.CreateUnbounded<PageHang>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _stopping = new();
    private Task _worker = Task.CompletedTask;

    /// <summary>Starts calling the hook once the app's services exist; changes reported before then wait for them.</summary>
    public void Start(IServiceProvider services)
    {
        if (handler is not null)
        {
            _worker = Task.Run(() => CallAllAsync(services));
        }
    }

    /// <summary>Queues a change for the hook; safe from the UI thread, which never waits for the hook.</summary>
    public void Report(PageHang hang)
    {
        if (handler is not null)
        {
            _changes.Writer.TryWrite(hang);
        }
    }

    /// <summary>
    /// Stops admitting changes and gives the queued ones <paramref name="settle"/> to reach the hook. True when they
    /// all did; otherwise the hook's token is cancelled, the rest are dropped, and the process does not wait further.
    /// </summary>
    public async Task<bool> StopAsync(TimeSpan settle)
    {
        _changes.Writer.TryComplete();
        try
        {
            await _worker.WaitAsync(settle);
            return true;
        }
        catch (TimeoutException)
        {
            await _stopping.CancelAsync();
            return false;
        }
    }

    private async Task CallAllAsync(IServiceProvider services)
    {
        var token = _stopping.Token;
        try
        {
            await foreach (var hang in _changes.Reader.ReadAllAsync(token))
            {
                await CallAsync(hang, services, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // The app is exiting and the wait for the hook ended: the changes still queued are dropped.
        }
    }

    private async Task CallAsync(PageHang hang, IServiceProvider services, CancellationToken token)
    {
        try
        {
            await handler!(hang, services, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            HookFailed(logger, hang.State.ToString(), exception);
        }
    }
}
