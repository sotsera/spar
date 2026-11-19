using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using Spar.Testing;
using Spar.Testing.BlackBox.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;

// Black-box desktop spike: see RESULTS.md. Verbs:
//   run hidden|shown <count>   launch, ready, UI Automation queries, Invoke, close; one line per launch
//   dump hidden|shown          the page's UI Automation tree (control type, ARIA role, name)
//   relaunch hidden|shown      a failed relaunch (lock held / primary suspended): the native message read and answered
//   startfail hidden|shown     a start-up failure that shows a native message (profile folder is a file)
[assembly: SupportedOSPlatform("windows10.0.19045")]

var verb = args.ElementAtOrDefault(0) ?? "run";
var hidden = (args.ElementAtOrDefault(1) ?? "hidden") == "hidden";
var count = int.Parse(args.ElementAtOrDefault(2) ?? "1", CultureInfo.InvariantCulture);
var spike = new Spike(hidden);
return verb switch
{
    "run" => await spike.RunAsync(count),
    "dump" => await spike.DumpAsync(),
    "types" => await spike.TypesAsync(),
    "windows" => await spike.WindowsAsync(),
    "relaunch" => await spike.RelaunchAsync(),
    "startfail" => await spike.StartFailAsync(),
    _ => 2,
};

[SupportedOSPlatform("windows10.0.19045")]
internal sealed partial class Spike(bool hidden)
{
    private static readonly string AppId = Environment.GetEnvironmentVariable("SPIKE_APP_ID") ?? "com.example.Notebook";
    private static readonly string Repository = FindRepository();
    private static readonly string Executable = Environment.GetEnvironmentVariable("SPIKE_EXE") ?? Path.Combine(Repository, @"samples\Notebook\bin\Production\Release\net11.0\win-x64\publish\Notebook.exe");
    private static readonly string Stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
    private int _launch;

    private string Mode => hidden ? "hidden" : "shown";

    public async Task<int> RunAsync(int count)
    {
        var realFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppId);
        Console.WriteLine($"# mode={Mode} count={count} real-folder-exists-before={Directory.Exists(realFolder)}");
        Console.WriteLine("launch,ok,ready_ms,window_ms,heading_ms,invoke_ms,close_ms,exit,tree_ms,started_processes,foreground_hits,visible_on_input_desktop,desktop_gone,heading_type,heading_aria,failure");
        var failures = 0;
        var rows = new List<double[]>();
        for (var index = 0; index < count; index++)
        {
            var result = await OneAsync();
            Console.WriteLine(result.Line);
            if (!result.Ok)
            {
                failures++;
            }
            else
            {
                rows.Add(result.Times);
            }
        }

        Console.WriteLine($"# failures={failures} of {count}; real-folder-exists-after={Directory.Exists(realFolder)}");
        string[] names = ["ready", "window", "heading", "invoke", "close", "tree"];
        for (var column = 0; column < names.Length; column++)
        {
            var values = rows.Select(r => r[column]).Order().ToList();
            if (values.Count > 0)
            {
                Console.WriteLine($"# {names[column]}: median {values[values.Count / 2]:0} ms, min {values[0]:0}, max {values[^1]:0}");
            }
        }

        return failures == 0 ? 0 : 1;
    }

    public async Task<int> WindowsAsync()
    {
        using var run = Launch(out var desktop);
        using var automation = AutomationThread.Start(desktop);
        await WaitReadyAsync(run);
        await Task.Delay(500);
        var lines = await automation.RunAsync(uia => TopWindows.Of(run.Process.ProcessId).Select(w =>
        {
            var e = uia.FromHandle(w.Handle);
            var count = e is null ? -1 : uia.FindAll(e, Uia.ScopeDescendants, uia.True()).Count;
            return $"{w.Handle:x} {w.ClassName} owner={w.Owner:x} name='{e?.Name}' type={e?.ControlType} descendants={count}";
        }).ToList());
        lines.ForEach(Console.WriteLine);
        var viaRoot = await automation.RunAsync(uia => uia.FindAll(uia.Root(), Uia.ScopeChildren, uia.Equal(Uia.ProcessIdProperty, run.Process.ProcessId))
            .Select(e => $"root child {e.Handle:x} {e.ClassName} '{e.Name}' descendants={uia.FindAll(e, Uia.ScopeDescendants, uia.True()).Count}").ToList());
        viaRoot.ForEach(Console.WriteLine);
        run.Process.Kill();
        await run.Process.WaitForTreeAsync(TimeSpan.FromSeconds(10));
        desktop?.Dispose();
        return 0;
    }

    public async Task<int> TypesAsync()
    {
        using var run = Launch(out var desktop);
        await WaitReadyAsync(run);
        await Task.Delay(1500);
        var ids = string.Join(",", run.Process.ProcessIds);
        var script = $"Get-CimInstance Win32_Process | Where-Object {{ @({ids}) -contains $_.ProcessId }} | ForEach-Object {{ $t = if ($_.CommandLine -match '--type=([a-z-]+)') {{ $Matches[1] }} else {{ 'main' }}; $g = if ($_.CommandLine -match '--use-angle=([a-z0-9-]+)') {{ ' angle=' + $Matches[1] }} else {{ '' }}; $s = if ($_.CommandLine -match '--disable-gpu') {{ ' disable-gpu' }} else {{ '' }}; \"$($_.Name) $t$g$s\" }}";
        var info = new ProcessStartInfo("powershell", ["-NoProfile", "-Command", script]) { RedirectStandardOutput = true };
        using (var shell = System.Diagnostics.Process.Start(info)!)
        {
            Console.WriteLine(await shell.StandardOutput.ReadToEndAsync());
            await shell.WaitForExitAsync();
        }

        var gpuCache = Directory.Exists(Path.Combine(run.Root, "LocalAppData", AppId, "WebView2", "EBWebView", "Default", "GPUCache"));
        Console.WriteLine($"processes {run.Process.ProcessIds.Count}, started {run.Process.StartedProcesses}, GPUCache folder {gpuCache}");
        run.Process.Kill();
        await run.Process.WaitForTreeAsync(TimeSpan.FromSeconds(10));
        desktop?.Dispose();
        return 0;
    }

    public async Task<int> DumpAsync()
    {
        using var run = Launch(out var desktop);
        using var automation = AutomationThread.Start(desktop);
        await WaitReadyAsync(run);
        var window = await WaitWindowAsync(automation, run.Process.ProcessId);
        await WaitNamedAsync(automation, run.Process.ProcessId, Environment.GetEnvironmentVariable("SPIKE_WAIT_FOR") ?? "Start your notebook");
        var lines = await automation.RunAsync(uia =>
        {
            var top = TopWindow(uia, run.Process.ProcessId)!;
            var all = uia.FindAll(top, Uia.ScopeDescendants, uia.True());
            return all.Take(400).Select(e => $"{e.ControlType}\t{e.LocalizedControlType}\t{e.AriaRole}\t{e.ClassName}\t{e.Name.Replace('\n', ' ')}").ToList();
        });
        Console.WriteLine($"# window '{window}', {lines.Count} elements");
        foreach (var line in lines)
        {
            Console.WriteLine(line);
        }

        await CloseAsync(automation, run);
        desktop?.Dispose();
        return 0;
    }

    public async Task<int> RelaunchAsync()
    {
        // 1. The lock held with nobody serving it: the lock wait (about 5 s), then "not responding".
        {
            using var run = Prepare(out var desktop, out var root);
            var folder = Path.Combine(root, "LocalAppData", AppId);
            Directory.CreateDirectory(folder);
            using (new FileStream(Path.Combine(folder, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                using var automation = AutomationThread.Start(desktop);
                var clock = Stopwatch.StartNew();
                run.Start(desktop);
                await ReportDialogAsync("lock held", automation, run, clock);
            }

            desktop?.Dispose();
        }

        // 2. A real primary, suspended: the relaunch is not acknowledged within 2 s.
        {
            using var primary = Launch(out var desktop);
            using var automation = AutomationThread.Start(desktop);
            await WaitReadyAsync(primary);
            await WaitWindowAsync(automation, primary.Process.ProcessId);
            primary.Process.SuspendProcess();
            using var second = new Run(primary.Root, Executable);
            var clock = Stopwatch.StartNew();
            second.Start(desktop);
            await ReportDialogAsync("primary suspended", automation, second, clock);
            primary.Process.Kill();
            Console.WriteLine($"primary killed; tree gone: {await primary.Process.WaitForTreeAsync(TimeSpan.FromSeconds(10))}");
            desktop?.Dispose();
        }

        return 0;
    }

    public async Task<int> StartFailAsync()
    {
        using var run = Prepare(out var desktop, out var root);
        var folder = Path.Combine(root, "LocalAppData", AppId);
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "WebView2"), "not a folder");
        using var automation = AutomationThread.Start(desktop);
        var clock = Stopwatch.StartNew();
        run.Start(desktop);
        await ReportDialogAsync("profile folder is a file", automation, run, clock);
        desktop?.Dispose();
        return 0;
    }

    private async Task ReportDialogAsync(string label, AutomationThread automation, Run run, Stopwatch clock)
    {
        var pid = run.Process.ProcessId;
        var dialog = await Poll(TimeSpan.FromSeconds(15), () => automation.RunAsync(uia =>
        {
            foreach (var window in TopWindows.Of(pid).Where(w => w.IsDialog))
            {
                var found = uia.FromHandle(window.Handle)!;
                var texts = uia.FindAll(found, Uia.ScopeDescendants, uia.Equal(Uia.ControlTypeProperty, UiaElement.TextType)).Select(t => t.Name).ToList();
                var buttons = DialogButtons(uia, found).Select(b => b.Name).ToList();
                return (Title: found.Name, Texts: texts, Buttons: buttons, Owned: window.Owner != 0);
            }

            return ((string Title, List<string> Texts, List<string> Buttons, bool Owned)?)null;
        }));
        var shownAfter = clock.ElapsedMilliseconds;
        if (dialog is not { } found)
        {
            Console.WriteLine($"{label}: no dialog within 15 s; exit {run.Process.ExitCode}; stderr: {Redact(run.Process.StandardError.Trim())}");
            run.Process.Kill();
            return;
        }

        Console.WriteLine($"{label}: dialog after {shownAfter} ms (owned by the window: {found.Owned}); title '{found.Title}'");
        Console.WriteLine($"  texts: {string.Join(" | ", found.Texts.Select(t => t.Replace('\n', ' ')))}");
        Console.WriteLine($"  buttons: {string.Join(" | ", found.Buttons)}");
        var button = found.Buttons.FirstOrDefault(b => b is "OK" or "Close") ?? found.Buttons.First();
        await automation.RunAsync(uia =>
        {
            foreach (var window in TopWindows.Of(pid).Where(w => w.IsDialog))
            {
                var target = DialogButtons(uia, uia.FromHandle(window.Handle)!).FirstOrDefault(b => b.Name == button);
                if (target is not null)
                {
                    uia.Pattern<IUIAutomationInvokePattern>(target, Uia.InvokePattern)!.Invoke();
                    return true;
                }
            }

            return false;
        });
        var pressed = clock.ElapsedMilliseconds;
        var exit = await run.Process.Exited.WaitAsync(TimeSpan.FromSeconds(10));
        Console.WriteLine($"  pressed '{button}'; exit {exit} after {clock.ElapsedMilliseconds - pressed} ms; tree gone {await run.Process.WaitForTreeAsync(TimeSpan.FromSeconds(10))}");
        Console.WriteLine($"  stderr: {Redact(run.Process.StandardError.Trim())}");
    }

    private async Task<(bool Ok, string Line, double[] Times)> OneAsync()
    {
        var index = Interlocked.Increment(ref _launch);
        var watch = new FocusWatch();
        HiddenDesktop? desktop = null;
        Run? run = null;
        var times = new double[6];
        string headingType = "", headingAria = "", failure = "";
        var exit = -1;
        var desktopGone = "";
        try
        {
            run = Prepare(out desktop, out _);
            using var automation = AutomationThread.Start(desktop);
            var clock = Stopwatch.StartNew();
            run.Start(desktop);
            watch.Start(run.Process);
            await WaitReadyAsync(run);
            times[0] = clock.Elapsed.TotalMilliseconds;
            await WaitWindowAsync(automation, run.Process.ProcessId);
            times[1] = clock.Elapsed.TotalMilliseconds;
            (headingType, headingAria) = await WaitNamedAsync(automation, run.Process.ProcessId, "Start your notebook");
            times[2] = clock.Elapsed.TotalMilliseconds;
            var invokeClock = Stopwatch.StartNew();
            using var watcher = AutomationThread.Start(desktop);
            var invoking = automation.RunAsync(uia =>
            {
                var top = TopWindow(uia, run.Process.ProcessId)!;
                var button = uia.FindFirst(top, Uia.ScopeDescendants, uia.And(uia.Equal(Uia.ControlTypeProperty, UiaElement.ButtonType), uia.Equal(Uia.NameProperty, "New page")))
                    ?? throw new InvalidOperationException("no New page button");
                uia.Pattern<IUIAutomationInvokePattern>(button, Uia.InvokePattern)!.Invoke();
                return invokeClock.Elapsed.TotalMilliseconds;
            });
            await WaitNamedAsync(watcher, run.Process.ProcessId, "Create page");
            var seen = invokeClock.Elapsed.TotalMilliseconds;
            var invoked = await invoking;
            Console.Error.WriteLine($"dialog seen by another thread {seen:0} ms; invoke returned {invoked:0} ms");
            times[3] = seen;
            times[3] = invokeClock.Elapsed.TotalMilliseconds;

            var closeClock = Stopwatch.StartNew();
            exit = await CloseAsync(automation, run);
            times[4] = closeClock.Elapsed.TotalMilliseconds;
            var treeGone = await run.Process.WaitForTreeAsync(TimeSpan.FromSeconds(10));
            times[5] = closeClock.Elapsed.TotalMilliseconds;
            if (!treeGone)
            {
                failure = "tree left";
            }
            else if (exit != 0)
            {
                failure = $"exit {exit}";
            }
        }
        catch (Exception exception)
        {
            failure = exception.GetType().Name + ": " + exception.Message.Replace(',', ';');
            run?.Process.Kill();
        }
        finally
        {
            watch.Stop();
            var name = desktop?.Name;
            desktop?.Dispose();
            if (name is not null)
            {
                await Task.Delay(100);
                desktopGone = DesktopExists(name) ? "no" : "yes";
            }

            if (run is not null)
            {
                await run.Process.WaitForTreeAsync(TimeSpan.FromSeconds(10));
                run.Dispose();
            }
        }

        var started = run?.StartedProcesses ?? 0;
        var ok = failure.Length == 0 && watch.ForegroundHits == 0 && watch.VisibleOnInputDesktop == (hidden ? 0 : watch.VisibleOnInputDesktop);
        var line = string.Join(',', index, ok ? "ok" : "FAIL", F(times[0]), F(times[1]), F(times[2]), F(times[3]), F(times[4]), exit, F(times[5]), started, watch.ForegroundHits, watch.VisibleOnInputDesktop, desktopGone, headingType, headingAria, failure);
        return (ok, line, times);
    }

    private static string F(double value) => value.ToString("0", CultureInfo.InvariantCulture);

    private Run Launch(out HiddenDesktop? desktop)
    {
        var run = Prepare(out desktop, out _);
        run.Start(desktop);
        return run;
    }

    private Run Prepare(out HiddenDesktop? desktop, out string root)
    {
        var index = Interlocked.Increment(ref _launch);
        root = Path.Combine(Repository, "artifacts", "blackbox-spike", Stamp, $"{Mode}-{index:D3}");
        desktop = hidden ? HiddenDesktop.Create($"spar-bb-spike-{Environment.ProcessId}-{index}") : null;
        return new Run(root, Executable);
    }

    private static async Task WaitReadyAsync(Run run)
    {
        var log = Path.Combine(run.Root, "LocalAppData", AppId, "logs", "app.log");
        var ready = await Poll(TimeSpan.FromSeconds(20), () => Task.FromResult(ReadyIn(log) ? (bool?)true : null));
        if (ready is null)
        {
            throw new TimeoutException($"no ui-ready line in 20 s; exit {run.Process.ExitCode}");
        }
    }

    private static bool ReadyIn(string log)
    {
        if (!File.Exists(log))
        {
            return false;
        }

        using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return ReadyLine().IsMatch(reader.ReadToEnd());
    }

    private static async Task<string> WaitWindowAsync(AutomationThread automation, int pid)
    {
        var title = await Poll(TimeSpan.FromSeconds(10), () => automation.RunAsync(uia => TopWindow(uia, pid)?.Name));
        return title ?? throw new TimeoutException("UI Automation did not find the window");
    }

    private static async Task<(string Type, string Aria)> WaitNamedAsync(AutomationThread automation, int pid, string name)
    {
        var found = await Poll(TimeSpan.FromSeconds(10), () => automation.RunAsync(uia =>
        {
            var top = TopWindow(uia, pid);
            var element = top is null ? null : uia.FindFirst(top, Uia.ScopeDescendants, uia.Equal(Uia.NameProperty, name));
            return element is null ? ((string, string)?)null : (element.LocalizedControlType, element.AriaRole);
        }));
        return found ?? throw new TimeoutException($"UI Automation did not find '{name}'");
    }

    private static UiaElement? TopWindow(Uia uia, int pid) =>
        TopWindows.Of(pid).Where(w => !w.IsDialog && w.Owner == 0).Select(w => uia.FromHandle(w.Handle)).FirstOrDefault(e => e?.ControlType == UiaElement.WindowType);

    /// <summary>A dialog's own buttons: every button but those of its title bar.</summary>
    private static List<UiaElement> DialogButtons(Uia uia, UiaElement dialog)
    {
        var buttons = new List<UiaElement>();
        foreach (var child in uia.FindAll(dialog, Uia.ScopeChildren, uia.True()))
        {
            if (child.ControlType == 50037)
            {
                continue;
            }

            if (child.ControlType == UiaElement.ButtonType)
            {
                buttons.Add(child);
            }

            buttons.AddRange(uia.FindAll(child, Uia.ScopeDescendants, uia.Equal(Uia.ControlTypeProperty, UiaElement.ButtonType)));
        }

        return buttons;
    }

    private static async Task<int> CloseAsync(AutomationThread automation, Run run)
    {
        await automation.RunAsync(uia =>
        {
            var top = TopWindow(uia, run.Process.ProcessId) ?? throw new InvalidOperationException("no window to close");
            uia.Pattern<IUIAutomationWindowPattern>(top, Uia.WindowPattern)!.Close();
            return 0;
        });
        return await run.Process.Exited.WaitAsync(TimeSpan.FromSeconds(15));
    }

    private static async Task<T?> Poll<T>(TimeSpan limit, Func<Task<T?>> probe)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < limit)
        {
            try
            {
                if (await probe() is { } value)
                {
                    return value;
                }
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // An element went away between finding and reading: look again.
            }

            await Task.Delay(20);
        }

        return default;
    }

    private static unsafe bool DesktopExists(string name)
    {
        fixed (char* text = name)
        {
            var handle = PInvoke.OpenDesktop(text, 0, false, 0x0001);
            if (handle.IsNull)
            {
                return false;
            }

            PInvoke.CloseDesktop(handle);
            return true;
        }
    }

    private static string Redact(string text)
    {
        var profile = Environment.GetEnvironmentVariable("USERPROFILE") ?? "\0";
        return text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Spar.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("run from inside the repository");
    }

    [GeneratedRegex(@"^\S+ INFO  Spar\.Host  \[[0-9a-f]{8}\] ui-ready at [0-9.]+ ms\r?$", RegexOptions.Multiline)]
    private static partial Regex ReadyLine();
}

/// <summary>One launch: its disposable root, environment and process.</summary>
[SupportedOSPlatform("windows10.0.19045")]
internal sealed class Run(string root, string executable) : IDisposable
{
    private AppProcess? _process;

    public string Root { get; } = root;

    public AppProcess Process => _process!;

    public int StartedProcesses { get; private set; }

    public void Start(HiddenDesktop? desktop)
    {
        string[] folders = ["LocalAppData", "AppData", "Temp"];
        foreach (var folder in folders)
        {
            Directory.CreateDirectory(Path.Combine(Root, folder));
        }

        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var name = (string)entry.Key;
            if (!name.StartsWith("WEBVIEW2_", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("SPAR_", StringComparison.OrdinalIgnoreCase))
            {
                environment[name] = (string)entry.Value!;
            }
        }

        environment["LOCALAPPDATA"] = Path.Combine(Root, "LocalAppData");
        environment["APPDATA"] = Path.Combine(Root, "AppData");
        environment["TEMP"] = Path.Combine(Root, "Temp");
        environment["TMP"] = Path.Combine(Root, "Temp");

        // SW_SHOWNOACTIVATE (4) for the fallback; on the hidden desktop the app shows itself as it would.
        _process = AppProcess.Start(executable, [], environment, Root, desktop?.Name, desktop is null ? (ushort)4 : null);
    }

    public void Dispose()
    {
        if (_process is null)
        {
            return;
        }

        StartedProcesses = _process.StartedProcesses;
        _process.Dispose();
        for (var attempt = 0; attempt < 20 && Directory.Exists(Root); attempt++)
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(100);
            }
        }
    }
}

/// <summary>
/// Watches the input desktop (the spike's own) every 10 ms while an app runs: whether the foreground window ever
/// belongs to one of the app's processes, and whether any visible top-level window of them is there. Reads only.
/// </summary>
[SupportedOSPlatform("windows10.0.19045")]
internal sealed class FocusWatch
{
    private readonly CancellationTokenSource _stop = new();
    private Thread? _thread;

    public int ForegroundHits { get; private set; }

    public int VisibleOnInputDesktop { get; private set; }

    public void Start(AppProcess process)
    {
        _thread = new Thread(() => Watch(process)) { IsBackground = true };
        _thread.Start();
    }

    public void Stop()
    {
        _stop.Cancel();
        _thread?.Join();
    }

    private unsafe void Watch(AppProcess process)
    {
        var seen = new HashSet<nint>();
        while (!_stop.IsCancellationRequested)
        {
            var pids = process.ProcessIds.ToHashSet();
            uint owner;
            var foreground = PInvoke.GetForegroundWindow();
            PInvoke.GetWindowThreadProcessId(foreground, &owner);
            if (pids.Contains((int)owner))
            {
                ForegroundHits++;
            }

            var windows = new List<(nint, uint)>();
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(windows);
            PInvoke.EnumWindows(&Collect, (nint)handle);
            handle.Free();
            foreach (var (window, pid) in windows)
            {
                if (pids.Contains((int)pid) && PInvoke.IsWindowVisible((HWND)window) && seen.Add(window))
                {
                    VisibleOnInputDesktop++;
                }
            }

            Thread.Sleep(10);
        }
    }

    [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static unsafe BOOL Collect(HWND window, LPARAM state)
    {
        uint pid;
        PInvoke.GetWindowThreadProcessId(window, &pid);
        var list = (List<(nint, uint)>)System.Runtime.InteropServices.GCHandle.FromIntPtr(state.Value).Target!;
        list.Add((window, pid));
        return true;
    }
}
