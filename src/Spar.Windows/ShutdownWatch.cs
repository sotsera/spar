using System.Diagnostics;
using Spar.Hosting;
using Windows.Win32.Foundation;

namespace Spar.Windows;

/// <summary>
/// The shutdown hang policy (<see cref="ShutdownHang"/>): from the start of the teardown until the process is about to
/// return its exit code, a thread of its own waits. If the shutdown has not finished after about 10 s, it shows "Keep
/// waiting / Exit now", and again every 30 s after "Keep waiting". The prompt has no owner: by then the window may be
/// destroyed (the app's services are disposed after it), and a window whose thread is the one stuck would block the
/// prompt itself, since a dialog disables its owner through messages to the owner's thread. A shutdown that finishes
/// takes the prompt down. Only "Exit now" ends the process, with exit code 10; in the automated mode nobody can answer,
/// so the watch only logs.
/// </summary>
internal sealed class ShutdownWatch
{
    private const int KeepWaitingId = 300;
    private const int ExitNowId = 301;

    private readonly string _title;
    private readonly HostLog _log;
    private readonly bool _automated;
    private readonly int _firstPrompt;
    private readonly int _askAgain;
    private readonly ManualResetEventSlim _done = new();
    private readonly Lock _gate = new();
    private readonly long _started = Stopwatch.GetTimestamp();
    private PromptDialog? _prompt;

    private ShutdownWatch(string title, HostLog log, bool automated, int firstPrompt, int askAgain)
    {
        _title = title;
        _log = log;
        _automated = automated;
        _firstPrompt = firstPrompt;
        _askAgain = askAgain;
    }

    /// <summary>Starts watching the shutdown that begins now.</summary>
    public static ShutdownWatch Start(string title, HostLog log, bool automated, int firstPrompt, int askAgain)
    {
        var watch = new ShutdownWatch(title, log, automated, firstPrompt, askAgain);
        var thread = new Thread(watch.Watch) { Name = "Spar shutdown watch", IsBackground = true };
        thread.Start();
        return watch;
    }

    /// <summary>The shutdown finished: the process returns its exit code now. Takes an open prompt down.</summary>
    public void Complete()
    {
        _done.Set();
        lock (_gate)
        {
            _prompt?.Dismiss();
        }
    }

    private void Watch()
    {
        try
        {
            var wait = _firstPrompt;
            while (!_done.Wait(wait))
            {
                var seconds = Stopwatch.GetElapsedTime(_started).TotalSeconds;
                _log.Warn($"The app has been closing for {seconds:0.0} s without finishing.");
                wait = _askAgain;
                if (_automated)
                {
                    _log.Info("The automated mode shows no shutdown prompt; still waiting.");
                    continue;
                }

                var choice = Ask(seconds);
                if (choice == ExitNowId)
                {
                    ExitNow(seconds);
                    return;
                }

                if (choice == PromptDialog.Dismissed)
                {
                    return;
                }

                _log.Step("shutdown prompt: Keep waiting");
            }
        }
        catch (Exception exception)
        {
            _log.Error("The shutdown watch failed", exception);
        }
    }

    /// <summary>Shows the prompt and waits for it on this thread, which only watches; the shutdown goes on meanwhile.</summary>
    private int Ask(double seconds)
    {
        var instruction = $"{_title} is taking long to close";
        var content = $"It has been closing for {seconds:0} seconds. You can keep waiting, or exit now: exiting now skips "
            + "the app's remaining cleanup, so what it has not saved yet may be lost.";
        PromptDialog prompt;
        lock (_gate)
        {
            if (_done.IsSet)
            {
                return PromptDialog.Dismissed;
            }

            prompt = PromptDialog.Show(
                HWND.Null,
                _title,
                instruction,
                content,
                new PromptButton(KeepWaitingId, "Keep waiting"),
                new PromptButton(ExitNowId, "Exit now"),
                takeForeground: true,
                exception => _log.Error("The shutdown prompt failed", exception));
            _prompt = prompt;
        }

        _log.Step("shutdown prompt shown (Keep waiting / Exit now)");

        // This thread exists to wait: blocking it holds up neither the shutdown nor the prompt, which has its own.
        var choice = prompt.Result.GetAwaiter().GetResult();
        lock (_gate)
        {
            _prompt = null;
        }

        return choice;
    }

    /// <summary>The user's choice: the log is flushed (briefly), then the process ends with code 10 at once.</summary>
    private void ExitNow(double seconds)
    {
        _log.Step($"shutdown prompt: Exit now after {seconds:0.0} s; exit {ShutdownHang.ExitCode}");
        _log.Close();
        ProcessControl.ExitNow(ShutdownHang.ExitCode);
    }
}
