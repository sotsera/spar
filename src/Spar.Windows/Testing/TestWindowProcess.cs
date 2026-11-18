using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Spar.Hosting;
using Spar.Windows.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Spar.Windows.Testing;

/// <summary>
/// What every app window of a test process shares (plan section 5, "Rules for Spar's own code"; decisions "Window-test
/// profiles, trusted input, the page's pickers and the Sandbox runner" and "Step 8's shape: window tests driven by
/// Spar's own API"):
/// <list type="bullet">
/// <item>one UI thread, which owns every window and WebView2 object of the process;</item>
/// <item>the prerequisite checks, run once before the first window, so a missing Runtime fails the tests with a message
/// instead of a native abort;</item>
/// <item>one WebView2 environment per set of browser arguments, in a root of this process's own under the test
/// profiles folder, each with one default-profile anchor controller kept for the process, so a test's controller starts
/// on a live browser and the browser's exit never delays the next test;</item>
/// <item>the InPrivate profile names of the windows open at the same time, each distinct, since InPrivate windows of
/// one name share their storage.</item>
/// </list>
/// A later run deletes a leftover root only when no browser holds it (its <c>EBWebView\lockfile</c> is gone), and
/// otherwise leaves it. At process exit the anchors close and, once the browsers have let go, this process's root is
/// deleted. Created only by the test host, behind the test-host feature switch, so an app's executable has none of it.
/// </summary>
internal sealed class TestWindowProcess
{
    /// <summary>The anchor's window, a small offscreen one that is never shown on any monitor.</summary>
    private const int AnchorSize = 64;

    /// <summary>How long process exit waits for the browsers to let go of this process's root before leaving it behind.</summary>
    private static readonly TimeSpan ExitWait = TimeSpan.FromSeconds(3);

    private static readonly Lock Gate = new();
    private static TestWindowProcess? s_shared;

    private readonly Dictionary<string, Task<SharedBrowser>> _browsers = new(StringComparer.Ordinal);
    private readonly HashSet<int> _profiles = [];
    private readonly string _root;

    private TestWindowProcess(string root, UiThread ui, HostLog log)
    {
        _root = root;
        Ui = ui;
        Log = log;
    }

    /// <summary>The test process's one UI thread.</summary>
    public UiThread Ui { get; }

    /// <summary>The process's own lines (the shared browsers); each window logs to its app's log.</summary>
    public HostLog Log { get; }

    /// <summary>The process's shared part, created by the first window; it throws when the prerequisites are not met.</summary>
    public static TestWindowProcess Get(string profilesFolder)
    {
        lock (Gate)
        {
            return s_shared ??= Create(profilesFolder);
        }
    }

    /// <summary>A profile name no open window of this process uses, until <see cref="ReturnProfile"/>.</summary>
    public string RentProfile()
    {
        lock (Gate)
        {
            var index = 1;
            while (!_profiles.Add(index))
            {
                index++;
            }

            return "spartest" + index.ToString(CultureInfo.InvariantCulture);
        }
    }

    public void ReturnProfile(string name)
    {
        lock (Gate)
        {
            _profiles.Remove(int.Parse(name.AsSpan("spartest".Length), CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// The shared environment for <paramref name="arguments"/>, created with its anchor on first use (UI thread). The
    /// pointer carries one reference that the caller owns.
    /// </summary>
    public async Task<nint> EnvironmentAsync(string? arguments)
    {
        var key = arguments ?? "";
        if (!_browsers.TryGetValue(key, out var browser))
        {
            browser = CreateBrowserAsync(_browsers.Count, arguments);
            _browsers[key] = browser;
        }

        var shared = await browser;
        Marshal.AddRef(shared.Environment);
        return shared.Environment;
    }

    private static TestWindowProcess Create(string profilesFolder)
    {
        if (Prerequisites.CheckWindows() is { } windows)
        {
            throw new InvalidOperationException("Window tests cannot run here: " + StartupReport.Describe(windows));
        }

        var log = new HostLog(standardError: false);

        // As an app does before the loader's first call: WebView2's variables of the developer's environment would
        // otherwise reach the tests' browser (a debugging port among them). The test process's block is the browser's.
        BrowserEnvironment.Filter(allowed: false, log);
        if (Prerequisites.CheckRuntime() is { } runtime)
        {
            throw new InvalidOperationException("Window tests cannot run here: " + StartupReport.Describe(runtime));
        }

        var root = NewRoot(profilesFolder);
        var ui = UiThread.StartShared(0x00FFFFFF, exception => log.Error("The test windows' UI thread failed", exception));
        var process = new TestWindowProcess(root, ui, log);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => process.Exit();
        return process;
    }

    /// <summary>
    /// A root of this process's own under <paramref name="profilesFolder"/>, after deleting the roots earlier runs left
    /// that no browser holds any more. A root whose test process still runs is left alone even before its browser
    /// holds it: that process may be about to start one there.
    /// </summary>
    private static string NewRoot(string profilesFolder)
    {
        Directory.CreateDirectory(profilesFolder);
        foreach (var leftover in Directory.GetDirectories(profilesFolder))
        {
            if (!IsHeld(leftover) && !OwnerRuns(leftover))
            {
                TryDelete(leftover);
            }
        }

        var name = $"{DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{Environment.ProcessId}";
        var root = Path.Combine(profilesFolder, name);
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>Whether a browser still uses a user-data folder under <paramref name="root"/>: its lock file exists.</summary>
    private static bool IsHeld(string root) =>
        Directory.EnumerateDirectories(root).Any(folder => File.Exists(Path.Combine(folder, "EBWebView", "lockfile")));

    /// <summary>Whether the test process named by a root's suffix (<c>…-&lt;process id&gt;</c>) still runs.</summary>
    private static bool OwnerRuns(string root)
    {
        var name = Path.GetFileName(root);
        if (!int.TryParse(name.AsSpan(name.LastIndexOf('-') + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            || id == Environment.ProcessId)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(id);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Another test process may be starting in it; a later run tries again.
        }
    }

    /// <summary>The environment and its anchor (UI thread), with Spar's own browser arguments and minimum Runtime.</summary>
    private async Task<SharedBrowser> CreateBrowserAsync(int index, string? arguments)
    {
        var started = Stopwatch.GetTimestamp();
        var folder = Path.Combine(_root, "b" + index.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);
        var created = new EnvironmentCompleted();
        var options = new EnvironmentOptions(arguments, Prerequisites.MinimumRuntime);
        var result = Native.CreateCoreWebView2EnvironmentWithOptions(null, folder, options, created);
        if (result < 0)
        {
            throw new WebView2Exception("CreateCoreWebView2EnvironmentWithOptions", result);
        }

        var pointer = await created.Task;
        var environment = Com.Borrow<ICoreWebView2Environment10>(pointer);
        try
        {
            var anchorWindow = Ui.CreateWindow(
                "Spar test anchor", WindowPlacement.Style, AnchorBounds(), new AnchorMessages(), WindowPlacement.TestExtendedStyle);
            PInvoke.ShowWindow(anchorWindow, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
            var completed = new ControllerCompleted();
            environment.CreateCoreWebView2Controller((nint)anchorWindow, completed);
            var anchor = Com.Take<ICoreWebView2Controller>(await completed.Task);
            DisableReputationChecks(anchor);
            Log.Info($"Test browser {index} ready in {Stopwatch.GetElapsedTime(started).TotalMilliseconds:0} ms ({folder}).");
            return new SharedBrowser(pointer, folder, anchor, anchorWindow);
        }
        catch
        {
            Marshal.Release(pointer);
            throw;
        }
        finally
        {
            Com.Release(environment);
        }
    }

    /// <summary>SmartScreen off for the anchor too: a check it started would hold the browser's exit (pitfalls).</summary>
    private static void DisableReputationChecks(ICoreWebView2Controller anchor)
    {
        var webView = Com.Take<ICoreWebView2_22>(anchor.GetCoreWebView2());
        try
        {
            var settings = Com.Take<ICoreWebView2Settings8>(webView.GetSettings());
            try
            {
                settings.PutIsReputationCheckingRequired(0);
            }
            finally
            {
                Com.Release(settings);
            }
        }
        finally
        {
            Com.Release(webView);
        }
    }

    private static RECT AnchorBounds() => WindowPlacement.Offscreen(new RECT { right = AnchorSize, bottom = AnchorSize });

    /// <summary>
    /// Process exit: the anchors close on the UI thread, and once every browser has let go of this process's root it is
    /// deleted; a browser still held by a window a test left open keeps it for a later run's cleanup.
    /// </summary>
    private void Exit()
    {
        using var closed = new ManualResetEventSlim();
        Ui.Post(() =>
        {
            foreach (var browser in _browsers.Values.Where(task => task.IsCompletedSuccessfully).Select(task => task.Result))
            {
                browser.Close();
            }

            closed.Set();
        });
        if (!closed.Wait(ExitWait))
        {
            return;
        }

        var limit = Stopwatch.GetTimestamp() + (long)(ExitWait.TotalSeconds * Stopwatch.Frequency);
        while (IsHeld(_root) && Stopwatch.GetTimestamp() < limit)
        {
            Thread.Sleep(50);
        }

        if (!IsHeld(_root))
        {
            TryDelete(_root);
        }
    }

    /// <summary>A shared environment, the pointer holding the process's reference, and its anchor (UI thread).</summary>
    private sealed record SharedBrowser(nint Environment, string Folder, ICoreWebView2Controller Anchor, HWND AnchorWindow)
    {
        public void Close()
        {
            try
            {
                Anchor.Close();
            }
            catch (COMException)
            {
                // A browser that is gone refuses the close; its objects are released below anyway.
            }

            Com.Release(Anchor);
            PInvoke.DestroyWindow(AnchorWindow);
            Marshal.Release(Environment);
        }
    }

    /// <summary>The anchor's window handles nothing itself.</summary>
    private sealed class AnchorMessages : IWindowMessages
    {
        public LRESULT? OnMessage(HWND window, uint message, WPARAM wParam, LPARAM lParam) => null;
    }
}
