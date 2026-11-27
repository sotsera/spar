using Spar.AppWindows;
using Spar.Hosting;

namespace Spar.Linux;

/// <summary>
/// The app's windows of its kinds (decision "M3d's decide-first questions", items 3 and 4): opened on the main loop
/// when a page asks, each an <see cref="AppWindow"/> of its own with its route, params and allow-list, on the app's
/// network session with a web context of its own (its <c>spar</c> scheme serves that window); a modal in its opener's
/// window group, transient for it. The app's <see cref="WindowFamily{TWindow}"/> holds them with the main window:
/// closing the main window asks the others first, and its teardown takes those still open. The shutdown runs once every
/// window is gone. The session end with several windows comes with M3d's later steps.
/// </summary>
internal sealed partial class LinuxApp
{
    /// <summary>The app's open windows, the main one first (loop thread only).</summary>
    private readonly WindowFamily<AppWindow> _family = new();

    /// <summary>Every window of the app's kinds the run made, whose handles go once the loop has ended.</summary>
    private readonly List<AppWindow> _madeSecondaries = [];

    /// <summary>Whether the main window is gone (loop thread only).</summary>
    private bool _mainClosed;

    /// <summary>Opens <paramref name="window"/> on the main loop; completes once it is presented.</summary>
    async Task IAppWindowHost.OpenAsync(OpenWindow window, CancellationToken cancellationToken)
    {
        if (_ui is not { } ui || _built is not { } built || _security is not { } security)
        {
            throw new InvalidOperationException("The app's window is not open yet.");
        }

        // Off the loop: the saved colour scheme is read from the data root.
        var theme = WindowTheme.Read(_app.Builder.MainWindow, _folders.DataRoot);
        var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ui.Post(
            () =>
            {
                if (_window is null || _window.IsClosing || _window.Session == 0)
                {
                    shown.TrySetException(new InvalidOperationException("The app is closing."));
                    return;
                }

                var owner = new SecondaryOwner(this, window);
                var opened = new AppWindow(
                    _app, security, _log, _automated, _title, _folders, _profile, _hangHook, _promptAnswers, owner,
                    opened: window, windows: _windowSet, sharedSession: _window.Session)
                {
                    Family = _family,
                };
                owner.Window = opened;
                if (DevServer.DevServerSettings.IsSupported && _devServer is not null)
                {
                    opened.UseDevServer(_devServer);
                }

                _family.Add(opened);
                _madeSecondaries.Add(opened);
                try
                {
                    opened.Start(ui, theme, built);
                    _log.Step($"window {window.Id} opened at {window.Options.PageRoute}{(window.IsModal ? $", modal to {window.OpenerId}" : "")}");
                    shown.TrySetResult();
                }
                catch (Exception error)
                {
                    _family.Remove(opened);
                    opened.AbandonStart();
                    _log.Error($"Window {window.Id} could not be opened", error);
                    shown.TrySetException(error);
                }
            },
            "open window");

        await shown.Task.WaitAsync(cancellationToken);
    }

    /// <summary>Presents an open window of a single-instance kind.</summary>
    void IAppWindowHost.Focus(OpenWindow window) => _ui?.Post(
        () => _family.Find(window.Id)?.Focus(),
        "focus window");

    /// <summary>The window's own page closed it (<c>windows.closeWith</c>): its teardown, without its guards.</summary>
    void IAppWindowHost.Close(OpenWindow window) => _ui?.Post(
        () => _ = _family.Find(window.Id)?.TeardownAsync(0),
        "close window");

    /// <summary>The main window's teardown started: nothing opens any more; its teardown takes the others.</summary>
    private void CloseSecondaries() => _windowSet?.Closing();

    /// <summary>The shutdown runs once the main window and every other window are gone (loop thread).</summary>
    private void FinishWhenDone()
    {
        if (_mainClosed && _family.Others.Count == 0)
        {
            _ = FinishAsync(_ui!);
        }
    }

    /// <summary>A window of the app's kinds is gone: its bridge stops off the loop.</summary>
    private void SecondaryClosed(AppWindow window, OpenWindow opened)
    {
        _family.Remove(window);
        _windowSet?.Closed(opened.Id);
        _log.Step($"window {opened.Id} closed");
        _ = Task.Run(window.StopAsync);
        FinishWhenDone();
    }

    /// <summary>The handles the app's other windows' native callbacks found them by, once the loop has ended.</summary>
    private void ReleaseSecondaries()
    {
        foreach (var window in _madeSecondaries)
        {
            window.Release();
        }
    }

    /// <summary>
    /// A window of one of the app's kinds: its teardown neither stops the app nor its relaunches, it waits for no
    /// process of its own (WebKit gives a view's no id; the main window's wait at the end covers them all), and its end
    /// removes it from the app's windows.
    /// </summary>
    private sealed class SecondaryOwner(LinuxApp app, OpenWindow opened) : IAppWindowOwner
    {
        public AppWindow? Window { get; set; }

        public void TeardownStarted()
        {
        }

        public void WindowClosed()
        {
            if (Window is { } window)
            {
                app.SecondaryClosed(window, opened);
            }
        }

        public List<(int Id, string Name)> WebKitProcesses() => [];
    }
}
