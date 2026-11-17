using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Spar.Hosting;

/// <summary>
/// Runs the app's <c>spar.OnPageUnresponsive</c> hook (decision "Hangs: the page's hook, the Production stall probe and
/// "Exit now""; <c>docs/app-model.md</c> 3.16) the same way for every host: off the window's UI thread, one change at a
/// time in the order they happened, with the app's root services. The hook only observes; one that throws is logged
/// and the next change still reaches it.
/// </summary>
internal sealed class PageHangHook(Action<PageHang, IServiceProvider>? handler, ILogger logger)
{
    private static readonly Action<ILogger, string, Exception?> HookFailed = LoggerMessage.Define<string>(
        LogLevel.Error, new EventId(35, "PageHangHookFailed"), "The app's page hang hook failed for a change to {State}");

    private readonly Channel<PageHang> _changes = Channel.CreateUnbounded<PageHang>(new UnboundedChannelOptions { SingleReader = true });
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

    /// <summary>Stops admitting changes and completes once the queued ones reached the hook.</summary>
    public Task StopAsync()
    {
        _changes.Writer.TryComplete();
        return _worker;
    }

    private async Task CallAllAsync(IServiceProvider services)
    {
        await foreach (var hang in _changes.Reader.ReadAllAsync())
        {
            try
            {
                handler!(hang, services);
            }
            catch (Exception exception)
            {
                HookFailed(logger, hang.State.ToString(), exception);
            }
        }
    }
}
