using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Titlebar.Desktop;
using Titlebar.Desktop.Interop;
using static Titlebar.Desktop.Interop.Native;

// --uia-dump <hwnd> <file> [raw]: read another window's UI Automation tree with the native client, then exit.
if (args.Length >= 3 && args[0] == "--uia-dump")
{
    return UiaDump.Run((nint)long.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture), args[2], args.Length > 3 && args[3] == "raw");
}

var options = Options.Parse(args);
var outFolder = Path.IsPathRooted(options.Out) ? options.Out : Path.Combine(Host.RepoRoot(), options.Out);
using var log = new Log(outFolder, options.Label);
log.Mark("start", ("options", options.Describe()), ("args", string.Join(' ', args)), ("pid", (long)Environment.ProcessId),
    ("processStartToMain", (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds));

Scenarios.LogMonitors(log);
var host = new Host(options, log);
var ready = new ManualResetEventSlim();
host.OnReady += ready.Set;
var stop = new CancellationTokenSource();
Thread? capture = null;
if (options.CaptureMs > 0)
{
    capture = new Thread(() => Scenarios.CaptureFrames(host, stop.Token)) { IsBackground = true, Name = "capture" };
    capture.Start();
}

var scenario = new Thread(() =>
{
    try
    {
        if (!ready.Wait(30000))
        {
            log.Mark("ready-timeout");
        }
        else
        {
            switch (options.Mode)
            {
                case "probe": Scenarios.RunProbe(host); break;
                case "live": Scenarios.RunLive(host); break;
                case "drive": Drive.Run(host); break;
                default: Thread.Sleep(options.AfterReadyMs); break;
            }
        }

        if (capture is not null)
        {
            Thread.Sleep(Math.Max(0, options.CaptureMs - (int)log.Now));
            stop.Cancel();
            capture.Join();
        }

        if (options.HoldMs > 0)
        {
            File.WriteAllText(Path.Combine(outFolder, options.Label + ".hold"), $"{Environment.ProcessId} {(long)host.Hwnd}");
            Thread.Sleep(options.HoldMs);
        }
    }
    catch (Exception e)
    {
        log.Mark("scenario-error", ("error", e.ToString()));
    }
    finally
    {
        log.Mark("hit-test-summary", ("counts", string.Join(' ', host.HitCounts.Select(kv => $"{Probe.Name(kv.Key)}={kv.Value}"))),
            ("us", Scenarios.Format(Probe.Stats(host.HitTestUs))), ("regionAtPointUs", Scenarios.Format(Probe.Stats(host.RegionAtPointUs))));
        if (!host.Destroyed)
        {
            host.Invoke(host.BeginClose);
        }
    }
})
{ IsBackground = true, Name = "scenario" };
scenario.Start();

// The window, the WebView and their cleanup live on one STA thread.
var code = 0;
var ui = new Thread(() =>
{
    try
    {
        code = host.Run();
    }
    catch (Exception e)
    {
        log.Mark("ui-error", ("error", e.ToString()));
        code = 3;
    }
})
{ Name = "ui" };
if (OperatingSystem.IsWindows())
{
    ui.SetApartmentState(ApartmentState.STA);
}

ui.Start();
ui.Join();
log.Mark("exit", ("browserExited", host.BrowserExited));
return code;

internal static unsafe class Scenarios
{
    public static void LogMonitors(Log log)
    {
        var list = new List<string>();
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(list);
        EnumDisplayMonitors(0, 0, &OnMonitor, System.Runtime.InteropServices.GCHandle.ToIntPtr(handle));
        handle.Free();
        log.Mark("monitors", ("list", string.Join(" | ", list)));
    }

    [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static int OnMonitor(nint monitor, nint hdc, Rect* rect, nint data)
    {
        var list = (List<string>)System.Runtime.InteropServices.GCHandle.FromIntPtr(data).Target!;
        GetDpiForMonitor(monitor, 0, out var dpiX, out _);
        list.Add($"{*rect} dpi={dpiX}");
        return 1;
    }

    public static string Format((double P50, double P99, double Max, int N) s) =>
        $"p50={s.P50:F1} p99={s.P99:F1} max={s.Max:F1} n={s.N}";

    public static Dictionary<string, Rect> Elements(Host host)
    {
        var done = new ManualResetEventSlim();
        Dictionary<string, Rect> result = [];
        host.Invoke(() => Probe.ReadElements(host, r => { result = r; done.Set(); }));
        done.Wait(5000);
        return result;
    }

    public static void RunProbe(Host host)
    {
        var log = host.Log;
        Thread.Sleep(host.Options.AfterReadyMs);
        var elements = Elements(host);
        var windows = host.InvokeSync(() => Probe.Windows(host.Hwnd));
        foreach (var w in windows)
        {
            log.Mark("window", ("desc", w));
        }

        var points = host.InvokeSync(() => Probe.Points(host, elements));
        var map = host.InvokeSync(() => Probe.HitMap(host, points));
        foreach (var (name, value) in map)
        {
            log.Mark("hit", ("point", name), ("result", value?.ToString()));
        }

        var drag = points.FirstOrDefault(p => p.Name == "drag").Pt;
        var max = points.FirstOrDefault(p => p.Name is "max" or "dwm-max").Pt;
        log.Mark("hit-timing", ("drag", Format(host.InvokeSync(() => Probe.TimeHitTests(host, drag, 2000)))),
            ("max", Format(host.InvokeSync(() => Probe.TimeHitTests(host, max, 2000)))));

        // The renderer busy for 1.5 s: hit tests must not wait for it.
        host.Invoke(() => host.ExecuteScript("(() => { const end = performance.now() + 1500; while (performance.now() < end) {} return 1; })()",
            r => log.Mark("busy-script-done", ("result", r))));
        Thread.Sleep(300);
        log.Mark("hit-timing-renderer-busy", ("drag", Format(host.InvokeSync(() => Probe.TimeHitTests(host, drag, 200)))),
            ("map", string.Join(" | ", host.InvokeSync(() => Probe.HitMap(host, points.Where(p => p.Name is "drag" or "search" or "max" or "top-edge").ToList()))
                .Select(kv => $"{kv.Key}: {kv.Value}"))));
        Thread.Sleep(1500);

        host.InvokeSync(() =>
        {
            var info = new TitleBarInfoEx { Size = (uint)sizeof(TitleBarInfoEx) };
            SendMessage(host.Hwnd, WM_GETTITLEBARINFOEX, 0, (nint)(&info));
            var rects = new StringBuilder();
            for (var i = 0; i < 6; i++)
            {
                rects.Append($"{i}:state=0x{info.State[i]:X} [{info.Rects[i * 4]},{info.Rects[(i * 4) + 1]},{info.Rects[(i * 4) + 2]},{info.Rects[(i * 4) + 3]}] ");
            }

            log.Mark("titlebarinfoex", ("value", rects.ToString()), ("captionButtonBounds", host.CaptionButtonBounds().ToString()),
                ("window", host.WindowRect().ToString()), ("client", host.ClientRect().ToString()), ("dpi", host.Dpi),
                ("topResizeBand", host.TopResizeBand), ("barPx", host.BarPx), ("sinkHitTests", host.SinkHitTests));
            Rect frame;
            DwmGetWindowAttribute(host.Hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, &frame, (uint)sizeof(Rect));
            log.Mark("frame-bounds", ("extendedFrame", frame.ToString()));
            return 0;
        });
        log.Mark("layout-json", ("json", host.LastLayoutJson));
    }

    public static void RunLive(Host host)
    {
        Thread.Sleep(500);
        var dark = host.Options.Dark;
        for (var i = 0; i < host.Options.LiveSwitches; i++)
        {
            dark = !dark;
            var target = dark;
            host.InvokeSync(() => { host.SetTheme(target); return 0; });
            Thread.Sleep(700);
        }
    }

    // PrintWindow frames, about 60 per second, classified per band (title bar, body) against the theme colours.
    public static void CaptureFrames(Host host, CancellationToken stop)
    {
        var log = host.Log;
        while (host.Hwnd == 0)
        {
            Thread.Sleep(1);
        }

        var frames = 0;
        var saved = 0;
        string? lastKey = null;
        var wrong = new List<string>();
        var timeline = new StringBuilder();
        var folder = Path.Combine(log.Folder, log.Label + "-frames");
        Directory.CreateDirectory(folder);
        var bar = (int)Math.Round(host.Options.BarDip * Math.Max(96u, host.Dpi) / 96.0);
        while (!stop.IsCancellationRequested && !host.Destroyed)
        {
            var t = log.Now;
            var pixels = Capture.PrintWindowPixels(host.Hwnd);
            if (pixels is null)
            {
                Thread.Sleep(16);
                continue;
            }

            frames++;
            GetWindowRect(host.Hwnd, out var wr);
            var origin = new Point();
            ClientToScreen(host.Hwnd, ref origin);
            var ox = origin.X - wr.Left;
            var oy = origin.Y - wr.Top;
            GetClientRect(host.Hwnd, out var cr);
            var custom = host.Options.Frame == Frame.Custom;
            var barTop = oy + host.TopResizeBand + 3;
            var barRight = host.Options.Buttons == Buttons.System ? host.CaptionButtonBounds().Left - 8 : ox + cr.Right - 8;
            var barBand = new Rect { Left = ox + 8, Top = barTop, Right = barRight, Bottom = oy + bar - 3 };
            var bodyBand = new Rect { Left = ox + 8, Top = oy + (custom ? bar : 0) + 8, Right = ox + cr.Right - 8, Bottom = oy + cr.Bottom - 8 };
            var body = Capture.Dominant(pixels, bodyBand);
            (uint Color, double Share) barColor = custom ? Capture.Dominant(pixels, barBand) : (0u, 0d);
            var bodyClass = Classify(body.Color);
            var barClass = custom ? Classify(barColor.Color) : "-";
            var key = $"{barClass}/{bodyClass}";
            if (bodyClass == "other" || barClass == "other" || (custom && Theme(barClass) != Theme(bodyClass)))
            {
                wrong.Add($"{t:F0}ms bar={Capture.Hex(barColor.Color)} body={Capture.Hex(body.Color)}");
            }

            if (key != lastKey)
            {
                timeline.Append($"{t:F0}ms {key} (bar {Capture.Hex(barColor.Color)}, body {Capture.Hex(body.Color)}); ");
                lastKey = key;
                if (saved++ < 12)
                {
                    Capture.SavePng(pixels, Path.Combine(folder, $"{frames:D4}-{t:F0}ms.png"));
                }
            }

            var spent = log.Now - t;
            if (spent < 16)
            {
                Thread.Sleep((int)(16 - spent));
            }
        }

        log.Mark("capture-summary", ("frames", frames), ("wrongFrames", wrong.Count), ("wrong", string.Join("; ", wrong.Take(20))),
            ("timeline", timeline.ToString()));
    }

    private static string Classify(uint c) => c switch
    {
        0xffffff => "light-bg",
        0xe8e8e8 => "light-bar",
        0xf3f3f3 => "light-bar-inactive",
        0x1f1f1f => "dark-bg",
        0x2b2b2b => "dark-bar",
        0x262626 => "dark-bar-inactive",
        _ => "other",
    };

    private static string Theme(string cls) => cls.StartsWith("light", StringComparison.Ordinal) ? "light" : cls.StartsWith("dark", StringComparison.Ordinal) ? "dark" : cls;
}
