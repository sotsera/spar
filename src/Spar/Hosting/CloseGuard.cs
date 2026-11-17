using Microsoft.Extensions.Logging;

namespace Spar.Hosting;

/// <summary>What a host does with a close request once the guards have answered.</summary>
internal enum CloseVerdictKind
{
    Close,
    Keep,
    Ask,
}

/// <summary>A close request's outcome; <see cref="Question"/> is set for <see cref="CloseVerdictKind.Ask"/>.</summary>
internal readonly record struct CloseVerdict(CloseVerdictKind Kind, string? Question = null)
{
    public static CloseVerdict Close { get; } = new(CloseVerdictKind.Close);

    public static CloseVerdict Keep { get; } = new(CloseVerdictKind.Keep);
}

/// <summary>
/// Runs the backend's close guard (<c>spar.OnCloseRequested</c>, <c>docs/app-model.md</c> 6.11) the same way for every
/// host: off the caller's thread, so a window's UI thread never waits on app code; at session end within a short
/// deadline, where nothing may keep the window open or ask; and in the automated mode, where an ask counts as close.
/// </summary>
internal sealed class CloseGuard(
    Func<CloseRequest, IServiceProvider, CancellationToken, ValueTask<CloseDecision>>? guard,
    IServiceProvider services,
    ILogger logger,
    int sessionEndDeadlineMilliseconds = CloseGuard.SessionEndDeadlineMilliseconds)
{
    /// <summary>How long guards get when Windows logs off or shuts down (decision "Two-stage close").</summary>
    public const int SessionEndDeadlineMilliseconds = 5_000;

    /// <summary>The question shown when the guard itself failed: closing might lose work, so the user decides.</summary>
    public const string FailedGuardQuestion = "The app could not check whether it is safe to close. Close anyway?";

    private static readonly Action<ILogger, string, string, string, Exception?> Decided = LoggerMessage.Define<string, string, string>(
        LogLevel.Information, new EventId(20, "CloseGuardDecided"), "Close request for window {Window} ({Reason}): the backend's guard answered {Decision}");

    private static readonly Action<ILogger, string, string, Exception?> GuardFailed = LoggerMessage.Define<string, string>(
        LogLevel.Error, new EventId(21, "CloseGuardFailed"), "Close request for window {Window} ({Reason}): the backend's guard failed");

    private static readonly Action<ILogger, string, int, Exception?> GuardLate = LoggerMessage.Define<string, int>(
        LogLevel.Warning, new EventId(22, "CloseGuardLate"), "Close request for window {Window} at session end: the backend's guard did not answer within {Milliseconds} ms; closing");

    /// <summary>Whether the app declared a guard; without one every request closes.</summary>
    public bool HasGuard => guard is not null;

    /// <summary>Asks the backend's guard, on the thread pool, and applies the rules of the request's reason and mode.</summary>
    public async Task<CloseVerdict> DecideAsync(CloseRequest request, CancellationToken cancellationToken)
    {
        if (guard is null)
        {
            return CloseVerdict.Close;
        }

        var sessionEnd = request.Reason == CloseReason.SessionEnd;
        using var deadline = sessionEnd ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken) : null;
        deadline?.CancelAfter(sessionEndDeadlineMilliseconds);
        var token = deadline?.Token ?? cancellationToken;

        CloseDecision decision;
        try
        {
            // The guard is app code: it never runs on the caller's thread, which is a window's UI thread. A guard that
            // ignores its token still loses its say at the deadline.
            var running = Task.Run(() => guard(request, services, token).AsTask(), token);
            decision = await running.WaitAsync(token);
        }
        catch (OperationCanceledException) when (deadline is not null && deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            GuardLate(logger, request.Window, sessionEndDeadlineMilliseconds, null);
            return CloseVerdict.Close;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            GuardFailed(logger, request.Window, request.Reason.ToString(), exception);
            return Unanswerable(request) ? CloseVerdict.Close : new CloseVerdict(CloseVerdictKind.Ask, FailedGuardQuestion);
        }

        Decided(logger, request.Window, request.Reason.ToString(), decision.ToString(), null);
        return Apply(request, decision);
    }

    /// <summary>
    /// The rules of decision "Two-stage close": at session end nothing keeps the window open or asks, and in the
    /// automated mode an ask counts as close, so a test of saving on close needs no person.
    /// </summary>
    internal static CloseVerdict Apply(CloseRequest request, CloseDecision decision)
    {
        if (decision.IsClose)
        {
            return CloseVerdict.Close;
        }

        if (request.Reason == CloseReason.SessionEnd)
        {
            return CloseVerdict.Close;
        }

        if (decision.IsAsk)
        {
            return request.IsAutomated ? CloseVerdict.Close : new CloseVerdict(CloseVerdictKind.Ask, decision.Question);
        }

        return CloseVerdict.Keep;
    }

    /// <summary>Nobody can answer a question at session end or in the automated mode.</summary>
    private static bool Unanswerable(CloseRequest request) => request.Reason == CloseReason.SessionEnd || request.IsAutomated;
}
