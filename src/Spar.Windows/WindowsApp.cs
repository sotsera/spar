using Microsoft.Win32;
using Spar.Hosting;
using Spar.Runtime;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Spar.Windows;

/// <summary>
/// One run of the app on Windows: the window shown at once, the app's services built beside it, the WebView, and the
/// two-stage close (decision "Two-stage close"): every close of the window is a close request, the backend's guard
/// decides or asks, and only an approved request starts the teardown of plan section 3 ("Teardown").
/// </summary>
internal sealed class WindowsApp : IWindowMessages, IReportsErrors
{
    /// <summary>The exit code when the app's services could not be built, as in a test host.</summary>
    private const int StartFailedExitCode = 70;

    /// <summary>How long the page's close work may take (plan section 2c, "Flush before close").</summary>
    private const int PageCloseWorkMilliseconds = 1_000;

    /// <summary>How long teardown waits for cancelled commands to end before closing the WebView anyway.</summary>
    private const int CommandSettleMilliseconds = 3_000;

    private readonly SparApp _app;
    private readonly HostLog _log = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly TaskCompletionSource<WebViewBridge> _bridge = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly bool _automated = SparDevelopment.IsAutomatedFromEnvironment();
    private readonly string _title;
    private SparRunningApp? _running;
    private CloseGuard? _guard;
    private UiThread? _ui;
    private BrowserView? _view;
    private HWND _window;
    private State _state;
    private bool _closeRequestOpen;
    private int _exitCode;

    public WindowsApp(SparApp app)
    {
        _app = app;
        _title = app.Catalog.AppAssembly?.GetName().Name ?? "Spar";
    }

    private enum State
    {
        Starting,
        Running,
        Closing,
    }

    public int Run()
    {
        _log.Info($"Host started {HostLog.ProcessAgeMilliseconds():0.0} ms after the process.");
        var folders = SparFolders.ForApp(_title, _app.Builder.FolderOptions);
        var built = Task.Run(() => Build(folders));

        UiThread.Run(Background(), Report, ui => Start(ui, folders, built));

        Stop();
        _log.Step($"exit {_exitCode}");
        return _exitCode;
    }

    public void Report(Exception exception) => _log.Error("The window's thread failed", exception);

    public LRESULT? OnMessage(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        switch (message)
        {
            case PInvoke.WM_SIZE:
                _view?.Resize();
                return default(LRESULT);
            case PInvoke.WM_CLOSE:
                // Every close goes through a close request; the window is destroyed only by the teardown.
                OnCloseMessage();
                return default(LRESULT);
            case WindowsMessages.QueryEndSession:
                // Spar never blocks Windows from logging off (decision "Two-stage close").
                return new LRESULT(1);
            case PInvoke.WM_DESTROY:
                PInvoke.PostQuitMessage(0);
                return default(LRESULT);
            default:
                return null;
        }
    }

    /// <summary>Builds the app's services on the thread pool while the window and the WebView start.</summary>
    private SparRunningApp Build(SparFolders folders)
    {
        var builder = _app.Builder;
        SparRunningApp.AddSparServices(builder.Services, _app.Catalog, folders, builder.BridgeOptions.Freeze());
        var running = SparRunningApp.Build(builder.Services, _app.Catalog, folders);
        _log.UseAppLogging(running.Services);
        _log.Phase("services built");
        return running;
    }

    /// <summary>On the UI thread: the window first, shown at once, then everything that takes time, without blocking it.</summary>
    private void Start(UiThread ui, SparFolders folders, Task<SparRunningApp> built)
    {
        _ui = ui;
        _window = ui.CreateWindow(_title, WINDOW_STYLE.WS_OVERLAPPEDWINDOW, this);
        PInvoke.ShowWindow(_window, SHOW_WINDOW_CMD.SW_SHOWDEFAULT);
        _log.Phase("window shown");

        var frontend = _app.Catalog.AppAssembly is { } assembly ? SparFrontend.Open(assembly) : null;
        var pages = new PageServer(frontend, ui, _log);
        _view = new BrowserView(ui, _log, _window, folders.WebEngine, pages, _bridge.Task, Fail);
        _ = _view.StartAsync();
        _ = ConnectAsync(ui, built);
    }

    /// <summary>Once the services exist: the close guard, and the bridge the WebView waits for before it navigates.</summary>
    private async Task ConnectAsync(UiThread ui, Task<SparRunningApp> built)
    {
        try
        {
            _running = await built;
        }
        catch (Exception exception)
        {
            _log.Error("The app could not start", exception);
            Fail(StartFailedExitCode, "The app could not start: " + exception.Message);
            return;
        }

        _guard = new CloseGuard(_app.Builder.CloseGuard, _running.Services, _log.Logger);
        var bridge = new WebViewBridge(ui, _log, _running.Dispatcher, _running.Catalog.Fingerprint, SparDevelopment.IsSupported);
        _bridge.TrySetResult(bridge);
        if (_state == State.Starting)
        {
            _state = State.Running;
        }
    }

    private void OnCloseMessage()
    {
        if (_state == State.Closing || _closeRequestOpen)
        {
            // A second click joins the open request; during teardown there is nothing left to ask.
            return;
        }

        _ = CloseRequestedAsync(CloseReason.User);
    }

    /// <summary>
    /// The two-stage close: the page's guard, then the backend's, each closing, keeping or asking. Only an approved
    /// request starts the teardown.
    /// </summary>
    private async Task CloseRequestedAsync(CloseReason reason)
    {
        _closeRequestOpen = true;
        try
        {
            var request = new CloseRequest("main", reason, _automated);
            _log.Step($"close requested ({reason}{(_automated ? ", automated" : "")})");

            // The page's guard (windows.onCloseRequested) is asked here, first, when it is built: its "keep" ends the
            // request before the backend is asked, and a page silent for about 5 s gets "Wait / Close anyway".
            if (!await AskPageAsync(request))
            {
                return;
            }

            var verdict = _guard is null ? CloseVerdict.Close : await _guard.DecideAsync(request, _stopping.Token);
            if (verdict.Kind == CloseVerdictKind.Keep)
            {
                _log.Step("close kept by the backend's guard");
                return;
            }

            if (verdict.Kind == CloseVerdictKind.Ask && !NativeDialog.ConfirmClose(_window, _title, verdict.Question!))
            {
                _log.Step("close kept by the user");
                return;
            }

            await TeardownAsync(0);
        }
        catch (Exception exception)
        {
            _log.Error("The close request failed", exception);
        }
        finally
        {
            _closeRequestOpen = false;
        }
    }

    /// <summary>The seam for the page's close guard; until it is built every page agrees.</summary>
    private static Task<bool> AskPageAsync(CloseRequest request)
    {
        _ = request;
        return Task.FromResult(true);
    }

    /// <summary>
    /// Plan section 3, "Teardown": mark closing, let the page finish its close work, stop admitting calls and let the
    /// cancelled ones settle (bounded, still pumping), close the controller on this thread, and destroy the window.
    /// The process then exits without waiting for the browser.
    /// </summary>
    private async Task TeardownAsync(int exitCode)
    {
        if (_state == State.Closing)
        {
            return;
        }

        _state = State.Closing;
        _exitCode = exitCode;
        _log.Step("closing");

        if (_bridge.Task.IsCompletedSuccessfully)
        {
            var bridge = _bridge.Task.Result;
            await bridge.RunPageCloseWorkAsync(TimeSpan.FromMilliseconds(PageCloseWorkMilliseconds));
            var settled = bridge.CloseAsync("window closing");
            _log.Step("calls stopped");
            var first = await Task.WhenAny(settled, Task.Delay(CommandSettleMilliseconds));
            _log.Step(first == settled ? "calls settled" : "calls still running at the limit");
        }

        _stopping.Cancel();
        _view?.Close();
        _log.Step("controller closed");
        PInvoke.DestroyWindow(_window);
        _log.Step("window destroyed");
    }

    /// <summary>An unrecoverable failure: a message owned by the window, then the teardown with its exit code.</summary>
    private void Fail(int exitCode, string message) => _ui?.Post(() =>
    {
        if (_state == State.Closing)
        {
            return;
        }

        _log.Error($"Exiting with code {exitCode}: {message}");
        NativeDialog.ShowError(_window, _title, message);
        _ = TeardownAsync(exitCode);
    });

    /// <summary>After the window's thread has ended: the bridge's reader, then the app's services.</summary>
    private void Stop()
    {
        try
        {
            if (_bridge.Task.IsCompletedSuccessfully)
            {
                // This is the process's main thread; the window's thread has already ended, so waiting blocks nothing.
                _bridge.Task.Result.StopAsync().GetAwaiter().GetResult();
            }

            _running?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _log.Step("services disposed");
        }
        catch (Exception exception)
        {
            _log.Error("The app's services did not stop cleanly", exception);
        }
        finally
        {
            _stopping.Dispose();
        }
    }

    /// <summary>The window's colour before the page paints: the system's app theme, dark or light (plan 3d's first layer).</summary>
    private static uint Background()
    {
        var light = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
        return light is 0 ? 0x001F1F1Fu : 0x00FFFFFFu;
    }

    /// <summary>Messages CsWin32 is not asked to generate.</summary>
    private static class WindowsMessages
    {
        public const uint QueryEndSession = 0x0011;
    }
}

/// <summary>
/// The Windows host as Spar's generated code creates it (<see cref="ISparHost"/>): one window, its WebView and the
/// app's backend. Apps never reference it; Spar.Sdk adds it for <c>SparPlatforms</c> that include windows.
/// </summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed class SparWindowsHost : ISparHost
{
    public int Run(SparApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return new WindowsApp(app).Run();
    }
}
