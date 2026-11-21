using System.Diagnostics;

namespace Spar.Testing;

/// <summary>
/// A launched app (<see cref="SparBlackBox.LaunchAsync"/>): its process, its data root and the folders under it, its
/// captured output, its window and native dialogs through UI Automation, and its exit. Disposal ends what is still
/// running (after <see cref="SparBlackBoxOptions.ExitTimeout"/> when an exit was already asked for, at once otherwise),
/// waits until every process of the app is gone, the browser's included, and deletes the data root it owns. A cleanup
/// that cannot finish is a failure, reported beside the test's own (decision "Fixture rules: the teardown hook").
/// </summary>
public sealed class SparBlackBoxApp : IAsyncDisposable
{
    /// <summary>How long disposal waits for the whole process tree once it has ended the app.</summary>
    private static readonly TimeSpan TreeLimit = TimeSpan.FromSeconds(10);

    /// <summary>How long disposal keeps trying to delete the data root once every process of the app is gone.</summary>
    private static readonly TimeSpan RootLimit = TimeSpan.FromSeconds(5);

    private readonly SparBlackBoxOptions _options;
    private readonly BlackBoxFolders _folders;
    private readonly bool _ownsRoot;
    private readonly IBlackBoxProcess _process;
    private readonly IBlackBoxAutomation _automation;
    private readonly Stopwatch _clock;
    private SparBlackBoxWindow? _window;
    private bool _exitRequested;
    private int _disposed;

    public SparProject Project { get; }

    public int ProcessId => _process.ProcessId;

    /// <summary>The disposable root every folder of this launch is under; pass it to a second launch to share the data.</summary>
    public string DataRoot => _folders.Root;

    /// <summary>What the app sees as <c>%LOCALAPPDATA%</c> (on Linux, <c>XDG_DATA_HOME</c>, which .NET's <c>LocalApplicationData</c> is).</summary>
    public string LocalAppData => _folders.LocalAppData;

    /// <summary>What the app sees as <c>%APPDATA%</c> (on Linux, <c>XDG_CONFIG_HOME</c>, which .NET's <c>ApplicationData</c> is).</summary>
    public string RoamingAppData => _folders.RoamingAppData;

    /// <summary>What the app sees as <c>%TEMP%</c> and <c>%TMP%</c> (on Linux, <c>TMPDIR</c>).</summary>
    public string TempFolder => _folders.Temp;

    /// <summary>The app's per-user folder, <c>%LOCALAPPDATA%\&lt;app id&gt;</c> (Linux: <c>$XDG_DATA_HOME/&lt;app id&gt;</c>), as a Production build names it.</summary>
    public string AppFolder => Path.Combine(_folders.LocalAppData, Project.AppId);

    /// <summary>The app's data folder (unless its <c>Program.cs</c> moved it).</summary>
    public string DataFolder => Path.Combine(AppFolder, "data");

    /// <summary>The app's logs folder (unless its <c>Program.cs</c> moved it).</summary>
    public string LogsFolder => Path.Combine(AppFolder, "logs");

    /// <summary>The app's log file, <c>app.log</c> in <see cref="LogsFolder"/>.</summary>
    public string LogFile => Path.Combine(LogsFolder, "app.log");

    /// <summary>What the app wrote to standard output so far.</summary>
    public string StandardOutput => _process.StandardOutput;

    /// <summary>What the app wrote to standard error so far: the host's lines, start-up problems included.</summary>
    public string StandardError => _process.StandardError;

    /// <summary>How long the launch took until the app was ready (or until it started, without the wait).</summary>
    public TimeSpan StartedAfter { get; private set; }

    /// <summary>The app's main window through the accessibility API (UI Automation on Windows, AT-SPI on Linux).</summary>
    public SparBlackBoxWindow Window => _window ??= new SparBlackBoxWindow(this);

    public bool HasExited => _process.Exited.IsCompleted;

    /// <summary>The exit code, once the process has exited; null while it runs.</summary>
    public int? ExitCode => HasExited ? _process.Exited.Result : null;

    /// <summary>The ids of the app's processes still running: the app and everything it started, the browser's included.</summary>
    public IReadOnlyList<int> ProcessIds => _process.ProcessIds;

    internal IBlackBoxAutomation Automation => _automation;

    /// <summary>The X display the app shows on (Linux): the test process's private Xvfb, for Spar's own tests that read it.</summary>
    internal string Display => _options.Display ?? _folders.Display;

    internal IBlackBoxProcess Process => _process;

    internal SparBlackBoxApp(
        SparProject project,
        SparBlackBoxOptions options,
        BlackBoxFolders folders,
        bool ownsRoot,
        IBlackBoxProcess process,
        IBlackBoxAutomation automation,
        Stopwatch clock)
    {
        Project = project;
        _options = options;
        _folders = folders;
        _ownsRoot = ownsRoot;
        _process = process;
        _automation = automation;
        _clock = clock;
    }

    /// <summary>
    /// Waits for a native message of the app (a task dialog or a message box on Windows, a <c>GtkAlertDialog</c> on Linux) and reads it. With no timeout it waits as
    /// long as <see cref="SparBlackBoxOptions.ReadyTimeout"/>; it fails at once when the app exits without one.
    /// </summary>
    public async Task<SparNativeDialog> WaitForDialogAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var limit = timeout ?? _options.ReadyTimeout;
        var started = Stopwatch.StartNew();
        while (true)
        {
            if (await _automation.FindDialogAsync(ProcessId, cancellationToken) is { } dialog)
            {
                return new SparNativeDialog(this, dialog);
            }

            if (HasExited)
            {
                throw new InvalidOperationException($"The app exited with {ExitCode} without showing a dialog.{Output()}");
            }

            if (started.Elapsed >= limit)
            {
                throw new TimeoutException($"The app showed no dialog within {limit.TotalSeconds:0.#} s.{Output()}");
            }

            await Task.Delay(50, cancellationToken);
        }
    }

    /// <summary>
    /// Closes the main window as its close button does (UI Automation's Window pattern sends the window its close
    /// request; on Linux the window's AT-SPI action <c>window.close</c>, GTK's <c>close-request</c>), so the page's and the backend's close guards run, and returns the exit code once the app has exited.
    /// A guard's question shows as a native dialog, which <see cref="WaitForDialogAsync"/> reads and answers meanwhile.
    /// </summary>
    public async Task<int> CloseAsync(CancellationToken cancellationToken = default)
    {
        _exitRequested = true;
        if (!await _automation.CloseWindowAsync(ProcessId, cancellationToken))
        {
            throw new InvalidOperationException($"The app has no window to close (it exited with {ExitCode?.ToString() ?? "nothing yet"}).");
        }

        return await WaitForExitAsync(cancellationToken);
    }

    /// <summary>Waits until the app exits on its own, at most <see cref="SparBlackBoxOptions.ExitTimeout"/>, and returns its exit code.</summary>
    public async Task<int> WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        _exitRequested = true;
        try
        {
            return await _process.Exited.WaitAsync(_options.ExitTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"The app did not exit within {_options.ExitTimeout.TotalSeconds:0.#} s.{Output()}");
        }
    }

    /// <summary>Ends the app and every process it started at once (the job object; <c>SIGKILL</c> on Linux); its exit code is then 1 (Linux: 137).</summary>
    public void Kill() => _process.Kill();

    /// <summary>
    /// Waits until no process of the app is left, the browser's included, at most <paramref name="timeout"/> (10 s by
    /// default); true when none is left. After a clean exit the browser's processes follow the app on their own.
    /// </summary>
    public Task<bool> WaitForProcessTreeExitAsync(TimeSpan? timeout = null) => _process.WaitForTreeAsync(timeout ?? TreeLimit);

    /// <summary>
    /// Ends what is still running, waits for every process of the app, and deletes the data root it owns. Every step
    /// runs even when an earlier one fails; a failure is kept for the test framework's hook when the test project has
    /// one (<see cref="SparTestCleanup"/>), so it is reported beside the test's own, and thrown otherwise.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var failures = new List<Exception>();
        await StopAsync(failures);
        _process.Dispose();
        (_automation as IDisposable)?.Dispose();
        if (_ownsRoot)
        {
            _folders.DeleteRuntime();
        }
        if (_ownsRoot && !_options.KeepData)
        {
            try
            {
                await TestDataRoots.DeleteAsync(DataRoot, RootLimit);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failures.Add(new InvalidOperationException($"The data root {DataRoot} could not be deleted: {exception.Message}", exception));
            }
        }

        if (failures.Count == 0)
        {
            return;
        }

        var failure = failures.Count == 1 ? failures[0] : new AggregateException("The launched app did not stop cleanly.", failures);
        if (!SparTestCleanup.TryKeep(failure))
        {
            throw failure;
        }
    }

    /// <summary>The ready signal: the host's <c>ui-ready</c> line in the app's log, then its window through the accessibility API.</summary>
    internal async Task WaitForReadyAsync(TimeSpan limit, CancellationToken cancellationToken)
    {
        var log = new LogWatch(LogFile);
        while (true)
        {
            if (log.Poll(ReadyLine.IsReady) && await HasWindowAsync(cancellationToken))
            {
                StartedAfter = _clock.Elapsed;
                return;
            }

            if (HasExited)
            {
                throw new InvalidOperationException($"{Project.Name} exited with {ExitCode} before it was ready.{Output()}");
            }

            if (_clock.Elapsed >= limit)
            {
                var stage = log.Seen ? "the accessibility API found no window" : $"its log ({LogFile}) has no ui-ready line";
                throw new TimeoutException($"{Project.Name} was not ready within {limit.TotalSeconds:0.#} s: {stage}.{Output()}");
            }

            await Task.Delay(25, cancellationToken);
        }
    }

    private async Task<bool> HasWindowAsync(CancellationToken cancellationToken) =>
        await _automation.TitleAsync(ProcessId, cancellationToken) is not null;

    private async Task StopAsync(List<Exception> failures)
    {
        if (!HasExited && _exitRequested)
        {
            try
            {
                await _process.Exited.WaitAsync(_options.ExitTimeout);
            }
            catch (TimeoutException)
            {
                var limit = _options.ExitTimeout.TotalSeconds;
                failures.Add(new TimeoutException($"The app did not exit within {limit:0.#} s of being asked to; it was ended."));
            }
        }

        if (!HasExited)
        {
            // Never asked to exit, or too slow to: the app and everything it started end now.
            _process.Kill();
        }
        else if (!await _process.WaitForTreeAsync(TreeLimit))
        {
            // The app exited, but processes it started did not follow it: a leak, reported, then ended.
            failures.Add(new InvalidOperationException(
                $"Processes of the app were still running {TreeLimit.TotalSeconds:0} s after it exited: {string.Join(", ", _process.ProcessIds)}."));
            _process.Kill();
        }

        if (!await _process.WaitForTreeAsync(TreeLimit))
        {
            failures.Add(new InvalidOperationException(
                $"Processes of the app were still running {TreeLimit.TotalSeconds:0} s after they were ended: " +
                $"{string.Join(", ", _process.ProcessIds)}."));
        }

        await _process.WaitForOutputAsync(TimeSpan.FromSeconds(2));
    }

    private string Output()
    {
        var error = StandardError.Trim();
        return error.Length == 0 ? "" : $"{System.Environment.NewLine}Standard error:{System.Environment.NewLine}{error}";
    }
}
