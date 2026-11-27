using Spar.AppWindows;
using Spar.Hosting;
using Windows.Win32;

namespace Spar.Windows;

/// <summary>
/// The app's windows of its kinds (decision "M3d's decide-first questions", items 3 and 4): opened on the UI thread
/// when a page asks, each an <see cref="AppWindow"/> of its own with its route, params and allow-list, in the app's
/// browser profile; a modal owned by its opener. The app's <see cref="WindowFamily{TWindow}"/> holds them with the main
/// window: closing the main window asks the others first, and its teardown takes those still open. The message loop
/// ends once every window is gone. The session end with several windows comes with M3d's later steps.
/// </summary>
internal sealed partial class WindowsApp
{
    /// <summary>The app's open windows, the main one first (UI thread only).</summary>
    private readonly WindowFamily<AppWindow> _family = new();

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
            var opened = new AppWindow(_app, _log, _automated, _title, _profile, _hangHook, owner, opened: window, windows: _windowSet)
            {
                Family = _family,
            };
            owner.Window = opened;
            if (DevServer.DevServerSettings.IsSupported && _devServer is not null)
            {
                opened.UseDevServer(_devServer);
            }

            _family.Add(opened);
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
        });

        await shown.Task.WaitAsync(cancellationToken);
    }

    /// <summary>Brings an open window of a single-instance kind to the front.</summary>
    void IAppWindowHost.Focus(OpenWindow window) => _ui?.Post(() =>
    {
        if (_family.Find(window.Id) is { IsClosing: false } open)
        {
            open.Focus();
        }
    });

    /// <summary>The window's own page closed it (<c>windows.closeWith</c>): its teardown, without its guards.</summary>
    void IAppWindowHost.Close(OpenWindow window) => _ui?.Post(() =>
    {
        if (_family.Find(window.Id) is { } open)
        {
            _ = open.TeardownAsync(0);
        }
    });

    /// <summary>The main window's teardown started: nothing opens any more; its teardown takes the others.</summary>
    private void CloseSecondaries() => _windowSet?.Closing();

    /// <summary>The loop ends once the main window and every other window are gone (UI thread).</summary>
    private void QuitWhenDone()
    {
        if (_mainDestroyed && _family.Others.Count == 0)
        {
            PInvoke.PostQuitMessage(0);
        }
    }

    /// <summary>A window of the app's kinds is gone: its bridge and logs stop off the UI thread.</summary>
    private void SecondaryDestroyed(AppWindow window, OpenWindow opened)
    {
        _family.Remove(window);
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
