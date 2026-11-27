using Spar.AppWindows;
using Spar.Hosting;
using Windows.Win32;

namespace Spar.Windows;

/// <summary>
/// The app's windows of its kinds (decision "M3d's decide-first questions", items 3 and 4): opened on the UI thread
/// when a page asks, each an <see cref="AppWindow"/> of its own with its route, params and allow-list, in the app's
/// browser profile. When the main window's teardown starts, the others are torn down with it, and the message loop ends
/// once every window is gone. Their close requests, the close order through each window's guard and the session end
/// come with M3d's later steps.
/// </summary>
internal sealed partial class WindowsApp
{
    /// <summary>The windows of the app's kinds that are open (UI thread only).</summary>
    private readonly List<AppWindow> _secondaries = [];

    /// <summary>Whether the main window is gone (UI thread only).</summary>
    private bool _mainDestroyed;

    /// <summary>Opens <paramref name="window"/> on the UI thread; completes once it is shown.</summary>
    async Task IAppWindowHost.OpenAsync(OpenWindow window, CancellationToken cancellationToken)
    {
        if (_ui is not { } ui || _built is not { } built)
        {
            throw new InvalidOperationException("The app's window is not open yet.");
        }

        // Off the UI thread: the saved colour scheme is read from the data root.
        var theme = WindowTheme.Read(_app.Builder.MainWindow, _folders.DataRoot);
        var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ui.Post(() =>
        {
            if (_window is null || _window.IsClosing)
            {
                shown.TrySetException(new InvalidOperationException("The app is closing."));
                return;
            }

            var owner = new SecondaryOwner(this, window);
            var opened = new AppWindow(_app, _log, _automated, _title, _profile, _hangHook, owner, opened: window, windows: _windowSet);
            owner.Window = opened;
            if (DevServer.DevServerSettings.IsSupported && _devServer is not null)
            {
                opened.UseDevServer(_devServer);
            }

            _secondaries.Add(opened);
            try
            {
                opened.Start(ui, theme, built);
                _log.Step($"window {window.Id} opened at {window.Options.PageRoute}");
                shown.TrySetResult();
            }
            catch (Exception error)
            {
                _secondaries.Remove(opened);
                _log.Error($"Window {window.Id} could not be opened", error);
                shown.TrySetException(error);
            }
        });

        await shown.Task.WaitAsync(cancellationToken);
    }

    /// <summary>Brings an open window of a single-instance kind to the front.</summary>
    void IAppWindowHost.Focus(OpenWindow window) => _ui?.Post(() =>
    {
        if (_secondaries.FirstOrDefault(open => open.Id == window.Id) is { IsClosing: false } open)
        {
            open.Focus();
        }
    });

    /// <summary>The main window's teardown started: nothing opens any more, and every other window is torn down.</summary>
    private void CloseSecondaries()
    {
        _windowSet?.Closing();
        foreach (var window in _secondaries.ToList())
        {
            _ = window.TeardownAsync(0);
        }
    }

    /// <summary>The loop ends once the main window and every other window are gone (UI thread).</summary>
    private void QuitWhenDone()
    {
        if (_mainDestroyed && _secondaries.Count == 0)
        {
            PInvoke.PostQuitMessage(0);
        }
    }

    /// <summary>A window of the app's kinds is gone: its bridge and logs stop off the UI thread.</summary>
    private void SecondaryDestroyed(AppWindow window, OpenWindow opened)
    {
        _secondaries.Remove(window);
        _windowSet?.Closed(opened.Id);
        _log.Step($"window {opened.Id} closed");
        _ = Task.Run(window.StopAsync);
        QuitWhenDone();
    }

    /// <summary>
    /// A window of one of the app's kinds: its teardown neither stops the app nor its relaunches, and its end removes it
    /// from the app's windows.
    /// </summary>
    private sealed class SecondaryOwner(WindowsApp app, OpenWindow opened) : IAppWindowOwner
    {
        public AppWindow? Window { get; set; }

        public void TeardownStarted()
        {
        }

        public void WindowDestroyed()
        {
            if (Window is { } window)
            {
                app.SecondaryDestroyed(window, opened);
            }
        }

        public Task? SessionEnded() => null;

        public void SessionEndLogged(TimeSpan limit)
        {
        }
    }
}
