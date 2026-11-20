using System.Collections.Concurrent;
using Spar.Hosting;
using Spar.Linux.Interop;

namespace Spar.Linux;

/// <summary>
/// The Linux side of the shared hang handling (<see cref="PageWatch"/>, <see cref="ShutdownWatch"/>): the main window's
/// bridge, view and main loop for the heartbeat, and the native prompts as <c>GtkAlertDialog</c>s on the main loop,
/// owned by the window or, for the shutdown prompt, by none. GTK is used from the loop's thread only, so a prompt asked
/// for from another thread is shown, and taken down, through the loop; the shutdown keeps the loop running until it
/// has finished, so its prompt can show.
/// </summary>
internal sealed class LinuxPageHost : IPageWatchHost
{
    private readonly MainLoop _ui;
    private readonly HostLog _log;
    private readonly Func<nint> _window;
    private readonly ScriptBridge _bridge;
    private readonly BrowserView _view;
    private readonly ConcurrentQueue<string>? _answers;

    private LinuxPageHost(MainLoop ui, HostLog log, Func<nint> window, ScriptBridge bridge, BrowserView view, ConcurrentQueue<string>? answers)
    {
        _ui = ui;
        _log = log;
        _window = window;
        _bridge = bridge;
        _view = view;
        _answers = answers;
    }

    /// <summary>
    /// Whether the page can be seen: the window mapped, and neither minimised nor, where GTK says so (4.12), suspended
    /// by the compositor. A view that is never presented is a hidden page with throttled timers (pitfalls).
    /// </summary>
    public bool IsPageVisible
    {
        get
        {
            var window = _window();
            if (window == 0 || Gtk.gtk_widget_get_mapped(window) == 0)
            {
                return false;
            }

            var surface = Gtk.gtk_native_get_surface(window);
            return surface == 0 || (Gtk.gdk_toplevel_get_state(surface) & (Gtk.ToplevelMinimized | Gtk.ToplevelSuspended)) == 0;
        }
    }

    /// <summary>
    /// The main window's heartbeat, following <paramref name="bridge"/>'s documents and <paramref name="view"/>'s
    /// reports (loop thread). <paramref name="window"/> gives the GTK window, 0 once it is gone; <paramref name="answers"/>
    /// are a Development build's scripted answers to the dialogs, null otherwise.
    /// </summary>
    public static PageWatch Watch(
        MainLoop ui,
        HostLog log,
        Func<nint> window,
        string title,
        ScriptBridge bridge,
        BrowserView view,
        PageHangHook hook,
        bool automated,
        int interval,
        ConcurrentQueue<string>? answers)
    {
        var watch = new PageWatch(new LinuxPageHost(ui, log, window, bridge, view, answers), log, title, hook, automated, interval);
        bridge.DocumentOpened += watch.DocumentOpened;
        bridge.DocumentClosed += watch.DocumentClosed;
        bridge.Answered += watch.Answered;
        bridge.CloseAnswered += watch.CloseAnswered;
        view.EngineUnresponsive += () => watch.EngineReportedUnresponsive("WebKit");
        return watch;
    }

    /// <summary>The shutdown prompt, from any thread: over no window, since the window may be gone.</summary>
    public static IHostPrompt ShowShutdownPrompt(MainLoop ui, HostLog log, ConcurrentQueue<string>? answers, HostPromptRequest request) =>
        Prompt.Show(ui, log, () => 0, request, answers);

    public void Post(Action work, string name) => _ui.Post(work, name);

    public bool Ping(long seq) => _bridge.Ping(seq);

    public bool RequestClose(long seq, CloseReason reason, bool automated) => _bridge.RequestClose(seq, reason, automated);

    public void EndClose(long seq) => _bridge.EndClose(seq);

    public void ReloadHungPage() => _view.ReloadHungPage();

    public IHostPrompt ShowPrompt(HostPromptRequest request) => Prompt.Show(_ui, _log, _window, request, _answers);

    /// <summary>
    /// A <c>GtkAlertDialog</c> as the shared code sees it: its first button the default and the one Escape and closing
    /// choose, so dismissing it never reloads, closes or exits. In a Development build a scripted answer
    /// (<see cref="DevelopmentTriggers.PromptAnswersVariable"/>) may stand in for the person's.
    /// </summary>
    private sealed class Prompt : IHostPrompt
    {
        private readonly TaskCompletionSource<PromptAnswer> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly MainLoop _ui;
        private AlertDialog? _dialog;
        private bool _dismissed;

        private Prompt(MainLoop ui) => _ui = ui;

        public Task<PromptAnswer> Answer => _answer.Task;

        /// <summary>Shows the prompt on the loop (now when called there, posted otherwise) over <paramref name="parent"/>'s window.</summary>
        public static Prompt Show(MainLoop ui, HostLog log, Func<nint> parent, HostPromptRequest request, ConcurrentQueue<string>? answers)
        {
            var prompt = new Prompt(ui);
            if (ui.IsCurrent)
            {
                prompt.Open(log, parent(), request, answers);
            }
            else
            {
                ui.Post(() => prompt.Open(log, parent(), request, answers), "prompt");
            }

            return prompt;
        }

        public void Dismiss()
        {
            if (_ui.IsCurrent)
            {
                Close();
            }
            else
            {
                _ui.Post(Close, "prompt dismissed");
            }
        }

        private void Open(HostLog log, nint parent, HostPromptRequest request, ConcurrentQueue<string>? answers)
        {
            if (_dismissed)
            {
                return;
            }

            try
            {
                var dialog = AlertDialog.Show(parent, request.Instruction, request.Content, [request.First, request.Second], cancelButton: 0);
                _dialog = dialog;
                _ = AwaitAsync(dialog);
            }
            catch (Exception exception)
            {
                log.Error("The prompt could not be shown", exception);
                return;
            }

            if (SparDevelopment.IsSupported && answers is not null && DevelopmentTriggers.NextAnswer(answers, log, out var scripted, out var delay))
            {
                _ = AnswerLaterAsync(log, scripted, delay);
            }
        }

        private async Task AwaitAsync(AlertDialog dialog)
        {
            var button = await dialog.Answer;
            _answer.TrySetResult(button switch
            {
                0 => PromptAnswer.First,
                1 => PromptAnswer.Second,
                _ => PromptAnswer.Dismissed,
            });
        }

        /// <summary>A Development build's scripted answer: given after the delay, and the dialog taken down (loop thread).</summary>
        private async Task AnswerLaterAsync(HostLog log, PromptAnswer answer, int delay)
        {
            await Task.Delay(delay);
            if (_answer.Task.IsCompleted)
            {
                return;
            }

            log.Warn($"Development trigger: the prompt is answered with its {(answer == PromptAnswer.First ? "first" : "second")} button.");
            _answer.TrySetResult(answer);
            _dialog?.Dismiss();
        }

        private void Close()
        {
            _dismissed = true;
            _answer.TrySetResult(PromptAnswer.Dismissed);
            _dialog?.Dismiss();
        }
    }
}
