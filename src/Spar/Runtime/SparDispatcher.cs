using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Spar.Runtime;

/// <summary>What a generated command handler gets: its scope's services, the request JSON and the call token.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class SparCallContext(IServiceProvider services, ReadOnlyMemory<byte> request, CancellationToken cancellationToken)
{
    public IServiceProvider Services { get; } = services;

    /// <summary>The request's JSON; empty for a command that takes none.</summary>
    public ReadOnlyMemory<byte> Request { get; } = request;

    public CancellationToken CancellationToken { get; } = cancellationToken;
}

/// <summary>
/// Runs one command: its own DI scope, the call filled into <see cref="ISparCall"/>, the command's time limit folded
/// into the token, and every outcome turned into a <see cref="SparOutcome"/>. Nothing is ever retried.
/// </summary>
internal sealed class SparDispatcher(IServiceProvider services, SparCatalog catalog)
{
    /// <summary>The app's bridge limits (<see cref="SparBuilder.Bridge"/>), or the defaults.</summary>
    public BridgeLimits Limits { get; } = services.GetService<BridgeLimits>() ?? BridgeLimits.Default;

    /// <summary>The app's logger for the bridge; nothing is written when the app adds no logging.</summary>
    public ILogger Logger { get; } = services.GetService<ILoggerFactory>()?.CreateLogger("Spar.Bridge") ?? NullLogger.Instance;

    /// <summary>Whether the app has this command: only names it has go into telemetry tags, which the page cannot inflate.</summary>
    public bool Knows(string contract, string command) => catalog.TryGet(contract, command, out _);

    public async ValueTask<SparOutcome> InvokeAsync(
        string contract, string command, long callId, ReadOnlyMemory<byte> request, CancellationToken cancellationToken)
    {
        if (!catalog.TryGet(contract, command, out var entry))
        {
            return SparOutcome.FromFailure(SparFailure.UnknownCommand(contract, command));
        }

        using var limit = entry.TimeoutSeconds > 0 ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken) : null;
        limit?.CancelAfter(TimeSpan.FromSeconds(entry.TimeoutSeconds));
        var callToken = limit?.Token ?? cancellationToken;

        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<SparCall>().Start(callId, contract, command, callToken);

        try
        {
            return await entry.Handler(new SparCallContext(scope.ServiceProvider, request, callToken));
        }
        catch (OperationCanceledException exception) when (exception.CancellationToken == callToken && callToken.IsCancellationRequested)
        {
            // Confirmed: the command ended on its own call token. Another token's cancellation is not a confirmation.
            return SparOutcome.FromFailure(SparFailure.Cancelled());
        }
        catch (Exception exception)
        {
            return SparOutcome.FromFailure(SparFailure.Failed(exception));
        }
    }
}

/// <summary>The scoped <see cref="ISparCall"/> of one command, filled by the dispatcher before the contract is resolved.</summary>
internal sealed class SparCall : ISparCall
{
    public long CallId { get; private set; }

    public CancellationToken CancellationToken { get; private set; }

    public string Contract { get; private set; } = "";

    public string Command { get; private set; } = "";

    public void Start(long callId, string contract, string command, CancellationToken cancellationToken)
    {
        CallId = callId;
        Contract = contract;
        Command = command;
        CancellationToken = cancellationToken;
    }
}
