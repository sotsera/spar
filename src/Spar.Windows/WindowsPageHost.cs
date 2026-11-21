using Spar.Hosting;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Spar.Windows;

/// <summary>
/// The Windows side of the shared hang handling (<see cref="PageWatch"/>, <see cref="ShutdownWatch"/>): the main
/// window's bridge, view and UI thread for the heartbeat, and the native prompts as task dialogs on threads of their
/// own (<see cref="PromptDialog"/>), owned by the window or, for the shutdown prompt, by nothing.
/// </summary>
internal sealed class WindowsPageHost : IPageWatchHost
{
    private readonly UiThread _ui;
    private readonly HostLog _log;
    private readonly HWND _window;
    private readonly string _title;
    private readonly WebViewBridge _bridge;
    private readonly BrowserView _view;

    public bool IsPageVisible => PInvoke.IsWindowVisible(_window) && !PInvoke.IsIconic(_window) && _view.IsShown;

    private WindowsPageHost(UiThread ui, HostLog log, HWND window, string title, WebViewBridge bridge, BrowserView view)
    {
        _ui = ui;
        _log = log;
        _window = window;
        _title = title;
        _bridge = bridge;
        _view = view;
    }

    /// <summary>The main window's heartbeat, following <paramref name="bridge"/>'s documents and <paramref name="view"/>'s reports (UI thread).</summary>
    public static PageWatch Watch(
        UiThread ui,
        HostLog log,
        HWND window,
        string title,
        WebViewBridge bridge,
        BrowserView view,
        PageHangHook hook,
        bool automated,
        int interval)
    {
        var watch = new PageWatch(new WindowsPageHost(ui, log, window, title, bridge, view), log, title, hook, automated, interval);
        bridge.DocumentOpened += watch.DocumentOpened;
        bridge.DocumentClosed += watch.DocumentClosed;
        bridge.Answered += watch.Answered;
        bridge.CloseAnswered += watch.CloseAnswered;
        view.RendererUnresponsive += () => watch.EngineReportedUnresponsive("WebView2");
        return watch;
    }

    /// <summary>The shutdown prompt: owned by no window, since the window may be gone, and brought to the front.</summary>
    public static IHostPrompt ShowShutdownPrompt(string title, HostLog log, HostPromptRequest request) =>
        Prompt.Show(HWND.Null, title, request, takeForeground: true, exception => log.Error("The shutdown prompt failed", exception));

    public void Post(Action work, string name) => _ui.Post(work, name);

    public bool Ping(long seq) => _bridge.Ping(seq);

    public bool RequestClose(long seq, CloseReason reason, bool automated) => _bridge.RequestClose(seq, reason, automated);

    public void EndClose(long seq) => _bridge.EndClose(seq);

    public void ReloadHungPage() => _view.ReloadHungPage();

    public IHostPrompt ShowPrompt(HostPromptRequest request) =>
        Prompt.Show(_window, _title, request, takeForeground: false, exception => _log.Error("The hung-page dialog failed", exception));

    /// <summary>A <see cref="PromptDialog"/> as the shared code sees it.</summary>
    private sealed class Prompt : IHostPrompt
    {
        // The task dialog's button ids, stable per prompt since M1b: automation presses them (TDM_CLICK_BUTTON in
        // docs/evidence/m1b-hangs), so each button keeps its own id: 200 Wait, 201 Reload page, 202 Close anyway,
        // 300 Keep waiting, 301 Exit now.
        private const int WaitId = 200;
        private const int ReloadId = 201;
        private const int CloseAnywayId = 202;
        private const int KeepWaitingId = 300;
        private const int ExitNowId = 301;

        private readonly PromptDialog _dialog;

        public Task<PromptAnswer> Answer { get; }

        private Prompt(PromptDialog dialog, int firstId, int secondId)
        {
            _dialog = dialog;
            Answer = AnswerAsync(dialog.Result, firstId, secondId);
        }

        public static Prompt Show(HWND owner, string title, HostPromptRequest request, bool takeForeground, Action<Exception> report)
        {
            var (firstId, secondId) = request.Kind switch
            {
                HostPromptKind.HungPage => (WaitId, ReloadId),
                HostPromptKind.HungPageClose => (WaitId, CloseAnywayId),
                HostPromptKind.Shutdown => (KeepWaitingId, ExitNowId),
                _ => throw new ArgumentOutOfRangeException(nameof(request), request.Kind, "Not a prompt kind."),
            };
            var dialog = PromptDialog.Show(
                owner,
                title,
                request.Instruction,
                request.Content,
                new PromptButton(firstId, request.First),
                new PromptButton(secondId, request.Second),
                takeForeground,
                report);
            return new Prompt(dialog, firstId, secondId);
        }

        public void Dismiss() => _dialog.Dismiss();

        private static async Task<PromptAnswer> AnswerAsync(Task<int> result, int firstId, int secondId)
        {
            var pressed = await result.ConfigureAwait(false);
            return pressed == firstId ? PromptAnswer.First : pressed == secondId ? PromptAnswer.Second : PromptAnswer.Dismissed;
        }
    }
}
