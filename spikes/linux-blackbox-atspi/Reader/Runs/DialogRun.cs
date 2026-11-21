using System.Diagnostics;
using Spike.BlackBoxAtSpi.AtSpi;
using Spike.BlackBoxAtSpi.Native;

namespace Spike.BlackBoxAtSpi.Runs;

/// <summary>
/// Question 4: Spar's <c>GtkAlertDialog</c>s over AT-SPI. <c>relaunch</c>: a first instance stopped with SIGSTOP, a
/// second one shows "can't start" (not responding, exit 8) over no window; the reader finds it, reads it, presses
/// Close (AT-SPI <c>DoAction</c> on even runs, an XTEST click at the button's extents on odd runs), and the second
/// exits 8. <c>hang</c>: the probe's page hangs (<c>BRIDGE_PROBE_PLAN=hang</c>) and Spar's "Wait / Reload page" dialog,
/// owned by the window, is found, read and answered "Reload page"; then the window is closed.
/// </summary>
internal static class DialogRun
{
    public static int Relaunch(string[] args)
    {
        var runs = args.Length > 0 ? int.Parse(args[0]) : 20;
        var timings = new Timings();
        using var bus = Bus.Connect();
        bus.Listen();
        var failures = 0;
        for (var run = 0; run < runs; run++)
        {
            Bus.ClearSignals();
            using var first = AppRun.Start();
            var main = Watch.ForWindow(bus, first, (role, name) => role == Reading.RoleFrame && name.Length > 0);
            first.WaitReady();
            first.Signal(Libc.SigStop);
            using var second = AppRun.Start();
            var ok = Answer(bus, second, null, "Close", run % 2 == 1, 8, timings, $"relaunch {run + 1}",
                () => second.ErrorLine("Start-up problem NotResponding")?.At);
            first.Signal(Libc.SigCont);
            var x11 = X11.Open(Environment.GetEnvironmentVariable("DISPLAY")!);
            var sent = Stopwatch.GetTimestamp();
            X11.SendDelete(x11, main.X11Window);
            X11.CloseDisplay(x11);
            var exit = first.WaitExit(sent);
            var dropped = first.LogLines().Any(l => l.Contains("was dropped", StringComparison.Ordinal));
            Console.WriteLine($"  first instance resumed and closed: {(exit is { } e ? $"exit {e.Code} after {e.Ms:0.0} ms" : "no exit")}; stale relaunch dropped: {dropped}");
            failures += ok && exit is { Code: 0 } ? 0 : 1;
        }

        timings.Print($"relaunch ({runs} runs, {failures} failed)");
        return failures == 0 ? 0 : 1;
    }

    public static int Hang(string[] args)
    {
        var runs = args.Length > 0 ? int.Parse(args[0]) : 20;
        var timings = new Timings();
        using var bus = Bus.Connect();
        bus.Listen();
        var failures = 0;
        for (var run = 0; run < runs; run++)
        {
            Bus.ClearSignals();
            using var app = AppRun.Start(new Dictionary<string, string?> { ["BRIDGE_PROBE_PLAN"] = "hang" });
            var main = Watch.ForWindow(bus, app, (role, name) => role == Reading.RoleFrame && name.Length > 0);
            app.WaitReady();
            Bus.ClearSignals();
            var ok = Answer(bus, app, new HashSet<nuint> { main.X11Window }, "Reload page", run % 2 == 1, null, timings, $"hang {run + 1}",
                () => app.ErrorLine("hung-page dialog shown")?.At, timeoutMs: 30_000, mainTitle: main.Title);
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < 5_000 && !app.LogLines().Any(l => l.Contains("hung-page dialog: Reload page", StringComparison.Ordinal)))
            {
                Thread.Sleep(10);
            }

            var reloaded = app.LogLines().FirstOrDefault(l => l.Contains("hung-page dialog: Reload page", StringComparison.Ordinal));
            var x11 = X11.Open(Environment.GetEnvironmentVariable("DISPLAY")!);
            Thread.Sleep(300);
            var sent = Stopwatch.GetTimestamp();
            X11.SendDelete(x11, main.X11Window);
            X11.CloseDisplay(x11);
            var exit = app.WaitExit(sent);
            Console.WriteLine($"  host log: {reloaded?.Split("] ").LastOrDefault() ?? "no Reload page line"}; then WM_DELETE_WINDOW: {(exit is { } e ? $"exit {e.Code} after {e.Ms:0.0} ms" : "no exit")}");
            failures += ok && reloaded is not null && exit is { Code: 0 } ? 0 : 1;
        }

        timings.Print($"hang ({runs} runs, {failures} failed)");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// Finds the app's dialog (a top-level window that is not the main one), reads it both ways, presses
    /// <paramref name="button"/> and, when <paramref name="exitCode"/> is set, waits for that exit.
    /// </summary>
    private static bool Answer(
        Bus bus, AppRun app, ISet<nuint>? known, string button, bool xtest, int? exitCode, Timings timings, string label,
        Func<long?> reference, int timeoutMs = 10_000, string? mainTitle = null)
    {
        var seen = Watch.ForWindow(bus, app, (role, name) => mainTitle is null ? role is Reading.RoleAlert or Reading.RoleDialog or Reading.RoleFrame : role != Reading.RoleFrame || name != mainTitle, known, timeoutMs);
        if (seen.Window is not { } window)
        {
            Console.WriteLine($"{label}: no dialog over AT-SPI (X11 viewable {Watch.Show(seen.Viewable)}); stderr: {string.Join(" | ", app.Errors.TakeLast(3).Select(e => e.Line))}");
            return false;
        }

        var referenceAt = reference() is { } at ? app.Since(at) : (double?)null;
        var clock = Stopwatch.StartNew();
        var dialog = Reading.ReadDialog(bus, window, seen.Role, seen.Title);
        var walkMs = clock.Elapsed.TotalMilliseconds;
        clock.Restart();
        var cached = Reading.ReadDialogFromCache(bus, window, seen.Title, out var cacheError);
        var cacheMs = clock.Elapsed.TotalMilliseconds;
        var same = cached is not null && cached.Texts.SequenceEqual(dialog.Texts) && cached.Buttons.Select(b => b.Name).SequenceEqual(dialog.Buttons.Select(b => b.Name));
        var target = dialog.Buttons.FirstOrDefault(b => b.Name == button);
        var states = bus.States(window);
        Console.WriteLine($"{label}: [{dialog.Role}] \"{dialog.Title}\" modal={Reading.Has(states, Reading.StateModal)} texts [{string.Join(" | ", dialog.Texts.Select(t => t.Replace('\n', ' ')))}] " +
            $"buttons [{string.Join(" | ", dialog.Buttons.Select(b => b.Name))}]; walk {walkMs:0.0} ms in {dialog.Calls} calls, cache {cacheMs:0.0} ms {(cached is null ? cacheError : same ? "same" : "DIFFERENT: " + string.Join(" | ", cached.Texts) + " / " + string.Join(" | ", cached.Buttons.Select(b => b.Name)))}");
        if (target.Name is null)
        {
            Console.WriteLine($"{label}: no button '{button}'");
            return false;
        }

        var actions = bus.Actions(target.Node);
        var pressed = Stopwatch.GetTimestamp();
        string how;
        if (xtest)
        {
            var screen = bus.Extents(target.Node);
            var inWindow = Window(bus, target.Node);
            var x11 = X11.Open(Environment.GetEnvironmentVariable("DISPLAY")!);
            var origin = X11.Origin(x11, seen.X11Window);
            var (x, y) = (origin.X + inWindow.X + (inWindow.Width / 2), origin.Y + inWindow.Y + (inWindow.Height / 2));
            pressed = Stopwatch.GetTimestamp();
            X11.Click(x11, x, y);
            X11.CloseDisplay(x11);
            how = $"XTEST click at ({x},{y}) [screen extents {screen}, window extents {inWindow}, X11 origin {origin}]";
        }
        else
        {
            var done = bus.DoAction(target.Node, Math.Max(0, Array.IndexOf(actions, "click")));
            how = $"DoAction({string.Join(",", actions)}) = {done} in {Stats.Ms(pressed, Stopwatch.GetTimestamp()):0.00} ms";
        }

        double? exitMs = null;
        var ok = true;
        if (exitCode is { } expected)
        {
            var exit = app.WaitExit(pressed);
            exitMs = exit?.Ms;
            ok = exit?.Code == expected;
            how += $"; exit {exit?.Code.ToString() ?? "none"} after {Watch.Show(exitMs)} ms";
        }
        else
        {
            // The dialog goes: wait for its window to leave the AT-SPI tree.
            var gone = Stopwatch.StartNew();
            while (gone.ElapsedMilliseconds < 5_000 && Reading.WindowsOf(bus, app.Id).Any(w => w.Node == window))
            {
                Thread.Sleep(2);
            }

            exitMs = Stats.Ms(pressed, Stopwatch.GetTimestamp());
            ok = gone.ElapsedMilliseconds < 5_000;
            how += $"; dialog gone from AT-SPI after {exitMs:0.0} ms";
        }

        Console.WriteLine($"{label}: reference line {Watch.Show(referenceAt)}, X11 viewable {Watch.Show(seen.Viewable)}, polled {Watch.Show(seen.Polled)}, event {Watch.Show(seen.Event)} ({seen.EventKind}) ms after launch; {how}{(ok ? "" : " FAILED")}");
        timings.Add(seen, referenceAt, walkMs, cacheMs, xtest, Stats.Ms(pressed, Stopwatch.GetTimestamp()), exitMs);
        return ok;
    }

    /// <summary>Extents relative to the window (<c>ATSPI_COORD_TYPE_WINDOW</c>).</summary>
    private static (int X, int Y, int Width, int Height) Window(Bus bus, Ref node)
    {
        var box = (object?[])bus.CallOrThrow(node.Name, node.Path, "org.a11y.atspi.Component", "GetExtents", 1u)[0]!;
        return ((int)box[0]!, (int)box[1]!, (int)box[2]!, (int)box[3]!);
    }

    private sealed class Timings
    {
        private readonly List<double> _polledAfterViewable = [];
        private readonly List<double> _eventAfterViewable = [];
        private readonly List<double> _viewableAfterReference = [];
        private readonly List<double> _walk = [];
        private readonly List<double> _cache = [];
        private readonly List<double> _doneAction = [];
        private readonly List<double> _doneXtest = [];

        public void Add(Seen seen, double? reference, double walk, double cache, bool xtest, double _, double? done)
        {
            if (seen.Viewable is { } viewable)
            {
                if (seen.Polled is { } polled)
                {
                    _polledAfterViewable.Add(polled - viewable);
                }

                if (seen.Event is { } evented)
                {
                    _eventAfterViewable.Add(evented - viewable);
                }

                if (reference is { } r)
                {
                    _viewableAfterReference.Add(viewable - r);
                }
            }

            _walk.Add(walk);
            _cache.Add(cache);
            if (done is { } d)
            {
                (xtest ? _doneXtest : _doneAction).Add(d);
            }
        }

        public void Print(string what)
        {
            Console.WriteLine($"SUMMARY {what}");
            Console.WriteLine(Stats.Line("  the host's log line to the dialog viewable (X11)", _viewableAfterReference));
            Console.WriteLine(Stats.Line("  viewable to found by polling AT-SPI (negative: before the map)", _polledAfterViewable));
            Console.WriteLine(Stats.Line("  viewable to the first AT-SPI event (negative: before the map)", _eventAfterViewable));
            Console.WriteLine(Stats.Line("  read the dialog by walking", _walk));
            Console.WriteLine(Stats.Line("  read the dialog from the cache (one call)", _cache));
            Console.WriteLine(Stats.Line("  press by DoAction to done (exit or dialog gone)", _doneAction));
            Console.WriteLine(Stats.Line("  press by XTEST click to done (exit or dialog gone)", _doneXtest));
        }
    }
}
