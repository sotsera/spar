using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Checks.Desktop.Interop;
using static Checks.Desktop.Interop.Native;

namespace Checks.Desktop;

// Host-safe measurements: messages to the spike's own windows, its own captures, nothing on the user's input.
internal static unsafe class Probe
{
    public const string ElementScript = """
        (() => { const o = {}; for (const id of ['bar','menu','title','tab1','tab2','search','drag','reserve','min','max','close','main']) {
          const el = document.getElementById(id); if (!el || el.hidden || el.offsetParent === null) continue;
          const r = el.getBoundingClientRect(); o[id] = [r.left, r.top, r.right, r.bottom]; }
          return JSON.stringify({ dpr: devicePixelRatio, rects: o }); })()
        """;

    // Element rectangles in screen pixels, read once from the page (the probe only, never the hit test).
    public static void ReadElements(Host host, Action<Dictionary<string, Rect>> done)
    {
        host.ExecuteScript(ElementScript, json =>
        {
            var result = new Dictionary<string, Rect>();
            if (json is not null)
            {
                using var outer = JsonDocument.Parse(json);
                using var doc = JsonDocument.Parse(outer.RootElement.GetString()!);
                var dpr = doc.RootElement.GetProperty("dpr").GetDouble();
                var origin = new Point();
                ClientToScreen(host.Hwnd, ref origin);
                foreach (var p in doc.RootElement.GetProperty("rects").EnumerateObject())
                {
                    result[p.Name] = new Rect
                    {
                        Left = origin.X + (int)Math.Round(p.Value[0].GetDouble() * dpr),
                        Top = origin.Y + (int)Math.Round(p.Value[1].GetDouble() * dpr),
                        Right = origin.X + (int)Math.Round(p.Value[2].GetDouble() * dpr),
                        Bottom = origin.Y + (int)Math.Round(p.Value[3].GetDouble() * dpr),
                    };
                }
            }

            done(result);
        });
    }

    public static Point Center(Rect r) => new() { X = (r.Left + r.Right) / 2, Y = (r.Top + r.Bottom) / 2 };

    // Named probe points in screen pixels.
    public static List<(string Name, Point Pt)> Points(Host host, Dictionary<string, Rect> el)
    {
        var w = host.WindowRect();
        var c = new Point();
        ClientToScreen(host.Hwnd, ref c);
        var client = host.ClientRect();
        var list = new List<(string, Point)>
        {
            ("top-edge", new Point { X = c.X + (client.Right / 2), Y = c.Y + 2 }),
            ("top-left-corner", new Point { X = c.X + 2, Y = c.Y + 2 }),
            ("left-border", new Point { X = w.Left + 2, Y = (w.Top + w.Bottom) / 2 }),
            ("right-border", new Point { X = w.Right - 3, Y = (w.Top + w.Bottom) / 2 }),
            ("bottom-border", new Point { X = (w.Left + w.Right) / 2, Y = w.Bottom - 3 }),
        };
        foreach (var name in new[] { "menu", "search", "close" })
        {
            if (el.TryGetValue(name, out var r))
            {
                list.Add(($"top-edge-over-{name}", new Point { X = Center(r).X, Y = c.Y + 4 }));
            }
        }

        foreach (var name in new[] { "menu", "title", "tab1", "search", "drag", "reserve", "min", "max", "close", "main" })
        {
            if (el.TryGetValue(name, out var r))
            {
                list.Add((name, Center(r)));
            }
        }

        if (host.Options.Buttons == Buttons.System && host.Options.Frame == Frame.Custom)
        {
            // DWM's own buttons, from the caption button bounds (window coordinates).
            var b = host.CaptionButtonBounds();
            var third = b.Width / 3;
            for (var i = 0; i < 3; i++)
            {
                list.Add(($"dwm-{(i == 0 ? "min" : i == 1 ? "max" : "close")}",
                    new Point { X = w.Left + b.Left + (third * i) + (third / 2), Y = w.Top + ((b.Top + b.Bottom) / 2) }));
            }
        }

        return list;
    }

    public static Dictionary<string, object?> HitMap(Host host, List<(string Name, Point Pt)> points)
    {
        var map = new Dictionary<string, object?>();
        foreach (var (name, pt) in points)
        {
            var target = WindowFromPoint(pt);
            var code = (long)SendMessageTimeout(target, WM_NCHITTEST, 0, MakeLParam(pt.X, pt.Y), 0x2, 1000, out var r) != 0 ? (long)r : -999;
            var top = (long)SendMessage(host.Hwnd, WM_NCHITTEST, 0, MakeLParam(pt.X, pt.Y));
            string? region = null;
            if (host.Composition4 != 0)
            {
                var cp = pt;
                ScreenToClient(host.Hwnd, ref cp);
                Vt.NonClientRegionAtPoint(host.Composition4, cp, out var kind);
                region = kind.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            GetWindowThreadProcessId(target, out var pid);
            map[name] = $"window={ClassNameOf(target)} pid={(pid == GetCurrentProcessId() ? "self" : pid.ToString(System.Globalization.CultureInfo.InvariantCulture))} " +
                $"windowHit={Name(code)} topLevelHit={Name(top)}" + (region is null ? "" : $" webview2Region={region}");
        }

        return map;
    }

    public static string Name(long code) => code switch
    {
        -999 => "timeout",
        HTTRANSPARENT => "HTTRANSPARENT",
        HTNOWHERE => "HTNOWHERE",
        HTCLIENT => "HTCLIENT",
        HTCAPTION => "HTCAPTION",
        HTSYSMENU => "HTSYSMENU",
        HTMINBUTTON => "HTMINBUTTON",
        HTMAXBUTTON => "HTMAXBUTTON",
        HTLEFT => "HTLEFT",
        HTRIGHT => "HTRIGHT",
        HTTOP => "HTTOP",
        HTTOPLEFT => "HTTOPLEFT",
        HTTOPRIGHT => "HTTOPRIGHT",
        HTBOTTOM => "HTBOTTOM",
        HTBOTTOMLEFT => "HTBOTTOMLEFT",
        HTBOTTOMRIGHT => "HTBOTTOMRIGHT",
        HTCLOSE => "HTCLOSE",
        _ => code.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    // Every descendant window of the spike window, and the process's other top-level windows.
    public static List<string> Windows(nint hwnd)
    {
        var list = new List<string>();
        var handle = GCHandle.Alloc(list);
        try
        {
            Describe(list, hwnd, "top");
            EnumChildWindows(hwnd, &OnChild, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }

        return list;
    }

    public static List<nint> ChildWindows(nint hwnd)
    {
        var list = new List<nint>();
        var handle = GCHandle.Alloc(list);
        try
        {
            EnumChildWindows(hwnd, &OnChildHandle, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }

        return list;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int OnChildHandle(nint hwnd, nint data)
    {
        ((List<nint>)GCHandle.FromIntPtr(data).Target!).Add(hwnd);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int OnChild(nint hwnd, nint data)
    {
        var list = (List<string>)GCHandle.FromIntPtr(data).Target!;
        Describe(list, hwnd, "child");
        return 1;
    }

    private static void Describe(List<string> list, nint hwnd, string kind)
    {
        var thread = GetWindowThreadProcessId(hwnd, out var pid);
        GetWindowRect(hwnd, out var r);
        var ex = (long)GetWindowLongPtr(hwnd, -20);
        list.Add($"{kind} {ClassNameOf(hwnd)} parent={ClassNameOf(GetParent(hwnd))} pid={(pid == GetCurrentProcessId() ? "self" : "other")} " +
            $"thread={(thread == GetCurrentThreadId() ? "ui" : "other")} visible={IsWindowVisible(hwnd)} rect={r} exstyle=0x{ex:X}");
    }

    public static (double P50, double P99, double Max, int N) Stats(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0)
        {
            return (0, 0, 0, 0);
        }

        return (sorted[sorted.Length / 2], sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * 0.99))], sorted[^1], sorted.Length);
    }

    // Hit-test cost: the top-level window's WM_NCHITTEST, called directly on the UI thread.
    public static (double P50, double P99, double Max, int N) TimeHitTests(Host host, Point pt, int count)
    {
        var samples = new List<double>(count);
        for (var i = 0; i < count; i++)
        {
            var t0 = Stopwatch.GetTimestamp();
            SendMessage(host.Hwnd, WM_NCHITTEST, 0, MakeLParam(pt.X, pt.Y));
            samples.Add(Stopwatch.GetElapsedTime(t0).TotalMicroseconds);
        }

        return Stats(samples);
    }
}
