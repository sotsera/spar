using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Checks.Desktop.Interop;
using static Checks.Desktop.Interop.Native;

namespace Checks.Desktop;

// Real mouse and keyboard input against the spike window. Windows Sandbox only: it refuses to run anywhere else.
internal static unsafe class Drive
{
    private const string SandboxUser = "WDAGUtilityAccount";
    private static Host s_host = null!;
    private static int s_shot;

    public static void Run(Host host)
    {
        s_host = host;
        var log = host.Log;
        if (!string.Equals(Environment.UserName, SandboxUser, StringComparison.OrdinalIgnoreCase))
        {
            log.Mark("drive-refused", ("user", "not the Windows Sandbox account"));
            return;
        }

        Thread.Sleep(1000);
        host.InvokeSync(() => SetForegroundWindow(host.Hwnd));
        Thread.Sleep(300);
        var steps = (host.Options.DriveSteps ?? "drag,controls,rclick,dblclick,maxhits,maxbutton,topedge,snaphover,minimize,keys,close").Split(',');
        Shot("start");
        foreach (var step in steps)
        {
            try
            {
                log.Mark("step", ("name", step));
                Step(step);
            }
            catch (Exception e)
            {
                log.Mark("step-error", ("name", step), ("error", e.ToString()));
            }

            Restore();
        }
    }

    private static Log L => s_host.Log;

    private static Dictionary<string, Rect> Elements() => Scenarios.Elements(s_host);

    private static Rect Window() => s_host.InvokeSync(s_host.WindowRect);

    private static bool Zoomed() => s_host.InvokeSync(() => IsZoomed(s_host.Hwnd) != 0);

    private static bool Iconic() => s_host.InvokeSync(() => IsIconic(s_host.Hwnd) != 0);

    private static void Restore()
    {
        if (s_host.Destroyed)
        {
            return;
        }

        if (Zoomed() || Iconic())
        {
            s_host.InvokeSync(() => ShowWindow(s_host.Hwnd, SW_RESTORE));
            Thread.Sleep(500);
        }

        s_host.InvokeSync(() => SetForegroundWindow(s_host.Hwnd));
        Move(new Point { X = 5, Y = 5 });
        Thread.Sleep(300);
    }

    private static void Step(string step)
    {
        var el = Elements();
        if (step.StartsWith("dpi:", StringComparison.Ordinal))
        {
            // Changes the guest's display scale while the window is open, then checks the window and the caches.
            var percent = int.Parse(step[4..], System.Globalization.CultureInfo.InvariantCulture);
            var before = s_host.Dpi;
            var t0 = L.Now;
            var set = DisplayScale.Set(percent);
            var end = Environment.TickCount64 + 5000;
            while (s_host.Dpi == before && Environment.TickCount64 < end)
            {
                Thread.Sleep(20);
            }

            var changedMs = L.Now - t0;
            Thread.Sleep(1500);
            L.Mark("result", ("step", step), ("set", set), ("dpiBefore", before), ("dpiAfter", s_host.Dpi), ("msToDpiChange", changedMs),
                ("layoutDpr", s_host.LayoutDpr), ("geometry", Geometry()));
            Shot("dpi" + percent);
            return;
        }

        switch (step)
        {
            case "bar":
                {
                    var bar = el.GetValueOrDefault("bar");
                    var k = s_host.Dpi / 96.0;
                    L.Mark("result", ("step", "bar"), ("barPx", bar.Height), ("barDip", bar.Height / k), ("dpi", s_host.Dpi),
                        ("hostBarPx", s_host.BarPx), ("topBand", s_host.InvokeSync(() => s_host.TopResizeBand)), ("geometry", Geometry()));
                    break;
                }

            case "maxminimize" or "maxclose":
                {
                    s_host.InvokeSync(() => ShowWindow(s_host.Hwnd, SW_MAXIMIZE));
                    Thread.Sleep(800);
                    var el2 = Elements();
                    var target = step == "maxminimize" ? MinButton(el2) : CloseButton(el2);
                    if (target is not null)
                    {
                        Click(target.Value);
                    }

                    Thread.Sleep(1200);
                    L.Mark("result", ("step", step), ("target", target is null ? "none" : $"{target.Value.X},{target.Value.Y}"),
                        ("minimized", !s_host.Destroyed && Iconic()), ("destroyed", s_host.Destroyed));
                    break;
                }

            case "maxsnaphover":
                s_host.InvokeSync(() => ShowWindow(s_host.Hwnd, SW_MAXIMIZE));
                Thread.Sleep(800);
                Step("snaphover");
                break;
            case "tbi":
                L.Mark("result", ("step", "tbi"), ("restored", s_host.InvokeSync(s_host.TitleBarInfoText)),
                    ("captionBounds", s_host.InvokeSync(s_host.CaptionButtonBounds).ToString()), ("window", Window().ToString()));
                s_host.InvokeSync(() => ShowWindow(s_host.Hwnd, SW_MAXIMIZE));
                Thread.Sleep(800);
                L.Mark("result", ("step", "tbi-maximized"), ("maximized", s_host.InvokeSync(s_host.TitleBarInfoText)),
                    ("captionBounds", s_host.InvokeSync(s_host.CaptionButtonBounds).ToString()), ("window", Window().ToString()));
                break;
            case "drag":
                {
                    var before = Window();
                    var start = Probe.Center(el["drag"]);
                    Move(start);
                    Thread.Sleep(150);
                    Button(down: true);
                    for (var i = 1; i <= 12; i++)
                    {
                        Move(new Point { X = start.X + (i * 10), Y = start.Y + (i * 5) });
                        Thread.Sleep(15);
                    }

                    Thread.Sleep(100);
                    Button(down: false);
                    Thread.Sleep(400);
                    var after = Window();
                    L.Mark("result", ("step", "drag"), ("moved", $"{after.Left - before.Left},{after.Top - before.Top}"),
                        ("expected", "120,60"), ("pass", after.Left - before.Left == 120 && after.Top - before.Top == 60));
                    break;
                }

            case "controls":
                {
                    var before = Window();
                    ClearEvents();
                    Click(Probe.Center(el["menu"]));
                    Click(Probe.Center(el["tab2"]));
                    Click(Probe.Center(el["search"]));
                    Thread.Sleep(200);
                    Type("abc");
                    Thread.Sleep(400);
                    var events = Events();
                    var after = Window();
                    var pass = events.Contains("\"target\":\"menu\"") && events.Contains("\"target\":\"tab2\"") && events.Contains("\"value\":\"abc\"")
                        && before.Left == after.Left && before.Top == after.Top;
                    L.Mark("result", ("step", "controls"), ("pass", pass), ("events", Trim(events)));
                    Key(0x1B); // Escape clears focus noise
                    break;
                }

            case "rclick":
                {
                    ClearEvents();
                    Click(Probe.Center(el["drag"]), right: true);
                    Thread.Sleep(700);
                    var menus = TopLevel("#32768");
                    Shot("rclick");
                    L.Mark("result", ("step", "rclick"), ("systemMenuVisible", menus.Count > 0), ("menus", string.Join(";", menus)), ("pageEvents", Trim(Events())));
                    Key(0x1B);
                    Thread.Sleep(300);
                    if (TopLevel("#32768").Count > 0)
                    {
                        Key(0x1B);
                    }

                    break;
                }

            case "dblclick":
                {
                    var p = Probe.Center(el["drag"]);
                    Click(p);
                    Thread.Sleep(30);
                    Click(p);
                    Thread.Sleep(800);
                    var zoomed = Zoomed();
                    L.Mark("result", ("step", "dblclick-maximize"), ("maximized", zoomed), ("geometry", Geometry()));
                    Shot("maximized");
                    var el2 = Elements();
                    var p2 = Probe.Center(el2["drag"]);
                    Click(p2);
                    Thread.Sleep(30);
                    Click(p2);
                    Thread.Sleep(800);
                    L.Mark("result", ("step", "dblclick-restore"), ("maximized", Zoomed()));
                    break;
                }

            case "maxhits":
                {
                    // Maximized: hit tests at every probe point, then a real click on each control.
                    s_host.InvokeSync(() => ShowWindow(s_host.Hwnd, SW_MAXIMIZE));
                    Thread.Sleep(800);
                    var el2 = Elements();
                    var map = s_host.InvokeSync(() => Probe.HitMap(s_host, Probe.Points(s_host, el2)));
                    foreach (var (name, value) in map)
                    {
                        L.Mark("max-hit", ("point", name), ("result", value?.ToString()));
                    }

                    L.Mark("result", ("step", "maxhits"), ("geometry", Geometry()));
                    ClearEvents();
                    Click(Probe.Center(el2["menu"]));
                    Click(Probe.Center(el2["search"]));
                    Thread.Sleep(300);
                    L.Mark("result", ("step", "max-controls"), ("events", Trim(Events())), ("stillMaximized", Zoomed()));
                    break;
                }

            case "maxbutton":
                {
                    var target = MaxButton(el);
                    if (target is null)
                    {
                        L.Mark("result", ("step", "maxbutton"), ("skipped", "no maximize button"));
                        break;
                    }

                    Click(target.Value);
                    Thread.Sleep(800);
                    var zoomed = Zoomed();
                    L.Mark("result", ("step", "maxbutton-click"), ("maximized", zoomed));
                    var target2 = MaxButton(Elements());
                    if (target2 is not null)
                    {
                        Click(target2.Value);
                        Thread.Sleep(800);
                    }

                    L.Mark("result", ("step", "maxbutton-click-restore"), ("maximized", Zoomed()));
                    break;
                }

            case "topedge":
                {
                    var before = Window();
                    var client = new Point();
                    s_host.InvokeSync(() => ClientToScreen(s_host.Hwnd, ref client));
                    var start = new Point { X = (before.Left + before.Right) / 2, Y = client.Y + 2 };
                    var code = s_host.InvokeSync(() => (long)SendMessage(s_host.Hwnd, WM_NCHITTEST, 0, MakeLParam(start.X, start.Y)));
                    Move(start);
                    Thread.Sleep(200);
                    Button(down: true);
                    for (var i = 1; i <= 8; i++)
                    {
                        Move(new Point { X = start.X, Y = start.Y - (i * 5) });
                        Thread.Sleep(20);
                    }

                    Thread.Sleep(100);
                    Button(down: false);
                    Thread.Sleep(400);
                    var after = Window();
                    L.Mark("result", ("step", "topedge"), ("hit", Probe.Name(code)), ("topMoved", after.Top - before.Top),
                        ("heightChange", after.Height - before.Height), ("pass", after.Top - before.Top == -40 && after.Height - before.Height == 40));
                    break;
                }

            case "snaphover":
                {
                    var target = MaxButton(el);
                    if (target is null)
                    {
                        L.Mark("result", ("step", "snaphover"), ("skipped", "no maximize button"));
                        break;
                    }

                    ClearEvents();
                    var k = s_host.Dpi / 96.0;
                    int S(int v) => (int)Math.Round(v * k);
                    var area = new Rect { Left = target.Value.X - S(200), Top = target.Value.Y + S(30), Right = target.Value.X + S(200), Bottom = target.Value.Y + S(200) };
                    Move(new Point { X = target.Value.X, Y = target.Value.Y + S(120) });
                    Thread.Sleep(600);
                    var before = Capture.Screen(area);
                    Move(new Point { X = target.Value.X - S(30), Y = target.Value.Y });
                    Thread.Sleep(100);
                    Move(target.Value);
                    Thread.Sleep(1800);
                    var during = Capture.Screen(area);
                    Shot("snaphover");
                    var changed = 0;
                    for (var i = 0; i < before!.Data.Length; i++)
                    {
                        changed += before.Data[i] != during!.Data[i] ? 1 : 0;
                    }

                    var share = (double)changed / before.Data.Length;
                    L.Mark("result", ("step", "snaphover"), ("changedShareBelowButton", share), ("flyout", share > 0.3),
                        ("hit", Probe.Name(s_host.InvokeSync(() => (long)SendMessage(WindowFromPoint(target.Value), WM_NCHITTEST, 0, MakeLParam(target.Value.X, target.Value.Y))))),
                        ("pageEvents", Trim(Events())));
                    Move(new Point { X = 5, Y = 5 });
                    Thread.Sleep(400);
                    Key(0x1B);
                    break;
                }

            case "minimize":
                {
                    var target = MinButton(el);
                    if (target is null)
                    {
                        L.Mark("result", ("step", "minimize"), ("skipped", "no minimize button"));
                        break;
                    }

                    Click(target.Value);
                    Thread.Sleep(800);
                    L.Mark("result", ("step", "minimize"), ("minimized", Iconic()));
                    break;
                }

            case "keys":
                {
                    foreach (var (name, vk) in new[] { ("win-up", (ushort)0x26), ("win-left", (ushort)0x25), ("win-right", (ushort)0x27), ("win-down", (ushort)0x28) })
                    {
                        Restore();
                        Thread.Sleep(300);
                        var fg = s_host.InvokeSync(() => GetForegroundWindow() == s_host.Hwnd);
                        Chord(0x5B, vk, extended: true);
                        Thread.Sleep(1200);
                        Shot(name);
                        L.Mark("result", ("step", name), ("wasForeground", fg), ("maximized", Zoomed()), ("minimized", Iconic()), ("geometry", Geometry()));
                        Key(0x1B); // dismisses Snap Assist if it opened
                        Thread.Sleep(300);
                    }

                    Restore();
                    Chord(0x12, 0x20); // Alt+Space
                    Thread.Sleep(600);
                    var menus = TopLevel("#32768");
                    Shot("altspace");
                    L.Mark("result", ("step", "alt-space"), ("systemMenuVisible", menus.Count > 0));
                    Key(0x1B);
                    Thread.Sleep(300);
                    if (TopLevel("#32768").Count > 0)
                    {
                        Key(0x1B);
                    }

                    break;
                }

            case "close":
                {
                    var target = CloseButton(el);
                    if (target is not null)
                    {
                        Click(target.Value);
                    }
                    else
                    {
                        Chord(0x12, 0x73); // Alt+F4
                    }

                    Thread.Sleep(1500);
                    L.Mark("result", ("step", "close"), ("via", target is null ? "alt-f4" : "button"), ("destroyed", s_host.Destroyed));
                    break;
                }

            case "altf4":
                Chord(0x12, 0x73);
                Thread.Sleep(1500);
                L.Mark("result", ("step", "altf4"), ("destroyed", s_host.Destroyed));
                break;
        }
    }

    private static Point? MaxButton(Dictionary<string, Rect> el) => Button(el, "max", 1);

    private static Point? MinButton(Dictionary<string, Rect> el) => Button(el, "min", 0);

    private static Point? CloseButton(Dictionary<string, Rect> el) => Button(el, "close", 2);

    private static Point? Button(Dictionary<string, Rect> el, string id, int index)
    {
        if (s_host.Options.Frame == Frame.Standard || s_host.Options.Buttons == Buttons.System)
        {
            var w = Window();
            var b = s_host.InvokeSync(s_host.CaptionButtonBounds);
            if (b.Width <= 0)
            {
                return null;
            }

            var third = b.Width / 3;
            return new Point { X = w.Left + b.Left + (third * index) + (third / 2), Y = w.Top + ((b.Top + b.Bottom) / 2) };
        }

        return el.TryGetValue(id, out var r) ? Probe.Center(r) : null;
    }

    private static string Geometry() => s_host.InvokeSync(() =>
    {
        Rect frame;
        DwmGetWindowAttribute(s_host.Hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, &frame, (uint)sizeof(Rect));
        var info = new MonitorInfo { Size = (uint)sizeof(MonitorInfo) };
        GetMonitorInfo(MonitorFromWindow(s_host.Hwnd, MONITOR_DEFAULTTONEAREST), ref info);
        var origin = new Point();
        ClientToScreen(s_host.Hwnd, ref origin);
        var client = s_host.ClientRect();
        var clientScreen = new Rect { Left = origin.X, Top = origin.Y, Right = origin.X + client.Right, Bottom = origin.Y + client.Bottom };
        var inside = clientScreen.Left >= info.Work.Left && clientScreen.Top >= info.Work.Top && clientScreen.Right <= info.Work.Right && clientScreen.Bottom <= info.Work.Bottom;
        return $"window={s_host.WindowRect()} frame={frame} client={clientScreen} work={info.Work} monitor={info.Monitor} clientInsideWork={inside} dpi={s_host.Dpi}";
    });

    private static void ClearEvents()
    {
        lock (s_host.PageEvents)
        {
            s_host.PageEvents.Clear();
        }
    }

    private static string Events()
    {
        lock (s_host.PageEvents)
        {
            return string.Join(" ", s_host.PageEvents);
        }
    }

    private static string Trim(string s) => s.Length > 1500 ? s[..1500] : s;

    public static void Init(Host host) => s_host = host;

    public static List<string> TopLevelWindows() => AllTopLevel();

    public static void Shot(string name)
    {
        var info = new MonitorInfo { Size = (uint)sizeof(MonitorInfo) };
        GetMonitorInfo(MonitorFromPoint(default, MONITOR_DEFAULTTONEAREST), ref info);
        var pixels = Capture.Screen(info.Monitor);
        if (pixels is not null)
        {
            var path = Path.Combine(L.Folder, $"{L.Label}-{++s_shot:D2}-{name}.png");
            Capture.SavePng(pixels, path);
            L.Mark("screenshot", ("file", Path.GetFileName(path)));
        }
    }

    // ---- Input ----

    private static void Move(Point p)
    {
        // Absolute coordinates over the virtual desktop, normalized to 0..65535.
        var vx = GetSystemMetricsForDpi(76, 96);
        var vy = GetSystemMetricsForDpi(77, 96);
        var vw = GetSystemMetricsForDpi(78, 96);
        var vh = GetSystemMetricsForDpi(79, 96);
        var input = new Input { Type = 0 };
        input.Mouse.Dx = (int)Math.Round((p.X - vx) * 65535.0 / (vw - 1));
        input.Mouse.Dy = (int)Math.Round((p.Y - vy) * 65535.0 / (vh - 1));
        input.Mouse.Flags = 0x0001 | 0x8000 | 0x4000; // MOVE | ABSOLUTE | VIRTUALDESK
        SendInput(1, &input, sizeof(Input));
    }

    private static void Button(bool down, bool right = false)
    {
        var input = new Input { Type = 0 };
        input.Mouse.Flags = right ? (down ? 0x0008u : 0x0010u) : (down ? 0x0002u : 0x0004u);
        SendInput(1, &input, sizeof(Input));
    }

    private static void Click(Point p, bool right = false)
    {
        Move(p);
        Thread.Sleep(80);
        Button(down: true, right);
        Thread.Sleep(40);
        Button(down: false, right);
        Thread.Sleep(120);
    }

    private static void Key(ushort vk, bool up = false, bool extended = false)
    {
        var input = new Input { Type = 1 };
        input.Keyboard.Vk = vk;
        input.Keyboard.Flags = (up ? 0x0002u : 0) | (extended ? 0x0001u : 0);
        SendInput(1, &input, sizeof(Input));
        if (!up)
        {
            Thread.Sleep(30);
            Key(vk, up: true, extended);
        }
    }

    private static void Chord(ushort modifier, ushort key, bool extended = false)
    {
        var down = new Input { Type = 1 };
        down.Keyboard.Vk = modifier;
        SendInput(1, &down, sizeof(Input));
        Thread.Sleep(50);
        Key(key, extended: extended);
        Thread.Sleep(50);
        var up = new Input { Type = 1 };
        up.Keyboard.Vk = modifier;
        up.Keyboard.Flags = 0x0002;
        SendInput(1, &up, sizeof(Input));
    }

    private static void Type(string text)
    {
        foreach (var ch in text)
        {
            var input = new Input { Type = 1 };
            input.Keyboard.Scan = ch;
            input.Keyboard.Flags = 0x0004; // UNICODE
            SendInput(1, &input, sizeof(Input));
            input.Keyboard.Flags = 0x0004 | 0x0002;
            SendInput(1, &input, sizeof(Input));
            Thread.Sleep(30);
        }
    }

    // ---- Top-level windows (read-only) ----

    private static List<string> TopLevel(string className) => [.. AllTopLevel().Where(w => w.StartsWith(className + " ", StringComparison.Ordinal))];

    private static List<string> AllTopLevel()
    {
        var list = new List<string>();
        var handle = GCHandle.Alloc(list);
        try
        {
            EnumWindows(&OnWindow, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }

        return list;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int OnWindow(nint hwnd, nint data)
    {
        if (IsWindowVisible(hwnd) != 0)
        {
            var list = (List<string>)GCHandle.FromIntPtr(data).Target!;
            GetWindowThreadProcessId(hwnd, out var pid);
            GetWindowRect(hwnd, out var r);
            string process;
            try { process = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; }
            catch (ArgumentException) { process = "?"; }

            list.Add($"{ClassNameOf(hwnd)} '{TextOf(hwnd)}' {process} {r}");
        }

        return 1;
    }
}
