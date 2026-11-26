using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Spar.Linux;
using Spar.Testing;

namespace Spar.WindowTests;

/// <summary>
/// The Linux host tests that run an app as a process of its own (<see cref="LinuxHostTests"/>, <see cref="LinuxHangTests"/>,
/// <see cref="LinuxPageSecurityTests"/>, <see cref="LinuxStartupTests"/>, <see cref="LinuxSessionEndTests"/>) run one at a
/// time, after the parallel ones: they send real input to the window under the pointer and read its pixels, on a private
/// Xvfb of their own where nothing else opens (<see cref="LinuxDesktop.InputDisplay"/>).
/// <para>
/// Their traits, so that a CI run can take every test it runs with no skip (<c>eng/linux-ci/run.sh</c>): all have
/// <c>Surface=BlackBox</c> and <c>App=BridgeProbe</c>; <c>Probe=Development</c> marks a test that needs the probe's
/// Development build (its automated mode, triggers or heartbeat interval), which a run against the Production probe
/// leaves out and a run against the Development build takes; <c>Probe=None</c> marks one that runs another executable
/// (the Notebook), which the Development run leaves out. The flood's numbers are an explicit test (a measurement).
/// </para>
/// </summary>
[CollectionDefinition(nameof(ProbeCollection), DisableParallelization = true)]
public sealed class ProbeCollection;

/// <summary>
/// One launch, through the black box (<see cref="SparBlackBox"/>), of the bridge probe (<c>tests/apps/BridgeProbe</c>, its
/// Production NativeAOT executable, <c>Projects.BridgeProbe</c>) or another executable a runner names: the probe's
/// Development build (<c>SPAR_LINUX_HOST_APP</c>, whose automated mode, triggers and scripted answers a Production build
/// lacks), the probe built with <c>csp.AllowWebAssembly()</c> (<c>SPAR_LINUX_WASM_APP</c>) or the Notebook
/// (<c>SPAR_LINUX_NOTEBOOK</c>). Its folders are a black-box data root (<c>home</c>, <c>data</c>, <c>config</c>…), its log is
/// read as it grows, and the window manager's close and window counts go through X11 on the private display.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class ProbeApp : IAsyncDisposable
{
    /// <summary>The bridge probe's app id (with ".Development" in its Development build's folder).</summary>
    public const string ProbeAppId = "com.example.tests.BridgeProbe";

    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    /// <summary>The roots <see cref="NewRunFolder"/> made, which the launch that takes each deletes.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> Prepared = new(StringComparer.Ordinal);

    private readonly SparBlackBoxApp _app;
    private readonly string? _ownedRoot;

    private ProbeApp(SparBlackBoxApp app, DateTime launched, string? ownedRoot, bool development)
    {
        _app = app;
        IsDevelopmentBuild = development;
        Launched = launched;
        _ownedRoot = ownedRoot;
    }

    /// <summary>When the process was started, in UTC, as the log's times are.</summary>
    public DateTime Launched { get; }

    /// <summary>The launch's data root (<c>home</c>, <c>data</c>, …), which a later launch may share.</summary>
    public string RunFolder => _app.DataRoot;

    /// <summary>The app's process: through a wrapper, its descendant that runs the executable.</summary>
    public int Id => _app.ProcessId;

    public bool HasExited => _app.HasExited;

    /// <summary>The exit code, once the app exited; -1 before.</summary>
    public int ExitCode => _app.ExitCode ?? -1;

    /// <summary>What the process wrote to standard output and error so far.</summary>
    public string Output => _app.StandardOutput + _app.StandardError;

    /// <summary>The display the app shows on.</summary>
    public string AppDisplay => _app.Display;

    /// <summary>The launch through the black box, for what it reads itself (dialogs, the accessibility tree).</summary>
    public SparBlackBoxApp App => _app;

    /// <summary>Whether this launch runs a Development build, whose scripted answers and triggers a Production one lacks.</summary>
    public bool IsDevelopmentBuild { get; }

    /// <summary>The private display of these tests, or a skip where the black box cannot run.</summary>
    public static string Display()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The Linux host runs on Linux only.");
        WindowSupport.SkipUnlessBlackBox();
        return LinuxDesktop.InputDisplay;
    }

    /// <summary>The bridge probe's executable: a runner's own build in <c>SPAR_LINUX_HOST_APP</c>, else its Production one.</summary>
    public static string Probe()
    {
        Display();
        return Environment.GetEnvironmentVariable("SPAR_LINUX_HOST_APP") is { Length: > 0 } built ? built : Projects.BridgeProbe.ExecutablePath;
    }

    /// <summary>The probe built with <c>csp.AllowWebAssembly()</c>, or a skip: page security is read by the build.</summary>
    public static string WebAssemblyProbe()
    {
        var executable = Environment.GetEnvironmentVariable("SPAR_LINUX_WASM_APP");
        Assert.SkipWhen(string.IsNullOrEmpty(executable), "Needs SPAR_LINUX_WASM_APP, the bridge probe built with AllowWebAssembly().");
        return executable!;
    }

    /// <summary>Whether the probe is a Development build, whose automated mode and test variables a Production one lacks.</summary>
    public static bool IsDevelopmentProbe() => IsDevelopment(Probe());

    /// <summary>Whether <paramref name="executable"/> is a runner's Development build (the tests' own output folder is named after its variant too).</summary>
    public static bool IsDevelopment(string executable) =>
        executable != Projects.BridgeProbe.ExecutablePath && executable.Contains("/bin/Development/", StringComparison.Ordinal);

    /// <summary>A new data root of these tests, created, for a test that writes files into it before the launch.</summary>
    public static string NewRunFolder()
    {
        Display();
        var root = TestDataRoots.NextBlackBox();
        BlackBoxFolders.For(root).Create();
        Prepared[root] = true;
        return root;
    }

    /// <summary>Starts the bridge probe with <paramref name="plan"/>; skips where the black box cannot run.</summary>
    public static Task<ProbeApp> StartAsync(string plan, params (string Name, string? Value)[] variables) =>
        LaunchAsync(Probe(), ProbeAppId, [("BRIDGE_PROBE_PLAN", plan), .. variables]);

    /// <summary>
    /// Starts <paramref name="executable"/> on the private display, in <paramref name="runFolder"/> (a new root when null; a
    /// second launch of the same app passes the first one's), through <paramref name="wrapper"/> when given (a command the
    /// executable is appended to). <paramref name="appId"/> names its folder under <c>XDG_DATA_HOME</c>, where its log is. A
    /// variable with a null value is left out; a null <c>DISPLAY</c> removes the display, and <c>DBUS_SESSION_BUS_ADDRESS</c>
    /// replaces the launch's private bus.
    /// </summary>
    public static async Task<ProbeApp> LaunchAsync(
        string executable,
        string appId,
        IEnumerable<(string Name, string? Value)> variables,
        string? runFolder = null,
        IReadOnlyList<string>? wrapper = null,
        IReadOnlyList<string>? arguments = null)
    {
        var display = Display();
        var development = IsDevelopment(executable);
        var project = executable == Projects.BridgeProbe.ExecutablePath
            ? Projects.BridgeProbe
            : SparProject.Built(executable, Path.GetFileName(executable), development ? appId + ".Development" : appId);
        var owned = runFolder is not null && Prepared.TryRemove(runFolder, out _) ? runFolder : null;
        var launched = DateTime.UtcNow;
        var app = await SparBlackBox.LaunchAsync(project, options =>
        {
            options.WaitForReady = false;
            options.DataRoot = runFolder;
            options.Display = display;
            options.Wrapper = wrapper;
            foreach (var argument in arguments ?? [])
            {
                options.Arguments.Add(argument);
            }

            foreach (var (name, value) in variables)
            {
                if (name == "DISPLAY")
                {
                    options.Display = value ?? "";
                }
                else if (name == "DBUS_SESSION_BUS_ADDRESS")
                {
                    options.SessionBus = value;
                }
                else if (value is null)
                {
                    continue;
                }
                else if (name.StartsWith("SPAR_", StringComparison.Ordinal))
                {
                    options.DevelopmentVariables[name] = value;
                }
                else
                {
                    options.Environment[name] = value;
                }
            }
        }, TestContext.Current.CancellationToken);
        return new ProbeApp(app, launched, owned, development);
    }

    /// <summary>
    /// Answers the app's native dialog with <paramref name="button"/>: a Development build's scripted answers
    /// (<c>SPAR_DEV_PROMPT_ANSWERS</c>) do it themselves; on a Production build the black box reads the dialog over AT-SPI
    /// and presses the button, as a person would (no input).
    /// </summary>
    public async Task AnswerAsync(string button, TimeSpan? patience = null)
    {
        if (IsDevelopmentBuild)
        {
            return;
        }

        var dialog = await _app.WaitForDialogAsync(patience ?? TimeSpan.FromSeconds(40), TestContext.Current.CancellationToken);
        await dialog.PressAsync(button);
    }

    public static bool IsRunning(int process)
    {
        try
        {
            return File.ReadAllText($"/proc/{process}/comm").StartsWith("WebKit", StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>The environment a process started with (<c>/proc/&lt;pid&gt;/environ</c>), as name and value.</summary>
    public static Dictionary<string, string> EnvironmentOf(int process)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in File.ReadAllText($"/proc/{process}/environ").Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Split('=', 2);
            variables[parts[0]] = parts.Length == 2 ? parts[1] : "";
        }

        return variables;
    }

    /// <summary>The app's own folder under <c>XDG_DATA_HOME</c> (its data root), once it exists.</summary>
    public string? AppFolder() => Directory.Exists(_app.AppFolder) ? _app.AppFolder : null;

    /// <summary>
    /// The app's log up to its last complete line. A line the app is still writing is left out: a pattern ending in
    /// <c>$</c> would otherwise match the part written so far (a long JSON line was once read cut at 3,631 bytes).
    /// </summary>
    public string Log()
    {
        try
        {
            using var stream = new FileStream(_app.LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var text = reader.ReadToEnd();
            return text[..(text.LastIndexOf('\n') + 1)];
        }
        catch (IOException)
        {
            return "";
        }
    }

    /// <summary>Waits for a line of the app's log that matches <paramref name="pattern"/>, and returns the match.</summary>
    public Task<Match> WaitForAsync(string pattern) => WaitForAsync(pattern, Patience);

    /// <summary>Waits up to <paramref name="patience"/> for a line of the app's log that matches <paramref name="pattern"/>.</summary>
    public async Task<Match> WaitForAsync(string pattern, TimeSpan patience)
    {
        var expression = new Regex(pattern, RegexOptions.Multiline);
        var limit = Stopwatch.StartNew();
        while (limit.Elapsed < patience)
        {
            if (expression.Match(Log()) is { Success: true } found)
            {
                return found;
            }

            Assert.False(HasExited, $"The app exited with {ExitCode} before '{pattern}'.\n{Describe()}");
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"No '{pattern}' in the app's log within {patience.TotalSeconds} s.\n{Describe()}");
        return Match.Empty;
    }

    /// <summary>The time of the log line <paramref name="match"/> starts, in UTC.</summary>
    public static DateTime TimeOf(Match match) =>
        DateTime.Parse(match.Value[..match.Value.IndexOf(' ', StringComparison.Ordinal)], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    /// <summary>The proportional set size of the app and its WebKit processes, in MB (<c>smaps_rollup</c>).</summary>
    public double TotalPssMegabytes()
    {
        var kilobytes = 0L;
        foreach (var process in WebKitProcesses().Select(process => process.Id).Prepend(Id))
        {
            var line = File.ReadLines($"/proc/{process}/smaps_rollup").FirstOrDefault(text => text.StartsWith("Pss:", StringComparison.Ordinal));
            kilobytes += line is null ? 0 : long.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1], CultureInfo.InvariantCulture);
        }

        return kilobytes / 1024.0;
    }

    public List<(int Id, string Name)> WebKitProcesses() => WebProcesses.Below(Id);

    /// <summary>Ends the page's web process, as a crash would.</summary>
    public void EndWebProcess()
    {
        var pages = WebKitProcesses().Where(process => process.Name.StartsWith("WebKitWebProces", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(pages);
        foreach (var (id, _) in pages)
        {
            Process.GetProcessById(id).Kill();
        }
    }

    /// <summary>How many windows the process shows: its window, and one more for each dialog of its own that is open.</summary>
    public int WindowCount() => X11Frames.Count(AppDisplay, Id);

    /// <summary>
    /// The window manager's close to the process's main window only (its largest), without waiting for the exit: as a
    /// person's click on its close button, which never reaches a dialog the app shows meanwhile.
    /// </summary>
    public void RequestClose()
    {
        var window = X11Frames.MainWindow(AppDisplay, Id);
        Assert.True(window != 0 && X11Close.Send(AppDisplay, Id, window) == 1, "The app's main window was not found to close.");
    }

    /// <summary>The window manager's close to every window of the process, then the exit code.</summary>
    public async Task<int> CloseAsync()
    {
        Assert.True(X11Close.Send(AppDisplay, Id) > 0, $"No window of the app was found to close.\n{Describe()}");
        return await WaitForExitAsync();
    }

    /// <summary>Waits until the process shows a window on the display (a message, or the app's window); the time it took.</summary>
    public async Task<TimeSpan> WaitForWindowAsync()
    {
        var limit = Stopwatch.StartNew();
        while (!X11Frames.Shows(AppDisplay, Id))
        {
            Assert.False(HasExited, $"The app exited with {ExitCode} before it had a window.\n{Describe()}");
            Assert.True(limit.Elapsed < Patience, $"No window within {Patience.TotalSeconds} s.\n{Describe()}");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        return DateTime.UtcNow - Launched;
    }

    public Task<int> WaitForExitAsync() => WaitForExitAsync(Patience);

    public async Task<int> WaitForExitAsync(TimeSpan patience)
    {
        try
        {
            return await _app.Process.Exited.WaitAsync(patience, TestContext.Current.CancellationToken);
        }
        catch (TimeoutException)
        {
            Assert.Fail($"The app did not exit within {patience.TotalSeconds} s.\n{Describe()}");
            return -1;
        }
    }

    public async ValueTask DisposeAsync()
    {
        TestContext.Current.TestOutputHelper?.WriteLine(Describe());
        await _app.DisposeAsync();
        if (_ownedRoot is not null)
        {
            TestDataRoots.Delete(_ownedRoot);
            BlackBoxFolders.For(_ownedRoot).DeleteRuntime();
        }
    }

    public string Describe() => $"--- app.log ---\n{Log()}\n--- output ---\n{Output}";
}
