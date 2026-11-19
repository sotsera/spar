#:property AllowUnsafeBlocks=true
#:property PublishAot=false
#:property Nullable=enable
#:property TreatWarningsAsErrors=false

// The M1b start-up probe: starts a Spar app's executable with LOCALAPPDATA in a disposable root under artifacts/,
// reads its standard error, finds its window and dialogs by process, captures early frames with PrintWindow from this
// process, closes dialogs with WM_CLOSE and the app with WM_CLOSE, suspends a primary for the "not responding" case,
// and measures memory. It sends no input. Usage: dotnet run Probe.cs -- <command> [options]; see Main.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

static class Probe
{
    static int Main(string[] argv)
    {
        var o = Options.Parse(argv);
        return o.Command switch
        {
            "run" => Run(o),
            "relaunch" => Relaunch(o),
            "hung" => Hung(o),
            "frames" => Frames(o),
            "measure" => Measure(o),
            _ => Usage(),
        };
    }

    static int Usage()
    {
        Console.Error.WriteLine("commands: run | relaunch | hung | frames | measure; options: --exe --root --env K=V --arg A --label L --runs N --capture MS --close-dialog");
        return 2;
    }

    // ---------- one run ----------

    sealed record Result(int Exit, double ExitMs, double? WindowMs, double? DialogMs, string? DialogTitle, string Stderr, double? ReadyMs, List<Frame> FrameList, long? AppPrivate, long? WebViewPrivate, int WebViewProcesses);

    static Result RunOnce(Options o, Action<Process, Stopwatch>? whileRunning = null)
    {
        Directory.CreateDirectory(o.Root);
        var start = new ProcessStartInfo(o.Exe) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
        foreach (var a in o.Args) start.ArgumentList.Add(a);
        start.Environment["LOCALAPPDATA"] = o.Root;
        foreach (var (k, v) in o.Env) start.Environment[k] = v;
        var clock = Stopwatch.StartNew();
        using var p = Process.Start(start)!;
        var err = new StringBuilder();
        double? readyMs = null;
        p.ErrorDataReceived += (_, e) => { if (e.Data is null) return; lock (err) { err.AppendLine(e.Data); if (readyMs is null && e.Data.Contains("ui-ready")) readyMs = clock.Elapsed.TotalMilliseconds; } };
        p.OutputDataReceived += (_, e) => { if (e.Data is null) return; lock (err) err.AppendLine("out: " + e.Data); };
        p.BeginErrorReadLine();
        p.BeginOutputReadLine();

        double? windowMs = null, dialogMs = null;
        string? dialogTitle = null;
        var frames = new List<Frame>();
        nint window = 0;
        long? appPrivate = null, webPrivate = null;
        var webCount = 0;
        var closed = false;
        var deadline = TimeSpan.FromSeconds(o.Timeout);
        while (!p.HasExited && clock.Elapsed < deadline)
        {
            if (window == 0)
            {
                window = FindWindow((uint)p.Id, "Spar.Window");
                if (window != 0)
                {
                    windowMs = clock.Elapsed.TotalMilliseconds;
                    if (o.Capture > 0) frames = Capture(window, clock, o.Capture, o.Label, o.Root);
                }
            }

            var dialog = FindWindow((uint)p.Id, "#32770");
            if (dialog != 0 && dialogMs is null)
            {
                dialogMs = clock.Elapsed.TotalMilliseconds;
                Thread.Sleep(300);
                dialogTitle = Title(dialog);
                if (o.Shot is not null) SaveBmp(dialog, o.Shot);
                if (o.CloseDialog) PostMessage(dialog, WM_CLOSE, 0, 0);
            }

            if (readyMs is null && o.ReadyAfterWindow > 0 && windowMs is not null && clock.Elapsed.TotalMilliseconds > windowMs + o.ReadyAfterWindow) readyMs = clock.Elapsed.TotalMilliseconds;
            if (readyMs is not null && !closed)
            {
                if (whileRunning is not null) { whileRunning(p, clock); whileRunning = null; }
                if (o.Memory)
                {
                    Thread.Sleep(2000);
                    (appPrivate, webPrivate, webCount) = Memory(p.Id);
                }
                if (o.Keep) break;
                Thread.Sleep(o.CloseAfter);
                if (o.Shot is not null && window != 0) SaveBmp(window, o.Shot);
                PostMessage(window, WM_CLOSE, 0, 0);
                closed = true;
            }

            Thread.Sleep(2);
        }

        if (o.Keep && !p.HasExited) return new Result(-1, 0, windowMs, dialogMs, dialogTitle, err.ToString(), readyMs, frames, appPrivate, webPrivate, webCount);
        if (!p.WaitForExit(30000)) { p.Kill(true); Console.Error.WriteLine("probe: killed after timeout"); }
        p.WaitForExit();
        var exitMs = clock.Elapsed.TotalMilliseconds;
        lock (err) return new Result(p.ExitCode, exitMs, windowMs, dialogMs, dialogTitle, err.ToString(), readyMs, frames, appPrivate, webPrivate, webCount);
    }

    static void Print(string label, Result r)
    {
        Console.WriteLine($"[{label}] exit {r.Exit} after {r.ExitMs:F0} ms; window {Fmt(r.WindowMs)}; dialog {Fmt(r.DialogMs)} '{r.DialogTitle}'; ui-ready {Fmt(r.ReadyMs)}");
        foreach (var line in r.Stderr.Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0)) Console.WriteLine("    " + line);
    }

    static string Fmt(double? v) => v is null ? "-" : $"{v:F0} ms";

    static int Run(Options o)
    {
        var r = RunOnce(o);
        Print(o.Label, r);
        return 0;
    }

    // ---------- relaunch: a second launch hands its arguments to the first ----------

    static int Relaunch(Options o)
    {
        Result? second = null;
        var secondMs = 0.0;
        bool iconicBefore = false, iconicAfter = false;
        nint foregroundAfter = 0, window = 0;
        var primary = RunOnce(o with { Args = [] }, (p, clock) =>
        {
            window = FindWindow((uint)p.Id, "Spar.Window");
            if (o.Minimize) { ShowWindow(window, 6 /* SW_MINIMIZE */); Thread.Sleep(300); }
            iconicBefore = IsIconic(window);
            var sw = Stopwatch.StartNew();
            second = RunOnce(o with { Capture = 0, CloseDialog = true, Shot = null, Memory = false, Timeout = 20 });
            secondMs = sw.Elapsed.TotalMilliseconds;
            Thread.Sleep(500);
            iconicAfter = IsIconic(window);
            foregroundAfter = GetForegroundWindow();
        });
        Print("second", second!);
        Console.WriteLine($"second launch: exit {second!.Exit} in {secondMs:F0} ms (start to exit, from this probe)");
        Console.WriteLine($"primary window minimized before {iconicBefore}, after {iconicAfter}; foreground is primary: {foregroundAfter == window}");
        Print("primary", primary);
        return 0;
    }

    // ---------- hung: the primary suspended, a second launch must report "not responding" ----------

    static int Hung(Options o)
    {
        Result? second = null;
        var primary = RunOnce(o with { Args = [], Shot = null }, (p, clock) =>
        {
            NtSuspendProcess(p.Handle);
            try
            {
                second = RunOnce(o with { Capture = 0, CloseDialog = true, Memory = false, Timeout = 30 });
            }
            finally
            {
                NtResumeProcess(p.Handle);
            }
        });
        Print("second (primary suspended)", second!);
        Print("primary", primary);
        return 0;
    }

    // ---------- frames: early frames of the window ----------

    static int Frames(Options o)
    {
        for (var run = 0; run < o.Runs; run++)
        {
            var r = RunOnce(o with { Label = $"{o.Label}-{run}" });
            Console.WriteLine($"[{o.Label} run {run}] exit {r.Exit}, window {Fmt(r.WindowMs)}, ui-ready {Fmt(r.ReadyMs)}, {r.FrameList.Count} frames");
            Console.WriteLine("    " + Summarize(r.FrameList));
            Console.WriteLine($"    frames with any white (>=#fafafa) client pixels: {r.FrameList.Count(f => f.White > 0)}, with over 1%: {r.FrameList.Count(f => f.White > 0.01)}, max {r.FrameList.DefaultIfEmpty().Max(f => f?.White ?? 0):P1}; with #121212 over 1%: {r.FrameList.Count(f => f.Canvas > 0.01)}; with dark (<#404040) over 1%: {r.FrameList.Count(f => f.Dark > 0.01)}; empty prints: {r.FrameList.Count(f => f.Empty)}");
        }
        return 0;
    }

    // ---------- measure: start-up time and memory, several runs ----------

    static int Measure(Options o)
    {
        var ready = new List<double>();
        var app = new List<double>();
        var web = new List<double>();
        for (var run = 0; run < o.Runs; run++)
        {
            var r = RunOnce(o with { Memory = true });
            var phases = Phases(r.Stderr);
            var readyHost = phases.GetValueOrDefault("ui-ready");
            ready.Add(readyHost);
            app.Add((r.AppPrivate ?? 0) / 1048576.0);
            web.Add((r.WebViewPrivate ?? 0) / 1048576.0);
            Console.WriteLine($"[{o.Label} {run}] exit {r.Exit}; single instance {phases.GetValueOrDefault("single instance"):F1}, checks {phases.GetValueOrDefault("prerequisites checked"):F1}, window {phases.GetValueOrDefault("window shown"):F1}, host ui-ready {readyHost:F1} ms, controller {phases.GetValueOrDefault("controller"):F1}, first document {phases.GetValueOrDefault("first document"):F1}; private working set app {app[^1]:F1} MB, {r.WebViewProcesses} WebView2 processes {web[^1]:F1} MB");
        }
        Console.WriteLine($"[{o.Label}] median ui-ready {Median(ready):F1} ms ({ready.Min():F1}-{ready.Max():F1}); app {Median(app):F1} MB; WebView2 {Median(web):F1} MB ({web.Min():F1}-{web.Max():F1})");
        return 0;
    }

    static double Median(List<double> values)
    {
        var s = values.OrderBy(v => v).ToList();
        return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
    }

    static Dictionary<string, double> Phases(string stderr)
    {
        var d = new Dictionary<string, double>();
        foreach (Match m in Regex.Matches(stderr, @"\] (?<p>[^\]\r\n]+?) at (?<ms>[0-9.]+) ms"))
            d.TryAdd(m.Groups["p"].Value, double.Parse(m.Groups["ms"].Value, System.Globalization.CultureInfo.InvariantCulture));
        return d;
    }

    // ---------- frames ----------

    sealed record Frame(double Ms, uint Client, uint Title, bool Empty, double White, double Canvas, double Dark);

    static List<Frame> Capture(nint window, Stopwatch clock, int durationMs, string label, string root)
    {
        var frames = new List<Frame>();
        var until = clock.Elapsed.TotalMilliseconds + durationMs;
        var dir = Path.Combine(root, "frames", label);
        Directory.CreateDirectory(dir);
        uint lastClient = 1, lastTitle = 1;
        while (clock.Elapsed.TotalMilliseconds < until)
        {
            var t = clock.Elapsed.TotalMilliseconds;
            var shot = Grab(window);
            if (shot is null) { Thread.Sleep(16); continue; }
            var (w, h, px, clientTop) = shot.Value;
            uint client = px[(clientTop + (h - clientTop) / 2) * w + w / 2] & 0xFFFFFF;
            uint title = px[(clientTop / 2) * w + w / 2] & 0xFFFFFF;
            bool empty = px.All(v => (v & 0xFFFFFF) == 0);
            int white = 0, canvas = 0, dark = 0, total = 0;
            for (var y = clientTop; y < h - 8; y += 4) for (var x = 8; x < w - 8; x += 4) { var c = px[y * w + x] & 0xFFFFFF; total++; if (((c >> 16) & 0xFF) >= 0xFA && ((c >> 8) & 0xFF) >= 0xFA && (c & 0xFF) >= 0xFA) white++; if (c == 0x121212) canvas++; if (((c >> 16) & 0xFF) < 0x40 && ((c >> 8) & 0xFF) < 0x40 && (c & 0xFF) < 0x40) dark++; }
            frames.Add(new Frame(t, client, title, empty, white / (double)total, canvas / (double)total, dark / (double)total));
            if (client != lastClient || title != lastTitle)
            {
                WriteBmp(Path.Combine(dir, $"{t:00000}-{client:x6}-{title:x6}.bmp"), w, h, px);
                lastClient = client; lastTitle = title;
            }
            var next = t + 16.7;
            while (clock.Elapsed.TotalMilliseconds < next) Thread.SpinWait(200);
        }
        return frames;
    }

    static string Summarize(List<Frame> frames)
    {
        var sb = new StringBuilder();
        Frame? runStart = null; int count = 0;
        foreach (var f in frames)
        {
            if (runStart is not null && (f.Client == runStart.Client && f.Title == runStart.Title && f.Empty == runStart.Empty)) { count++; continue; }
            if (runStart is not null) sb.Append($"{(runStart.Empty ? "EMPTY" : $"client #{Rgb(runStart.Client)} title #{Rgb(runStart.Title)}")} x{count} from {runStart.Ms:F0}; ");
            runStart = f; count = 1;
        }
        if (runStart is not null) sb.Append($"{(runStart.Empty ? "EMPTY" : $"client #{Rgb(runStart.Client)} title #{Rgb(runStart.Title)}")} x{count} from {runStart.Ms:F0}");
        return sb.ToString();
    }

    static string Rgb(uint bgra) => $"{(bgra >> 16) & 0xFF:x2}{(bgra >> 8) & 0xFF:x2}{bgra & 0xFF:x2}";

    static unsafe (int W, int H, uint[] Px, int ClientTop)? Grab(nint window)
    {
        if (!GetWindowRect(window, out var r)) return null;
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        if (w <= 0 || h <= 0) return null;
        var point = new POINT();
        ClientToScreen(window, ref point);
        var clientTop = Math.Clamp(point.Y - r.Top, 1, h - 1);
        var screen = GetDC(0);
        var dc = CreateCompatibleDC(screen);
        var bmi = new BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        var bmp = CreateDIBSection(dc, ref bmi, 0, out var bits, 0, 0);
        var old = SelectObject(dc, bmp);
        var ok = PrintWindow(window, dc, 2 /* PW_RENDERFULLCONTENT */);
        var px = new uint[w * h];
        new ReadOnlySpan<uint>((void*)bits, w * h).CopyTo(px);
        SelectObject(dc, old);
        DeleteObject(bmp);
        DeleteDC(dc);
        ReleaseDC(0, screen);
        return ok ? (w, h, px, clientTop) : null;
    }

    static void SaveBmp(nint window, string path)
    {
        var g = Grab(window);
        if (g is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        WriteBmp(path, g.Value.W, g.Value.H, g.Value.Px);
    }

    static void WriteBmp(string path, int w, int h, uint[] px)
    {
        using var f = File.Create(path);
        using var b = new BinaryWriter(f);
        var size = 54 + w * h * 4;
        b.Write((ushort)0x4D42); b.Write(size); b.Write(0); b.Write(54);
        b.Write(40); b.Write(w); b.Write(-h); b.Write((ushort)1); b.Write((ushort)32); b.Write(0); b.Write(w * h * 4); b.Write(2835); b.Write(2835); b.Write(0); b.Write(0);
        foreach (var v in px) b.Write(v | 0xFF000000);
    }

    // ---------- memory ----------

    static (long App, long Web, int Count) Memory(int pid)
    {
        var parents = new Dictionary<int, int>();
        var snapshot = CreateToolhelp32Snapshot(2, 0);
        var entry = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
        if (Process32FirstW(snapshot, ref entry))
            do { parents[(int)entry.th32ProcessID] = (int)entry.th32ParentProcessID; } while (Process32NextW(snapshot, ref entry));
        CloseHandle(snapshot);
        bool Descends(int id) { for (var i = 0; i < 10 && parents.TryGetValue(id, out var parent); i++) { if (parent == pid) return true; id = parent; } return false; }
        long web = 0; var count = 0;
        foreach (var proc in Process.GetProcessesByName("msedgewebview2"))
        {
            if (!Descends(proc.Id)) continue;
            web += PrivateWorkingSet(proc.Id); count++;
        }
        return (PrivateWorkingSet(pid), web, count);
    }

    static long PrivateWorkingSet(int pid)
    {
        var h = OpenProcess(0x1000 | 0x0010, false, (uint)pid);
        if (h == 0) return 0;
        var c = new PROCESS_MEMORY_COUNTERS_EX2 { cb = (uint)Marshal.SizeOf<PROCESS_MEMORY_COUNTERS_EX2>() };
        var ok = GetProcessMemoryInfo(h, ref c, c.cb);
        CloseHandle(h);
        return ok ? (long)c.PrivateWorkingSetSize : 0;
    }

    // ---------- windows ----------

    static nint FindWindow(uint pid, string className)
    {
        nint found = 0;
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out var owner);
            if (owner != pid || !IsWindowVisible(h)) return true;
            var sb = new StringBuilder(256);
            GetClassName(h, sb, 256);
            if (sb.ToString() == className) { found = h; return false; }
            return true;
        }, 0);
        return found;
    }

    static string Title(nint h) { var sb = new StringBuilder(512); GetWindowText(h, sb, 512); return sb.ToString(); }

    // ---------- options ----------

    sealed record Options(string Command, string Exe, string Root, List<string> Args, List<(string, string)> Env, string Label, int Runs, int Capture, bool CloseDialog, string? Shot, bool Memory, bool Keep, int CloseAfter, int Timeout, bool Minimize, int ReadyAfterWindow = 0)
    {
        public static Options Parse(string[] a)
        {
            var o = new Options(a.Length > 0 ? a[0] : "", "", "", [], [], "run", 1, 0, false, null, false, false, 300, 60, false);
            for (var i = 1; i < a.Length; i++)
            {
                o = a[i] switch
                {
                    "--exe" => o with { Exe = a[++i] },
                    "--root" => o with { Root = Path.GetFullPath(a[++i]) },
                    "--arg" => o with { Args = [.. o.Args, a[++i]] },
                    "--env" => o with { Env = [.. o.Env, (a[++i].Split('=', 2)[0], a[i].Split('=', 2)[1])] },
                    "--label" => o with { Label = a[++i] },
                    "--runs" => o with { Runs = int.Parse(a[++i]) },
                    "--capture" => o with { Capture = int.Parse(a[++i]) },
                    "--close-dialog" => o with { CloseDialog = true },
                    "--shot" => o with { Shot = a[++i] },
                    "--memory" => o with { Memory = true },
                    "--keep" => o with { Keep = true },
                    "--close-after" => o with { CloseAfter = int.Parse(a[++i]) },
                    "--timeout" => o with { Timeout = int.Parse(a[++i]) },
                    "--minimize" => o with { Minimize = true },
                    "--ready-after-window" => o with { ReadyAfterWindow = int.Parse(a[++i]) },
                    _ => throw new ArgumentException(a[i]),
                };
            }
            return o;
        }
    }

    // ---------- interop ----------

    const uint WM_CLOSE = 0x0010;
    delegate bool EnumProc(nint h, nint l);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct BITMAPINFOHEADER { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct PROCESSENTRY32W { public uint dwSize, cntUsage, th32ProcessID; public nint th32DefaultHeapID; public uint th32ModuleID, cntThreads, th32ParentProcessID; public int pcPriClassBase; public uint dwFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile; }
    [StructLayout(LayoutKind.Sequential)] struct PROCESS_MEMORY_COUNTERS_EX2 { public uint cb, PageFaultCount; public nuint PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage, QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage, PrivateUsage, PrivateWorkingSetSize; public ulong SharedCommitUsage; }

    [DllImport("user32")] static extern bool EnumWindows(EnumProc f, nint l);
    [DllImport("user32")] static extern uint GetWindowThreadProcessId(nint h, out uint pid);
    [DllImport("user32", CharSet = CharSet.Unicode)] static extern int GetClassName(nint h, StringBuilder sb, int n);
    [DllImport("user32", CharSet = CharSet.Unicode)] static extern int GetWindowText(nint h, StringBuilder sb, int n);
    [DllImport("user32")] static extern bool IsWindowVisible(nint h);
    [DllImport("user32")] static extern bool IsIconic(nint h);
    [DllImport("user32")] static extern bool ShowWindow(nint h, int cmd);
    [DllImport("user32")] static extern nint GetForegroundWindow();
    [DllImport("user32")] static extern bool PostMessage(nint h, uint m, nint w, nint l);
    [DllImport("user32")] static extern bool GetWindowRect(nint h, out RECT r);
    [DllImport("user32")] static extern bool ClientToScreen(nint h, ref POINT p);
    [DllImport("user32")] static extern bool PrintWindow(nint h, nint dc, uint flags);
    [DllImport("user32")] static extern nint GetDC(nint h);
    [DllImport("user32")] static extern int ReleaseDC(nint h, nint dc);
    [DllImport("gdi32")] static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32")] static extern bool DeleteDC(nint dc);
    [DllImport("gdi32")] static extern nint CreateDIBSection(nint dc, ref BITMAPINFOHEADER bmi, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32")] static extern nint SelectObject(nint dc, nint o);
    [DllImport("gdi32")] static extern bool DeleteObject(nint o);
    [DllImport("ntdll")] static extern int NtSuspendProcess(nint process);
    [DllImport("ntdll")] static extern int NtResumeProcess(nint process);
    [DllImport("kernel32")] static extern nint CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern bool Process32FirstW(nint s, ref PROCESSENTRY32W e);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern bool Process32NextW(nint s, ref PROCESSENTRY32W e);
    [DllImport("kernel32")] static extern bool CloseHandle(nint h);
    [DllImport("kernel32")] static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("psapi")] static extern bool GetProcessMemoryInfo(nint p, ref PROCESS_MEMORY_COUNTERS_EX2 c, uint cb);
}
