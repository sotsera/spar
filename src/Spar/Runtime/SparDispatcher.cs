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
internal sealed class SparDispatcher(IServiceProvider services, SparCatalog catalog, ILogger? testLog = null)
{
    /// <summary>The app's bridge limits (<see cref="SparBuilder.Bridge"/>), or the defaults.</summary>
    public BridgeLimits Limits { get; } = services.GetService<BridgeLimits>() ?? BridgeLimits.Default;

    /// <summary>
    /// The bridge's logger: the app's, through its <c>ILoggerFactory</c> (nothing is written when the app adds no
    /// logging), and in a test also the app instance's own log.
    /// </summary>
    public ILogger Logger { get; } = BothLoggers.Of(services.GetService<ILoggerFactory>()?.CreateLogger("Spar.Bridge"), testLog);

    /// <summary>The app's clock: <see cref="TimeProvider.System"/> unless a test replaced it.</summary>
    public TimeProvider Time { get; } = services.GetService<TimeProvider>() ?? TimeProvider.System;

    /// <summary>Whether the app has this command: only names it has go into telemetry tags, which the page cannot inflate.</summary>
    public bool Knows(string contract, string command) => catalog.TryGet(contract, command, out _);

    /// <summary>
    /// The devtools' record of recent calls, set by the devtools of a Development build; null everywhere else, and absent
    /// from any other build (the switch is read at each use, so the trimmer folds it away).
    /// </summary>
    internal Devtools.CallRecorder? Recorder { get; set; }

    public async ValueTask<SparOutcome> InvokeAsync(
        string contract, string command, long callId, ReadOnlyMemory<byte> request, CancellationToken cancellationToken,
        SparCaller caller = SparCaller.Page)
    {
        var record = Devtools.SparDevtools.IsSupported ? Recorder?.Begin(callId, contract, command, caller) : null;
        var outcome = await RunAsync(contract, command, callId, request, caller, cancellationToken);
        if (Devtools.SparDevtools.IsSupported)
        {
            record?.End(outcome);
        }

        return outcome;
    }

    private async ValueTask<SparOutcome> RunAsync(
        string contract, string command, long callId, ReadOnlyMemory<byte> request, SparCaller caller, CancellationToken cancellationToken)
    {
        if (!catalog.TryGet(contract, command, out var entry))
        {
            return SparOutcome.FromFailure(SparFailure.UnknownCommand(contract, command));
        }

        using var limit = entry.TimeoutSeconds > 0 ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken) : null;
        limit?.CancelAfter(TimeSpan.FromSeconds(entry.TimeoutSeconds));
        var callToken = limit?.Token ?? cancellationToken;

        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<SparCall>().Start(callId, contract, command, caller, callToken);

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

    public SparCaller Caller { get; private set; }

    public void Start(long callId, string contract, string command, SparCaller caller, CancellationToken cancellationToken)
    {
        CallId = callId;
        Contract = contract;
        Command = command;
        Caller = caller;
        CancellationToken = cancellationToken;
    }
}

/// <summary>Writes each entry to the app's logger and to the test's, when there is one of each.</summary>
internal sealed class BothLoggers(ILogger first, ILogger second) : ILogger
{
    public static ILogger Of(ILogger? app, ILogger? test) => (app, test) switch
    {
        (null, null) => NullLogger.Instance,
        ({ } only, null) => only,
        (null, { } only) => only,
        ({ } a, { } b) => new BothLoggers(a, b),
    };

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => first.IsEnabled(logLevel) || second.IsEnabled(logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        first.Log(logLevel, eventId, state, exception, formatter);
        second.Log(logLevel, eventId, state, exception, formatter);
    }
}
