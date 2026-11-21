using System.Diagnostics;
using System.Runtime.InteropServices;
using Spar.Hosting;

namespace Spar.Linux;

/// <summary>
/// The end of the OS session on Linux (decision "The Linux black box: AT-SPI over GDBus; the dialog's title; SIGTERM as a
/// session end"): <c>SIGTERM</c> or <c>SIGINT</c>, which a session manager, <c>systemd</c> or a terminal sends, starts the
/// session-end close as Windows' <c>WM_ENDSESSION</c> does: both guards asked with <see cref="CloseReason.SessionEnd"/>,
/// nothing able to keep the window, the teardown, then the run's last work (the services' disposal), all within the
/// session end's 4 s from the signal, then the final line. The signals come through .NET's
/// <see cref="PosixSignalRegistration"/>, which keeps the runtime's own handlers in place, and the handler only posts:
/// nothing waits on the GLib main loop or on the signal's thread.
/// </summary>
internal static class SessionEnd
{
    /// <summary>The session end's whole budget, from the signal to the final line (Windows' <c>WM_ENDSESSION</c> hold).</summary>
    public static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(AppWindow.SessionEndMilliseconds);

    /// <summary>What a session end came to: its final line's outcome, how long it held, and whether the last work finished.</summary>
    internal readonly record struct Outcome(string Text, double HeldMilliseconds, bool Finished);

    /// <summary>
    /// Runs a session end that started at <paramref name="start"/> (a stopwatch timestamp), off the loop: the window's
    /// session-end close on <paramref name="ui"/>, then <paramref name="lastWork"/> once the window is gone, each cut at
    /// the budget's end. The caller writes the final line and ends what it owns.
    /// </summary>
    public static async Task<Outcome> RunAsync(MainLoop ui, AppWindow window, Func<Task> lastWork, long start)
    {
        var closing = new TaskCompletionSource<Task<CloseReport>>(TaskCreationOptions.RunContinuationsAsynchronously);
        ui.Post(() => closing.TrySetResult(window.EndSession(start)), "session end");
        var close = await closing.Task;
        var closed = await WithinAsync(close, start);

        // The last work starts once the window is gone, in time or not; the session end only stops waiting for it at the
        // budget's end (a test window whose teardown outlasted the budget under load kept its services otherwise).
        var work = close.ContinueWith(_ => lastWork(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
        var finished = closed && await WithinAsync(work, start);

        var held = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var text = !closed ? "the window did not close in time" : finished ? "window closed, last work done" : "window closed, last work still running";
        return new Outcome(text, held, finished);
    }

    /// <summary>What is left of the budget from <paramref name="start"/>, never negative.</summary>
    public static TimeSpan Left(long start)
    {
        var left = Budget - Stopwatch.GetElapsedTime(start);
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    /// <summary>Whether <paramref name="task"/> ended before the budget did; its failure counts as ended (it is logged where it ran).</summary>
    private static async Task<bool> WithinAsync(Task task, long start)
    {
        await Task.WhenAny(task, Task.Delay(Left(start)));
        return task.IsCompleted;
    }
}

/// <summary>
/// <c>SIGTERM</c> and <c>SIGINT</c> for an app's run (never a test process's, whose signals are its runner's): the first
/// starts the session end, a second one ends the process at once. Each is taken from the runtime's default (which ends
/// the process with 128 + the signal's number) by cancelling it.
/// </summary>
internal sealed class SessionEndSignals : IDisposable
{
    private const int Terminate = 15;
    private const int Interrupt = 2;

    private readonly Action<string> _first;
    private readonly Action<string, int> _again;
    private readonly PosixSignalRegistration _terminate;
    private readonly PosixSignalRegistration _interrupt;
    private int _count;

    private SessionEndSignals(Action<string> first, Action<string, int> again)
    {
        _first = first;
        _again = again;
        _terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnSignal);
        _interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, OnSignal);
    }

    /// <summary>
    /// Takes both signals from now on: <paramref name="first"/> gets the first one's name, <paramref name="again"/> a
    /// later one's name and the exit code it ends with (128 + its number: 143 for <c>SIGTERM</c>, 130 for <c>SIGINT</c>).
    /// Both are called on the runtime's signal thread, and must only start work, never wait for it.
    /// </summary>
    public static SessionEndSignals Register(Action<string> first, Action<string, int> again) => new(first, again);

    public void Dispose()
    {
        _terminate.Dispose();
        _interrupt.Dispose();
    }

    private void OnSignal(PosixSignalContext context)
    {
        context.Cancel = true;
        var terminate = context.Signal == PosixSignal.SIGTERM;
        var name = terminate ? "SIGTERM" : "SIGINT";
        if (Interlocked.Increment(ref _count) == 1)
        {
            _first(name);
        }
        else
        {
            _again(name, 128 + (terminate ? Terminate : Interrupt));
        }
    }
}
