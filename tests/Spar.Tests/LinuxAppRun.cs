using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using Spar.Linux;

namespace Spar.Tests;

/// <summary>
/// The tests that open windows on the runner's X server run one at a time, after the parallel ones: with no window
/// manager every window opens at the same place, and one over another spoils the frames a theme test reads.
/// </summary>
[CollectionDefinition(nameof(LinuxWindowsCollection), DisableParallelization = true)]
public sealed class LinuxWindowsCollection;

/// <summary>
/// One run of a Linux app's executable for the tests that drive the host from outside (<see cref="LinuxHostTests"/>,
/// <see cref="LinuxStartupTests"/>): its process under the runner's X server, every folder the app, GTK, WebKit and its
/// sandbox write to under the repository's artifacts (AGENTS.md), and its log. Until the Linux Window surface (M1c
/// step 7), these run where a runner provides an X server of its own in <c>SPAR_LINUX_TEST_DISPLAY</c> (never WSLg's)
/// and the app's executable; anywhere else they skip, saying why.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class LinuxAppRun : IAsyncDisposable
{
    /// <summary>The bridge probe's app id (with ".Development" in its Development build's folder).</summary>
    public const string ProbeAppId = "com.example.tests.BridgeProbe";

    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private readonly Process _process;
    private readonly string _display;
    private readonly string _appFolder;
    private readonly StringBuilder _output = new();
    private readonly string? _wrappedName;

    private LinuxAppRun(Process process, string display, string runFolder, string appFolder, DateTime launched, string? wrappedName)
    {
        _wrappedName = wrappedName;
        _process = process;
        _display = display;
        RunFolder = runFolder;
        _appFolder = appFolder;
        Launched = launched;
        process.OutputDataReceived += (_, line) => Append(line.Data);
        process.ErrorDataReceived += (_, line) => Append(line.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    /// <summary>When the process was started, in UTC, as the log's times are.</summary>
    public DateTime Launched { get; }

    /// <summary>The run's private folders (<c>home</c>, <c>data</c>, …), which a later launch may share.</summary>
    public string RunFolder { get; }

    /// <summary>The app's process: the one started, or, through a wrapper, its descendant that runs the executable.</summary>
    public int Id => _wrappedName is null ? _process.Id : Descendant(_process.Id, _wrappedName) ?? _process.Id;

    public bool HasExited => _process.HasExited;

    /// <summary>The exit code, once <see cref="WaitForExitAsync"/> returned; -1 before.</summary>
    public int ExitCode => _process.HasExited ? _process.ExitCode : -1;

    /// <summary>What the process wrote to standard output and error so far.</summary>
    public string Output
    {
        get
        {
            lock (_output)
            {
                return _output.ToString();
            }
        }
    }

    /// <summary>The X server of the runner's own, or a skip.</summary>
    public static string Display()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The Linux host runs on Linux only.");
        var display = Environment.GetEnvironmentVariable("SPAR_LINUX_TEST_DISPLAY");
        Assert.SkipWhen(string.IsNullOrEmpty(display), "Needs SPAR_LINUX_TEST_DISPLAY, an X server of the run's own (never the user's desktop).");
        return display!;
    }

    /// <summary>The bridge probe's executable, or a skip.</summary>
    public static string Probe()
    {
        var executable = Environment.GetEnvironmentVariable("SPAR_LINUX_HOST_APP");
        Assert.SkipWhen(string.IsNullOrEmpty(executable), "Needs SPAR_LINUX_HOST_APP, the bridge probe's executable.");
        return executable!;
    }

    /// <summary>Whether the probe is a Development build, whose automated mode and test variables a Production one lacks.</summary>
    public static bool IsDevelopmentProbe() => Probe().Contains("/Development/", StringComparison.Ordinal);

    /// <summary>Starts the bridge probe with <paramref name="plan"/>; skips where the runner provides no probe or display.</summary>
    public static LinuxAppRun Start(string plan, params (string Name, string? Value)[] variables) =>
        Launch(Probe(), ProbeAppId, [("BRIDGE_PROBE_PLAN", plan), .. variables]);

    /// <summary>
    /// Starts <paramref name="executable"/> under the run's X server, in <paramref name="runFolder"/> (a new one when
    /// null; a second launch of the same app passes the first one's), through <paramref name="wrapper"/> when given (a
    /// command and its arguments the executable is appended to). <paramref name="appId"/> names its folder under
    /// <c>XDG_DATA_HOME</c>, where its log is. A variable with a null value is removed from the environment.
    /// </summary>
    public static LinuxAppRun Launch(
        string executable,
        string appId,
        IEnumerable<(string Name, string? Value)> variables,
        string? runFolder = null,
        IReadOnlyList<string>? wrapper = null,
        IReadOnlyList<string>? arguments = null)
    {
        var display = Display();
        var run = runFolder ?? Path.Combine(TestFolders.Artifacts(), "linux-homes", "host-tests", Guid.NewGuid().ToString("N")[..12]);
        var start = new ProcessStartInfo(wrapper is { Count: > 0 } ? wrapper[0] : executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (wrapper is { Count: > 0 })
        {
            foreach (var argument in wrapper.Skip(1))
            {
                start.ArgumentList.Add(argument);
            }

            start.ArgumentList.Add(executable);
        }

        foreach (var argument in arguments ?? [])
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var folder in new[] { "home", "data", "config", "cache", "state" })
        {
            Directory.CreateDirectory(Path.Combine(run, folder));
        }

        start.Environment["HOME"] = Path.Combine(run, "home");
        start.Environment["XDG_DATA_HOME"] = Path.Combine(run, "data");
        start.Environment["XDG_CONFIG_HOME"] = Path.Combine(run, "config");
        start.Environment["XDG_CACHE_HOME"] = Path.Combine(run, "cache");
        start.Environment["XDG_STATE_HOME"] = Path.Combine(run, "state");
        start.Environment["DISPLAY"] = display;
        start.Environment["GDK_BACKEND"] = "x11";
        start.Environment.Remove("WAYLAND_DISPLAY");
        start.Environment.Remove("WEBKIT_INSPECTOR_SERVER");
        start.Environment.Remove("WEBKIT_INSPECTOR_HTTP_SERVER");
        foreach (var (name, value) in variables)
        {
            if (value is null)
            {
                start.Environment.Remove(name);
            }
            else
            {
                start.Environment[name] = value;
            }
        }

        var launched = DateTime.UtcNow;
        var process = Process.Start(start) ?? throw new InvalidOperationException($"{executable} did not start.");
        var appFolder = Path.Combine(run, "data", appId);
        var wrappedName = wrapper is { Count: > 0 } ? Path.GetFileName(executable)[..Math.Min(15, Path.GetFileName(executable).Length)] : null;
        return new LinuxAppRun(process, display, run, appFolder, launched, wrappedName);
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

    /// <summary>The app's own folder under <c>XDG_DATA_HOME</c> (its data root), with ".Development" in a Development build.</summary>
    public string? AppFolder() =>
        Directory.Exists(Path.GetDirectoryName(_appFolder))
            ? Directory.EnumerateDirectories(Path.GetDirectoryName(_appFolder)!, Path.GetFileName(_appFolder) + "*").FirstOrDefault()
            : null;

    public string Log()
    {
        try
        {
            if (AppFolder() is not { } folder)
            {
                return "";
            }

            using var stream = new FileStream(Path.Combine(folder, "logs", "app.log"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
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

            Assert.False(_process.HasExited, $"The app exited with {(_process.HasExited ? _process.ExitCode : 0)} before '{pattern}'.\n{Describe()}");
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
        var processes = WebKitProcesses().Select(process => process.Id).Prepend(_process.Id);
        var kilobytes = 0L;
        foreach (var process in processes)
        {
            var line = File.ReadLines($"/proc/{process}/smaps_rollup").FirstOrDefault(text => text.StartsWith("Pss:", StringComparison.Ordinal));
            kilobytes += line is null ? 0 : long.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1], CultureInfo.InvariantCulture);
        }

        return kilobytes / 1024.0;
    }

    public List<(int Id, string Name)> WebKitProcesses() => WebProcesses.Below(_process.Id);

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
    public int WindowCount() => X11Frames.Count(_display, Id);

    /// <summary>
    /// The window manager's close to the process's main window only (its largest), without waiting for the exit: as a
    /// person's click on its close button, which never reaches a dialog the app shows meanwhile.
    /// </summary>
    public void RequestClose()
    {
        var window = X11Frames.MainWindow(_display, Id);
        Assert.True(window != 0 && X11Close.Send(_display, Id, window) == 1, "The app's main window was not found to close.");
    }

    /// <summary>The window manager's close to every window of the process, then the exit code.</summary>
    public async Task<int> CloseAsync()
    {
        Assert.True(X11Close.Send(_display, Id) > 0, "No window of the app was found to close.");
        return await WaitForExitAsync();
    }

    /// <summary>Waits until the process shows a window on the display (a message, or the app's window); the time it took.</summary>
    public async Task<TimeSpan> WaitForWindowAsync()
    {
        var limit = Stopwatch.StartNew();
        while (!X11Frames.Shows(_display, Id))
        {
            Assert.False(_process.HasExited, $"The app exited with {(_process.HasExited ? _process.ExitCode : 0)} before it had a window.\n{Describe()}");
            Assert.True(limit.Elapsed < Patience, $"No window within {Patience.TotalSeconds} s.\n{Describe()}");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        return DateTime.UtcNow - Launched;
    }

    public Task<int> WaitForExitAsync() => WaitForExitAsync(Patience);

    public async Task<int> WaitForExitAsync(TimeSpan patience)
    {
        using var limit = new CancellationTokenSource(patience);
        try
        {
            await _process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            Assert.Fail($"The app did not exit within {patience.TotalSeconds} s.\n{Describe()}");
        }

        return _process.ExitCode;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }

        TestContext.Current.TestOutputHelper?.WriteLine(Describe());
        _process.Dispose();
    }

    /// <summary>The first process below <paramref name="root"/> named <paramref name="name"/> (the kernel keeps 15 characters).</summary>
    private static int? Descendant(int root, string name)
    {
        var pending = new Stack<int>([root]);
        while (pending.TryPop(out var parent))
        {
            foreach (var task in Directory.Exists($"/proc/{parent}/task") ? Directory.EnumerateDirectories($"/proc/{parent}/task") : [])
            {
                string children;
                try
                {
                    children = File.ReadAllText(Path.Combine(task, "children"));
                }
                catch (IOException)
                {
                    continue;
                }

                foreach (var child in children.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(text => int.Parse(text, CultureInfo.InvariantCulture)))
                {
                    try
                    {
                        if (File.ReadAllText($"/proc/{child}/comm").Trim() == name)
                        {
                            return child;
                        }
                    }
                    catch (IOException)
                    {
                        // The process ended meanwhile.
                    }

                    pending.Push(child);
                }
            }
        }

        return null;
    }

    public string Describe() => $"--- app.log ---\n{Log()}\n--- output ---\n{Output}";

    private void Append(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_output)
        {
            _output.AppendLine(line);
        }
    }
}
