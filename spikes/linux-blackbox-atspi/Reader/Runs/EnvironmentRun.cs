using System.Diagnostics;
using Spike.BlackBoxAtSpi.AtSpi;
using Spike.BlackBoxAtSpi.Native;

namespace Spike.BlackBoxAtSpi.Runs;

/// <summary>
/// Question 2: what makes a GTK 4 app (the Production probe) visible over AT-SPI in the run's private Xvfb and D-Bus
/// session, and how long from its launch until its window is. Variant: <c>default</c>, <c>gtk-a11y-none</c>
/// (<c>GTK_A11Y=none</c>), <c>no-at-bridge</c> (<c>NO_AT_BRIDGE=1</c>), <c>private</c> (an accessibility bus the reader
/// starts, its address in <c>AT_SPI_BUS_ADDRESS</c>), <c>events</c> (events registered with the registry first).
/// Also: the tree under the window, the cache call, the window's actions, and libatspi against GDBus for the same reads.
/// </summary>
internal static class EnvironmentRun
{
    public static int Run(string[] args)
    {
        var variant = args.FirstOrDefault() ?? "default";
        var output = Environment.GetEnvironmentVariable("SPIKE_OUT")!;
        Console.WriteLine($"ENV variant={variant} bus={Environment.GetEnvironmentVariable("SPIKE_BUS")} session={Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS") ?? "(none)"}");
        var extra = new Dictionary<string, string?>();
        PrivateBus? own = null;
        if (variant == "private")
        {
            own = PrivateBus.Start();
            Environment.SetEnvironmentVariable("AT_SPI_BUS_ADDRESS", own.Address);
            extra["AT_SPI_BUS_ADDRESS"] = own.Address;
            Console.WriteLine($"private accessibility bus {own.Address} started in {own.StartMs:0.0} ms");
        }

        if (variant == "gtk-a11y-none")
        {
            extra["GTK_A11Y"] = "none";
        }

        if (variant == "no-at-bridge")
        {
            extra["NO_AT_BRIDGE"] = "1";
        }

        var clock = Stopwatch.StartNew();
        var (address, source, error) = Bus.FindAddress();
        Console.WriteLine($"address from {source} in {clock.Elapsed.TotalMilliseconds:0.0} ms: {address ?? "none: " + error}");
        if (address is null)
        {
            // No accessibility bus at all: launch the app anyway for its warnings and its start.
            using var bare = AppRun.Start(extra);
            Console.WriteLine($"ui-ready {bare.WaitReady()?.ToString("0.0") ?? "never"} ms");
            Thread.Sleep(500);
            CloseByX11(bare);
            PrintErrors(bare);
            return 0;
        }

        clock.Restart();
        using var bus = Bus.Connect();
        Console.WriteLine($"connected as {bus.UniqueName} in {clock.Elapsed.TotalMilliseconds:0.0} ms");
        bus.Listen();
        Console.WriteLine("names on the accessibility bus before the launch: " + string.Join(" ", bus.Names()));
        Registry(bus, variant == "events");

        using var app = AppRun.Start(extra);
        double? named = null, window = null, viewable = null;
        string? title = null;
        Ref? frame = null;
        var x11 = X11.Open(Environment.GetEnvironmentVariable("DISPLAY")!);
        try
        {
            while (app.Elapsed < 10_000 && !app.Process.HasExited && (window is null || viewable is null))
            {
                if (named is null && bus.NamesOf(app.Id).Count > 0)
                {
                    named = app.Elapsed;
                }

                if (window is null && named is not null)
                {
                    foreach (var (node, role, name) in Reading.WindowsOf(bus, app.Id))
                    {
                        if (role == Reading.RoleFrame && name.Length > 0)
                        {
                            window = app.Elapsed;
                            title = name;
                            frame = node;
                        }
                    }
                }

                if (viewable is null && X11.WindowsOf(x11, app.Id).Any(w => w.Viewable))
                {
                    viewable = app.Elapsed;
                }

                Thread.Sleep(2);
            }

            var ready = app.WaitReady();
            Console.WriteLine($"launch to: a11y bus name {Show(named)}, window over AT-SPI {Show(window)} (\"{title}\"), X11 viewable {Show(viewable)}, ui-ready {Show(ready)} ms");
            Console.WriteLine("names of the app on the accessibility bus: " + string.Join(" ", bus.NamesOf(app.Id)));
            var listed = bus.RegistryApplications(out var registryError);
            Console.WriteLine($"registry lists {listed.Count} applications{(registryError is null ? "" : ": " + registryError)}; the app among them: {listed.Any(r => bus.ProcessOf(r.Name) == app.Id)}");

            if (frame is { } main)
            {
                Inspect(bus, app, main, output, variant);
                Compare(bus, app, title!);
            }

            Thread.Sleep(300);
            var signals = new List<Signal>();
            Bus.WaitSignal(_ => false, Stopwatch.StartNew(), 50, signals);
            Console.WriteLine($"signals received during the run: {signals.Count}");
            foreach (var group in signals.GroupBy(s => $"{s.Interface.Replace("org.a11y.atspi.Event.", "")}.{s.Member} {s.Detail.Split(' ')[0]}").OrderBy(g => g.Key))
            {
                Console.WriteLine($"  {group.Count(),4} {group.Key}");
            }

            CloseByX11(app);
            PrintErrors(app);
        }
        finally
        {
            X11.CloseDisplay(x11);
            own?.Dispose();
        }

        return 0;
    }

    private static string Show(double? value) => value?.ToString("0.0") ?? "never";

    /// <summary>The registry's interface (introspected), and, for <c>events</c>, the window and children events registered.</summary>
    private static void Registry(Bus bus, bool register)
    {
        var xml = bus.Call(Bus.Registry, "/org/a11y/atspi/registry", "org.freedesktop.DBus.Introspectable", "Introspect", null, out var error);
        if (xml is null)
        {
            Console.WriteLine($"registry: not reachable ({error})");
            return;
        }

        foreach (var line in ((string)xml[0]!).Split('\n').Where(l => l.Contains("<method") || l.Contains("<arg") || l.Contains("<signal")))
        {
            Console.WriteLine("  registry " + line.Trim());
        }

        if (!register)
        {
            return;
        }

        foreach (var kind in new[] { "window:", "object:children-changed", "object:state-changed" })
        {
            var reply = bus.Call(Bus.Registry, "/org/a11y/atspi/registry", "org.a11y.atspi.Registry", "RegisterEvent", [kind, Array.Empty<string>(), ""], out var failure)
                ?? bus.Call(Bus.Registry, "/org/a11y/atspi/registry", "org.a11y.atspi.Registry", "RegisterEvent", [kind], out failure);
            Console.WriteLine($"RegisterEvent({kind}): {(reply is null ? failure : "ok")}");
        }
    }

    /// <summary>The window's tree, its interfaces and actions, and the cache call.</summary>
    private static void Inspect(Bus bus, AppRun app, Ref frame, string output, string variant)
    {
        Console.WriteLine($"window {frame}: interfaces {string.Join(",", bus.Interfaces(frame))}; states 0x{bus.States(frame):x}");
        try
        {
            Console.WriteLine($"window actions: [{string.Join(", ", bus.Actions(frame))}]");
        }
        catch (InvalidOperationException exception)
        {
            Console.WriteLine($"window actions: none ({exception.Message})");
        }

        var root = new Ref(frame.Name, Bus.RootPath);
        Console.WriteLine($"application: toolkit {bus.Property(root, "org.a11y.atspi.Application", "ToolkitName")} {bus.Property(root, "org.a11y.atspi.Application", "Version")}");
        var clock = Stopwatch.StartNew();
        var (text, nodes) = Reading.Dump(bus, root);
        Console.WriteLine($"tree from the application root: {nodes} nodes in {clock.Elapsed.TotalMilliseconds:0.0} ms (walked, several calls a node)");
        File.WriteAllText(Path.Combine(output, $"tree-{variant}.txt"), text + "\n");
        clock.Restart();
        var items = bus.CacheItems(frame.Name, out var error);
        Console.WriteLine(items is null
            ? $"cache GetItems: {error}"
            : $"cache GetItems: {items.Length} items in {clock.Elapsed.TotalMilliseconds:0.0} ms (one call)");

        // Every bus name of the app's process tree: does the page's web process expose itself too?
        foreach (var name in bus.Names().Where(n => n.StartsWith(':')))
        {
            if (bus.ProcessOf(name) is { } pid && pid != app.Id && pid != Environment.ProcessId)
            {
                var comm = File.Exists($"/proc/{pid}/comm") ? File.ReadAllText($"/proc/{pid}/comm").Trim() : "?";
                Console.WriteLine($"other connection {name}: process {pid} ({comm})");
            }
        }
    }

    /// <summary>The same reads through libatspi and through GDBus: find the app by process id and read its window's title (20 each).</summary>
    private static void Compare(Bus bus, AppRun app, string title)
    {
        var clock = Stopwatch.StartNew();
        int init;
        try
        {
            init = Lib.Init();
        }
        catch (DllNotFoundException exception)
        {
            Console.WriteLine($"libatspi: not loadable ({exception.Message})");
            return;
        }

        Console.WriteLine($"libatspi: atspi_init {init} in {clock.Elapsed.TotalMilliseconds:0.0} ms");
        var viaLib = new List<double>();
        var viaBus = new List<double>();
        for (var i = 0; i < 21; i++)
        {
            clock.Restart();
            var applications = Lib.ApplicationsOf(app.Id);
            var names = new List<string>();
            foreach (var application in applications)
            {
                var windows = Lib.Children(application);
                names.AddRange(windows.Select(Lib.Name));
                Lib.Release(windows);
            }

            Lib.Release(applications);
            var libMs = clock.Elapsed.TotalMilliseconds;
            clock.Restart();
            var busNames = Reading.WindowsOf(bus, app.Id).Select(w => w.Name).ToList();
            var busMs = clock.Elapsed.TotalMilliseconds;
            if (i == 0)
            {
                Console.WriteLine($"first read: libatspi {libMs:0.0} ms [{string.Join("|", names)}], GDBus {busMs:0.0} ms [{string.Join("|", busNames)}]");
                continue;
            }

            viaLib.Add(libMs);
            viaBus.Add(busMs);
        }

        Console.WriteLine(Stats.Line("find by pid + window title, libatspi (desktop walk)", viaLib));
        Console.WriteLine(Stats.Line("find by pid + window title, GDBus (bus daemon's pid, then the app)", viaBus));
        var title2 = new List<double>();
        foreach (var window in Reading.WindowsOf(bus, app.Id).Take(1))
        {
            for (var i = 0; i < 20; i++)
            {
                clock.Restart();
                _ = bus.Name(window.Node);
                title2.Add(clock.Elapsed.TotalMilliseconds);
            }
        }

        Console.WriteLine(Stats.Line("one property read (the title), GDBus", title2));
    }

    /// <summary>Closes the app's windows the window manager's way and reports the exit.</summary>
    public static void CloseByX11(AppRun app)
    {
        var x11 = X11.Open(Environment.GetEnvironmentVariable("DISPLAY")!);
        try
        {
            var windows = X11.WindowsOf(x11, app.Id).Where(w => w.Viewable).ToList();
            var sent = Stopwatch.GetTimestamp();
            foreach (var window in windows)
            {
                X11.SendDelete(x11, window.Window);
            }

            var exit = app.WaitExit(sent);
            Console.WriteLine($"WM_DELETE_WINDOW to {windows.Count} window(s) [{string.Join("|", windows.Select(w => w.Title))}]: " +
                (exit is { } e ? $"exit {e.Code} after {e.Ms:0.0} ms" : "no exit within 10 s"));
        }
        finally
        {
            X11.CloseDisplay(x11);
        }
    }

    public static void PrintErrors(AppRun app)
    {
        foreach (var (at, line) in app.Errors.Where(e => !e.Line.Contains(" INFO ", StringComparison.Ordinal)).Take(30))
        {
            Console.WriteLine($"  stderr +{app.Since(at):0} ms: {line}");
        }
    }
}
