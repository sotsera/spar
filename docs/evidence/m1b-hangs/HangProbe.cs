#:property AllowUnsafeBlocks=true
#:property PublishAot=false
#:property Nullable=enable
#:property TreatWarningsAsErrors=false

// The M1b hangs probe: starts a Spar app's executable with LOCALAPPDATA in a disposable root under artifacts/, reads
// its standard error with the probe's own clock, and runs a script of steps against it: wait for a log line, find the
// app's dialogs by process, press their buttons with TDM_CLICK_BUTTON, post WM_CLOSE to the window, suspend and resume
// the app's renderer processes (NtSuspendProcess, found by their --user-data-dir under the root) or its UI thread
// (SuspendThread), and wait for the exit. It sends no mouse or keyboard input. Usage:
//   dotnet run HangProbe.cs -- --exe <path> --root <dir> [--env K=V]... --log <file> -- <step> <step> ...
// Steps: wait:<text>[:<seconds>]  absent:<text>[:<seconds>]  sleep:<ms>  dialog[:<seconds>]  no-dialog[:<seconds>]
//        press:<button id>  gone[:<seconds>]  close
//        suspend-renderer  resume-renderer  stall-ui:<ms>  exit[:<seconds>]  mark:<label>
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

static class HangProbe
{
    static readonly Stopwatch Clock = Stopwatch.StartNew();
    static readonly List<string> Lines = [];
    static StreamWriter? s_log;

    static int Main(string[] argv)
    {
        var split = Array.IndexOf(argv, "--");
        var options = argv[..split];
        var steps = argv[(split + 1)..];
        string exe = "", root = "", logPath = "";
        var env = new List<(string, string)>();
        for (var i = 0; i < options.Length; i += 2)
        {
            switch (options[i])
            {
                case "--exe": exe = options[i + 1]; break;
                case "--root": root = options[i + 1]; break;
                case "--log": logPath = options[i + 1]; break;
                case "--env":
                    var kv = options[i + 1].Split('=', 2);
                    env.Add((kv[0], kv[1]));
                    break;
            }
        }

        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(logPath))!);
        s_log = new StreamWriter(logPath, append: false) { AutoFlush = true };
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
        start.Environment["LOCALAPPDATA"] = root;
        foreach (var (k, v) in env)
        {
            start.Environment[k] = v;
        }

        Note($"start {Path.GetFileName(exe)} env [{string.Join(" ", env.Select(e => e.Item1 + "=" + e.Item2))}]");
        using var app = Process.Start(start)!;
        app.ErrorDataReceived += (_, e) => Line(e.Data);
        app.OutputDataReceived += (_, e) => Line(e.Data is null ? null : "out: " + e.Data);
        app.BeginErrorReadLine();
        app.BeginOutputReadLine();

        var failed = false;
        nint dialog = 0;
        foreach (var step in steps)
        {
            var parts = step.Split(':', 2);
            var arg = parts.Length > 1 ? parts[1] : "";
            try
            {
                switch (parts[0])
                {
                    case "wait":
                        var (text, seconds) = TextAndSeconds(arg, 60);
                        if (!WaitForLine(text, seconds, app)) { Note($"TIMEOUT waiting for '{text}'"); failed = true; }
                        else Note($"seen '{text}'");
                        break;
                    case "absent":
                        var (missing, within) = TextAndSeconds(arg, 10);
                        if (WaitForLine(missing, within, app)) { Note($"UNEXPECTED '{missing}'"); failed = true; }
                        else Note($"not seen '{missing}' within {within} s (as expected)");
                        break;
                    case "sleep":
                        Thread.Sleep(int.Parse(arg));
                        break;
                    case "dialog":
                        dialog = WaitFor(() => FindWindow((uint)app.Id, "#32770"), Seconds(arg, 60), app);
                        if (dialog == 0) { Note("TIMEOUT waiting for a dialog"); failed = true; }
                        else Note($"dialog '{Title(dialog)}' owner {(GetWindow(dialog, 4) == 0 ? "none" : "the app's window")}");
                        break;
                    case "press":
                        // Found again at the press: a dialog replaced meanwhile (the hung-page dialog becoming the close one) has gone.
                        dialog = FindWindow((uint)app.Id, "#32770");
                        Note($"press {arg} on '{Title(dialog)}'");
                        PostMessageW(dialog, 0x0400 + 102, (nint)int.Parse(arg), 0);
                        break;
                    case "gone":
                        var gone = WaitFor(() => FindWindow((uint)app.Id, "#32770") == 0 ? 1 : 0, Seconds(arg, 60), app);
                        if (gone == 0 && app.HasExited) gone = 1;
                        Note(gone != 0 ? (app.HasExited ? "dialog gone with the process" : "dialog gone") : "TIMEOUT: the dialog is still there");
                        failed |= gone == 0;
                        dialog = 0;
                        break;
                    case "no-dialog":
                        var appeared = WaitFor(() => FindWindow((uint)app.Id, "#32770"), Seconds(arg, 10), app);
                        Note(appeared == 0 ? "no dialog (as expected)" : $"UNEXPECTED dialog '{Title(appeared)}'");
                        failed |= appeared != 0;
                        break;
                    case "close":
                        var window = FindWindow((uint)app.Id, "Spar.Window");
                        Note("WM_CLOSE to the window");
                        PostMessageW(window, 0x0010, 0, 0);
                        break;
                    case "suspend-renderer":
                        foreach (var pid in Renderers(root)) { Note($"suspend renderer {pid}"); Suspend(pid, true); }
                        break;
                    case "resume-renderer":
                        foreach (var pid in Renderers(root)) { Note($"resume renderer {pid}"); Suspend(pid, false); }
                        break;
                    case "stall-ui":
                        StallUi(app, int.Parse(arg));
                        break;
                    case "exit":
                        if (!app.WaitForExit(Seconds(arg, 60) * 1000)) { Note("TIMEOUT waiting for the exit"); failed = true; }
                        else { app.WaitForExit(); Note($"exit {app.ExitCode}"); }
                        break;
                    case "mark":
                        Note("mark " + arg);
                        break;
                    default:
                        Note("unknown step " + step);
                        failed = true;
                        break;
                }
            }
            catch (Exception exception)
            {
                Note($"step {step} failed: {exception.Message}");
                failed = true;
            }
        }

        if (!app.HasExited)
        {
            Note("the app is still running: WM_CLOSE");
            PostMessageW(FindWindow((uint)app.Id, "Spar.Window"), 0x0010, 0, 0);
            if (!app.WaitForExit(20_000))
            {
                Note("KILL (still running 20 s after WM_CLOSE)");
                app.Kill(entireProcessTree: true);
            }
        }

        // The browser exits on its own after the app (up to about 6.5 s when SmartScreen is still busy, pitfall).
        var exited = Clock.Elapsed;
        var left = Renderers(root, anyType: true);
        while (left.Count > 0 && Clock.Elapsed - exited < TimeSpan.FromSeconds(15))
        {
            Thread.Sleep(250);
            left = Renderers(root, anyType: true);
        }

        Note($"browser processes left for this root {(Clock.Elapsed - exited).TotalSeconds:0.0} s after the exit: {left.Count}");
        failed |= left.Count > 0;
        Note(failed ? "RESULT FAIL" : "RESULT OK");
        return failed ? 1 : 0;
    }

    static (string, int) TextAndSeconds(string arg, int fallback)
    {
        var colon = arg.LastIndexOf(':');
        return colon > 0 && int.TryParse(arg[(colon + 1)..], out var s) ? (arg[..colon], s) : (arg, fallback);
    }

    static int Seconds(string arg, int fallback) => int.TryParse(arg, out var s) ? s : fallback;

    static void Line(string? text)
    {
        if (text is null) return;
        lock (Lines)
        {
            Lines.Add(text);
            s_log!.WriteLine($"{Clock.Elapsed.TotalMilliseconds,10:0.0} | {text}");
            Monitor.PulseAll(Lines);
        }
    }

    static void Note(string text)
    {
        lock (Lines)
        {
            var line = $"{Clock.Elapsed.TotalMilliseconds,10:0.0} > {text}";
            s_log!.WriteLine(line);
            Console.WriteLine(line);
        }
    }

    static int s_seen;

    /// <summary>Waits for a line after the last one a wait matched, so a repeated text is found again.</summary>
    static bool WaitForLine(string text, int seconds, Process app)
    {
        var deadline = Clock.Elapsed + TimeSpan.FromSeconds(seconds);
        lock (Lines)
        {
            while (true)
            {
                for (var i = s_seen; i < Lines.Count; i++)
                {
                    if (Lines[i].Contains(text, StringComparison.Ordinal))
                    {
                        s_seen = i + 1;
                        return true;
                    }
                }

                var left = deadline - Clock.Elapsed;
                if (left <= TimeSpan.Zero || (app.HasExited && !Monitor.Wait(Lines, 200))) return false;
                Monitor.Wait(Lines, left < TimeSpan.FromMilliseconds(200) ? left : TimeSpan.FromMilliseconds(200));
            }
        }
    }

    static nint WaitFor(Func<nint> find, int seconds, Process app)
    {
        var deadline = Clock.Elapsed + TimeSpan.FromSeconds(seconds);
        while (Clock.Elapsed < deadline && !app.HasExited)
        {
            var found = find();
            if (found != 0) return found;
            Thread.Sleep(10);
        }

        return 0;
    }

    // ---------- windows ----------

    static nint FindWindow(uint pid, string className)
    {
        nint found = 0;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner != pid || !IsWindowVisible(window)) return true;
            var name = new StringBuilder(256);
            GetClassNameW(window, name, name.Capacity);
            if (name.ToString() != className) return true;
            found = window;
            return false;
        }, 0);
        return found;
    }

    static string Title(nint window)
    {
        var text = new StringBuilder(512);
        GetWindowTextW(window, text, text.Capacity);
        return text.ToString();
    }

    // ---------- processes ----------

    /// <summary>The browser processes whose user data folder is under the run's root: renderers, or all of them.</summary>
    static List<int> Renderers(string root, bool anyType = false)
    {
        var ids = new List<int>();
        var full = Path.GetFullPath(root);
        foreach (var process in Process.GetProcessesByName("msedgewebview2"))
        {
            using (process)
            {
                var command = CommandLine(process.Id);
                if (command is null || !command.Contains(full, StringComparison.OrdinalIgnoreCase)) continue;
                if (anyType || command.Contains("--type=renderer", StringComparison.Ordinal)) ids.Add(process.Id);
            }
        }

        return ids;
    }

    static unsafe string? CommandLine(int pid)
    {
        var handle = OpenProcess(0x1000, false, (uint)pid);
        if (handle == 0) return null;
        try
        {
            var buffer = new byte[65536];
            fixed (byte* data = buffer)
            {
                if (NtQueryInformationProcess(handle, 60, data, buffer.Length, out _) != 0) return null;
                var length = *(ushort*)data;
                var text = *(char**)(data + 8);
                return new string(text, 0, length / 2);
            }
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    static void Suspend(int pid, bool suspend)
    {
        var handle = OpenProcess(0x0800, false, (uint)pid);
        if (handle == 0) { Note($"cannot open {pid}"); return; }
        try { _ = suspend ? NtSuspendProcess(handle) : NtResumeProcess(handle); }
        finally { CloseHandle(handle); }
    }

    /// <summary>Suspends the app's UI thread (the owner of its window) for a while: a stall from outside, in any build.</summary>
    static void StallUi(Process app, int milliseconds)
    {
        var window = FindWindow((uint)app.Id, "Spar.Window");
        var thread = GetWindowThreadProcessId(window, out _);
        var handle = OpenThread(0x0002, false, thread);
        Note($"suspend the UI thread {thread} for {milliseconds} ms");
        SuspendThread(handle);
        Thread.Sleep(milliseconds);
        ResumeThread(handle);
        CloseHandle(handle);
    }

    delegate bool EnumProc(nint window, nint data);

    [DllImport("user32")] static extern bool EnumWindows(EnumProc proc, nint data);
    [DllImport("user32")] static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32")] static extern bool IsWindowVisible(nint window);
    [DllImport("user32", CharSet = CharSet.Unicode)] static extern int GetClassNameW(nint window, StringBuilder name, int max);
    [DllImport("user32", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(nint window, StringBuilder text, int max);
    [DllImport("user32")] static extern nint GetWindow(nint window, uint command);
    [DllImport("user32")] static extern bool PostMessageW(nint window, uint message, nint wParam, nint lParam);
    [DllImport("kernel32")] static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32")] static extern nint OpenThread(uint access, bool inherit, uint tid);
    [DllImport("kernel32")] static extern uint SuspendThread(nint thread);
    [DllImport("kernel32")] static extern uint ResumeThread(nint thread);
    [DllImport("kernel32")] static extern bool CloseHandle(nint handle);
    [DllImport("ntdll")] static extern unsafe int NtQueryInformationProcess(nint process, int infoClass, void* info, int length, out int returned);
    [DllImport("ntdll")] static extern int NtSuspendProcess(nint process);
    [DllImport("ntdll")] static extern int NtResumeProcess(nint process);
}
