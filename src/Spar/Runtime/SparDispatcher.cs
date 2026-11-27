using System.Diagnostics;
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

    /// <summary>
    /// The blob a <see cref="SparResourceAttribute"/> command returned, which the host serves for the load that ran it;
    /// null for none (a 404).
    /// </summary>
    public SparBlob? Resource { get; set; }

    /// <summary>The handles of the call's document, where a result's blobs go; null when nothing can hold them.</summary>
    internal BlobOwner? BlobOwner { get; init; }

    /// <summary>The window the call came from, whose grants a request's ids name; null for none.</summary>
    internal Files.HostWindow? Window => BlobOwner?.Window;
}

/// <summary>
/// Runs one command: its own DI scope, the call filled into <see cref="ISparCall"/>, the command's time limit folded
/// into the token, and every outcome turned into a <see cref="SparOutcome"/>. Nothing is ever retried.
/// </summary>
internal sealed class SparDispatcher(IServiceProvider services, SparCatalog catalog, ILogger? testLog = null)
{
    private readonly SerialGroups _serial = new();

    /// <summary>The app's serial groups; a bridge session takes a call's place in its group as it admits the call.</summary>
    internal SerialGroups Serial => _serial;

    /// <summary>The app's services, for the host's own commands that use them (the file dialogs).</summary>
    public IServiceProvider Services => services;

    /// <summary>Every blob handle of the app's documents (<see cref="SparBlob"/>).</summary>
    public BlobStore Blobs { get; } = new();

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
    /// The record of recent calls, set by the devtools of a Development build and by a scene of the test host (its
    /// <c>calls.json</c>); null everywhere else, and absent from a Production build (both switches are read at each use,
    /// so the trimmer folds them away).
    /// </summary>
    internal Devtools.CallRecorder? Recorder { get; set; }

    /// <summary>The command of that name, or null; the bridge reads a stream's kind from it before it opens the stream.</summary>
    public SparCommandEntry? Find(string contract, string command) => catalog.Find(contract, command);

    public ValueTask<SparOutcome> InvokeAsync(
        string contract, string command, long callId, ReadOnlyMemory<byte> request, CancellationToken cancellationToken,
        SparCaller caller = SparCaller.Page) =>
        InvokeAsync(contract, command, callId, request, null, null, cancellationToken, caller, null);

    /// <summary>A call whose result's blobs become handles of <paramref name="blobs"/>, a document's or the test host's.</summary>
    internal ValueTask<SparOutcome> InvokeAsync(
        string contract, string command, long callId, ReadOnlyMemory<byte> request, BlobOwner? blobs, CancellationToken cancellationToken,
        SparCaller caller = SparCaller.Page) =>
        InvokeAsync(contract, command, callId, request, null, null, cancellationToken, caller, new BlobCall(blobs, IsResource: false));

    /// <summary>
    /// One load of a <see cref="SparResourceAttribute"/> command's address: run like a call, in its own scope, and its
    /// blob given back to serve once; nothing is kept. Only a resource command runs this way, and never as a call.
    /// </summary>
    internal async ValueTask<(SparOutcome Outcome, SparBlob? Blob)> InvokeResourceAsync(
        string contract, string command, ReadOnlyMemory<byte> request, CancellationToken cancellationToken, SparCaller caller = SparCaller.Page)
    {
        var call = new BlobCall(null, IsResource: true);
        var outcome = await InvokeAsync(contract, command, 0, request, null, null, cancellationToken, caller, call);
        return (outcome, call.Resource);
    }

    /// <summary>
    /// Runs a stream or a subscription to its end: its items go to <paramref name="sink"/>, and the outcome is how it ended
    /// (a value for a stream that finished, its declared error, or a failure). A unary command named here, or a stream
    /// named through <see cref="InvokeAsync(string, string, long, ReadOnlyMemory{byte}, CancellationToken, SparCaller)"/>,
    /// is refused before it runs.
    /// </summary>
    public ValueTask<SparOutcome> InvokeStreamAsync(
        string contract, string command, long callId, ReadOnlyMemory<byte> request, SparItemSink sink, CancellationToken cancellationToken,
        SparCaller caller = SparCaller.Page) =>
        InvokeAsync(contract, command, callId, request, sink, null, cancellationToken, caller, null);

    /// <summary>
    /// A page's call, as the bridge runs it: <paramref name="running"/> is told when the command starts, after a serial
    /// call's wait for its group, so the bridge counts the wait in its queue and nothing after it.
    /// </summary>
    internal ValueTask<SparOutcome> InvokeAsync(
        string contract, string command, long callId, ReadOnlyMemory<byte> request, Action running, BlobOwner? blobs,
        CancellationToken cancellationToken, ValueTask<SerialGroups.Turn>? place = null) =>
        InvokeAsync(contract, command, callId, request, null, running, cancellationToken, SparCaller.Page, new BlobCall(blobs, IsResource: false), place);

    private async ValueTask<SparOutcome> InvokeAsync(
        string contract, string command, long callId, ReadOnlyMemory<byte> request, SparItemSink? sink, Action? running,
        CancellationToken cancellationToken,
        SparCaller caller,
        BlobCall? blobs,
        ValueTask<SerialGroups.Turn>? place = null)
    {
        var record = Devtools.SparDevtools.IsSupported || SparTestSession.IsSupported
            ? Recorder?.Begin(callId, contract, command, caller)
            : null;
        var outcome = await RunAsync(contract, command, callId, request, sink, running, caller, blobs, place, cancellationToken);
        if (Devtools.SparDevtools.IsSupported || SparTestSession.IsSupported)
        {
            record?.End(outcome);
        }

        return outcome;
    }

    private async ValueTask<SparOutcome> RunAsync(
        string contract, string command, long callId, ReadOnlyMemory<byte> request, SparItemSink? sink, Action? running, SparCaller caller,
        BlobCall? blobs, ValueTask<SerialGroups.Turn>? place, CancellationToken cancellationToken)
    {
        if (Refusal(contract, command, sink, blobs, out var entry) is { } refused)
        {
            // A place the bridge reserved is given back when its turn comes, so the calls behind it still run.
            if (place is { } reserved)
            {
                await GiveBackAsync(reserved);
            }

            return refused;
        }

        // A serial call waits for its group's turn first; cancelled meanwhile, it never ran.
        SerialGroups.Turn? turn = null;
        if (entry.SerialGroup is { } group)
        {
            var waiting = Time.GetTimestamp();
            try
            {
                // A page's call took its place as the bridge admitted it, in arrival order; any other caller takes it now.
                turn = await (place ?? _serial.EnterAsync(group, cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return SparOutcome.FromFailure(new SparFailure("cancelled", "cancelled-before-start", "notStarted"));
            }

            Activity.Current?.SetTag("spar.serial.group", group);
            Activity.Current?.SetTag("spar.serial.wait", Time.GetElapsedTime(waiting).TotalSeconds);
        }

        using var _ = turn;
        running?.Invoke();

        // The time limit counts from here: a call's wait for its group is not its own time.
        using var limit = entry.TimeoutSeconds > 0 ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken) : null;
        limit?.CancelAfter(TimeSpan.FromSeconds(entry.TimeoutSeconds));
        var callToken = limit?.Token ?? cancellationToken;

        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<SparCall>().Start(callId, contract, command, caller, callToken, blobs?.Owner?.Window);

        try
        {
            var context = new SparCallContext(scope.ServiceProvider, request, callToken) { BlobOwner = blobs?.Owner };

            // A stream's handler has disposed its iterator by the time it returns or throws: `await foreach` does.
            if (sink is not null)
            {
                return await entry.StreamHandler!(context, sink);
            }

            var outcome = await entry.Handler!(context);
            if (blobs is not null)
            {
                blobs.Resource = context.Resource;
            }

            return outcome;
        }
        catch (OperationCanceledException exception) when (exception.CancellationToken == callToken && callToken.IsCancellationRequested)
        {
            // Confirmed: the command ended on its own call token. Another token's cancellation is not a confirmation.
            return SparOutcome.FromFailure(SparFailure.Cancelled());
        }
        catch (SparStreamStopped stopped)
        {
            return SparOutcome.FromFailure(stopped.Failure);
        }
        catch (Exception exception)
        {
            return SparOutcome.FromFailure(SparFailure.Failed(exception));
        }
    }

    /// <summary>Why the call cannot run as it was made, or null; <paramref name="entry"/> is the command when there is one.</summary>
    private SparOutcome? Refusal(string contract, string command, SparItemSink? sink, BlobCall? blobs, out SparCommandEntry entry)
    {
        if (!catalog.TryGet(contract, command, out entry))
        {
            return SparOutcome.FromFailure(SparFailure.UnknownCommand(contract, command));
        }

        // A resource is loaded by its address, never called; and only a resource has an address.
        if (entry.IsResource != (blobs?.IsResource == true))
        {
            var expected = entry.IsResource ? "a resource, loaded by its address" : "a command, not a resource";
            return SparOutcome.FromFailure(SparFailure.InvalidRequest($"{contract}.{command} is {expected}."));
        }

        if (entry.IsStream != sink is not null)
        {
            var expected = entry.IsStream ? "a stream, opened with a stream message" : "a unary command, called with a call message";
            return SparOutcome.FromFailure(SparFailure.InvalidRequest($"{contract}.{command} is {expected}."));
        }

        return null;
    }

    /// <summary>Waits for a reserved place's turn and gives it back at once; one cancelled while it waits is already gone.</summary>
    private static async Task GiveBackAsync(ValueTask<SerialGroups.Turn> reserved)
    {
        try
        {
            (await reserved).Dispose();
        }
        catch (OperationCanceledException)
        {
            // It left the queue when its call was cancelled.
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

    /// <summary>The window the call came from (its grants and dialogs), or null for a call without one.</summary>
    public Files.HostWindow? Window { get; private set; }

    public void Start(long callId, string contract, string command, SparCaller caller, CancellationToken cancellationToken, Files.HostWindow? window)
    {
        CallId = callId;
        Contract = contract;
        Command = command;
        Caller = caller;
        CancellationToken = cancellationToken;
        Window = window;
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

/// <summary>Where a call's blobs go: the handles of its document, or, for a resource's load, the one blob it serves.</summary>
internal sealed record BlobCall(BlobOwner? Owner, bool IsResource)
{
    public SparBlob? Resource { get; set; }
}
