using System.Diagnostics;
using Spike.BlackBoxAtSpi.AtSpi;
using Spike.BlackBoxAtSpi.Native;

namespace Spike.BlackBoxAtSpi.Runs;

/// <summary>
/// Question 3: the app's main window by process id over AT-SPI, its title, and the close: the window's own AT-SPI
/// action <c>window.close</c> (even runs) against <c>WM_DELETE_WINDOW</c> through X11 (odd runs). Then, with the page's
/// close guard keeping the window (<c>BRIDGE_PROBE_PLAN=guard-keep</c>), whether each close goes through the guard.
/// </summary>
internal static class WindowRun
{
    public static int Run(string[] args)
    {
        var runs = args.Length > 0 ? int.Parse(args[0]) : 20;
        using var bus = Bus.Connect();
        bus.Listen();
        var polled = new List<double>();
        var evented = new List<double>();
        var viewable = new List<double>();
        var ready = new List<double>();
        var titleRead = new List<double>();
        var closeAction = new List<double>();
        var closeX11 = new List<double>();
        var actionCall = new List<double>();
        var failures = 0;
        for (var run = 0; run < runs; run++)
        {
            Bus.ClearSignals();
            using var app = AppRun.Start();
            var seen = Watch.ForWindow(bus, app, (role, name) => role == Reading.RoleFrame && name.Length > 0);
            var readyMs = app.WaitReady();
            var clock = Stopwatch.StartNew();
            var title = seen.Window is { } w ? bus.Name(w) : null;
            var titleMs = clock.Elapsed.TotalMilliseconds;
            string how;
            (int Code, double Ms)? exit;
            if (seen.Window is { } window && run % 2 == 0)
            {
                var actions = bus.Actions(window);
                var index = Array.IndexOf(actions, "window.close");
                var sent = Stopwatch.GetTimestamp();
                var done = bus.DoAction(window, index);
                actionCall.Add(Stats.Ms(sent, Stopwatch.GetTimestamp()));
                exit = app.WaitExit(sent);
                how = $"AT-SPI window.close (action {index}, returned {done})";
                if (exit is { } e)
                {
                    closeAction.Add(e.Ms);
                }
            }
            else
            {
                var x11 = X11.Open(Environment.GetEnvironmentVariable("DISPLAY")!);
                var sent = Stopwatch.GetTimestamp();
                X11.SendDelete(x11, seen.X11Window);
                X11.CloseDisplay(x11);
                exit = app.WaitExit(sent);
                how = "WM_DELETE_WINDOW";
                if (exit is { } e)
                {
                    closeX11.Add(e.Ms);
                }
            }

            var ok = seen.Polled is not null && title == "BridgeProbe" && exit is { Code: 0 };
            failures += ok ? 0 : 1;
            Console.WriteLine($"run {run + 1}: window polled {Watch.Show(seen.Polled)} event {Watch.Show(seen.Event)} ({seen.EventKind}) X11 viewable {Watch.Show(seen.Viewable)} ui-ready {Watch.Show(readyMs)} ms; " +
                $"title \"{title}\" read in {titleMs:0.00} ms; {how}: {(exit is { } x ? $"exit {x.Code} after {x.Ms:0.0} ms" : "no exit")}{(ok ? "" : " FAILED")}");
            Add(polled, seen.Polled);
            Add(evented, seen.Event);
            Add(viewable, seen.Viewable);
            Add(ready, readyMs);
            titleRead.Add(titleMs);
        }

        Console.WriteLine($"SUMMARY window ({runs} runs, {failures} failed)");
        Console.WriteLine(Stats.Line("  launch to window found by polling AT-SPI", polled));
        Console.WriteLine(Stats.Line("  launch to the first AT-SPI event of the window", evented));
        Console.WriteLine(Stats.Line("  launch to X11 viewable", viewable));
        Console.WriteLine(Stats.Line("  launch to ui-ready (log)", ready));
        Console.WriteLine(Stats.Line("  title read (one call)", titleRead));
        Console.WriteLine(Stats.Line("  DoAction(window.close) call", actionCall));
        Console.WriteLine(Stats.Line("  window.close to exit", closeAction));
        Console.WriteLine(Stats.Line("  WM_DELETE_WINDOW to exit", closeX11));
        Guard(bus);
        return failures == 0 ? 0 : 1;
    }

    /// <summary>Both closes with a page guard that keeps the window: each must leave the app running (the guard was asked).</summary>
    private static void Guard(Bus bus)
    {
        foreach (var how in new[] { "window.close", "WM_DELETE_WINDOW" })
        {
            using var app = AppRun.Start(new Dictionary<string, string?> { ["BRIDGE_PROBE_PLAN"] = "guard-keep" });
            var seen = Watch.ForWindow(bus, app, (role, name) => role == Reading.RoleFrame && name.Length > 0);
            app.WaitReady();
            var clock = Stopwatch.StartNew();
            while (!app.LogLines().Any(l => l.Contains("probe note: plan done", StringComparison.Ordinal)) && clock.ElapsedMilliseconds < 5_000)
            {
                Thread.Sleep(10);
            }

            if (how == "window.close")
            {
                bus.DoAction(seen.Window!.Value, Array.IndexOf(bus.Actions(seen.Window!.Value), "window.close"));
            }
            else
            {
                var x11 = X11.Open(Environment.GetEnvironmentVariable("DISPLAY")!);
                X11.SendDelete(x11, seen.X11Window);
                X11.CloseDisplay(x11);
            }

            Thread.Sleep(1_500);
            var kept = app.LogLines().Where(l => l.Contains("close", StringComparison.OrdinalIgnoreCase)).Select(l => l.Length > 160 ? l[..160] : l);
            Console.WriteLine($"guard-keep, {how}: running after 1.5 s: {!app.Process.HasExited}; log: {string.Join(" | ", kept)}");
        }
    }

    private static void Add(List<double> list, double? value)
    {
        if (value is { } v)
        {
            list.Add(v);
        }
    }
}
