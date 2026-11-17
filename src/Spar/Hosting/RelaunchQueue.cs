using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Spar.Hosting;

/// <summary>
/// The relaunches handed to the running instance (decision "The relaunch handler"; <c>docs/app-model.md</c> 3.14),
/// for every host: a relaunch is acknowledged as soon as it is queued here and never replayed. The app's handler runs
/// off the window's UI thread, one relaunch at a time in arrival order, with the app's root services; relaunches that
/// arrive before the services exist wait for them. The token the handler gets is cancelled when the app stops.
/// </summary>
internal sealed class RelaunchQueue(Func<Relaunch, IServiceProvider, CancellationToken, ValueTask>? handler, ILogger logger)
{
    private static readonly Action<ILogger, int, Exception?> Ignored = LoggerMessage.Define<int>(
        LogLevel.Information, new EventId(30, "RelaunchIgnored"), "A relaunch with {Count} arguments: the app has no relaunch handler");

    private static readonly Action<ILogger, Exception?> HandlerFailed = LoggerMessage.Define(
        LogLevel.Error, new EventId(31, "RelaunchHandlerFailed"), "The app's relaunch handler failed");

    private readonly Channel<Relaunch> _relaunches = Channel.CreateUnbounded<Relaunch>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _stopping = new();
    private Task _worker = Task.CompletedTask;

    /// <summary>Queues a relaunch; false once the app is stopping, when the caller must not acknowledge it.</summary>
    public bool TryAdd(Relaunch relaunch) => _relaunches.Writer.TryWrite(relaunch);

    /// <summary>Starts handling queued relaunches once the app's services exist.</summary>
    public void Start(IServiceProvider services) => _worker = Task.Run(() => HandleAllAsync(services));

    /// <summary>Stops admitting relaunches, cancels the handler's token and waits for the one running, if any.</summary>
    public async Task StopAsync()
    {
        _relaunches.Writer.TryComplete();
        await _stopping.CancelAsync();
        try
        {
            await _worker;
        }
        finally
        {
            _stopping.Dispose();
        }
    }

    private async Task HandleAllAsync(IServiceProvider services)
    {
        var token = _stopping.Token;
        try
        {
            await foreach (var relaunch in _relaunches.Reader.ReadAllAsync(token))
            {
                await HandleAsync(relaunch, services, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // The app is stopping: relaunches still queued were acknowledged and are dropped, never replayed.
        }
    }

    private async Task HandleAsync(Relaunch relaunch, IServiceProvider services, CancellationToken token)
    {
        if (handler is null)
        {
            Ignored(logger, relaunch.Arguments.Count, null);
            return;
        }

        try
        {
            await handler(relaunch, services, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // One failing relaunch never stops the next one, nor the app.
            HandlerFailed(logger, exception);
        }
    }
}
