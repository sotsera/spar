using System.Diagnostics;
using Spike.BlackBoxAtSpi.AtSpi;
using Spike.BlackBoxAtSpi.Native;

namespace Spike.BlackBoxAtSpi.Runs;

/// <summary>When a window of the app came, seen three ways: by polling AT-SPI, by AT-SPI events, and by X11 (viewable).</summary>
internal sealed record Seen(double? Polled, double? Event, string? EventKind, double? Viewable, Ref? Window, uint Role, string Title, nuint X11Window);

/// <summary>
/// Waits for a top-level window of a process that <paramref name="accept"/> takes, polling every
/// <see cref="PollMs"/> ms (the bus daemon for the process's names, then the app's root's children) while draining the
/// event queue; an event counts when its sender belongs to the process and it is a children-changed on the root, a
/// window event, or a state change to showing.
/// </summary>
internal static class Watch
{
    public const int PollMs = 5;

    public static Seen ForWindow(Bus bus, AppRun app, Func<uint, string, bool> accept, ISet<nuint>? knownX11 = null, int timeoutMs = 10_000)
    {
        double? polled = null, evented = null, viewable = null;
        string? kind = null;
        Ref? window = null;
        uint role = 0;
        var title = "";
        nuint x11Window = 0;
        var owners = new Dictionary<string, bool>();
        var all = new List<Signal>();
        var x11 = X11.Open(Environment.GetEnvironmentVariable("DISPLAY")!);
        try
        {
            while (app.Elapsed < timeoutMs && (polled is null || viewable is null || evented is null))
            {
                if (app.Process.HasExited)
                {
                    break;
                }

                var signals = new List<Signal>();
                Bus.WaitSignal(_ => false, Stopwatch.StartNew(), 0, signals);
                all.AddRange(signals);
                foreach (var signal in signals)
                {
                    if (evented is not null)
                    {
                        break;
                    }

                    if (!owners.TryGetValue(signal.Sender, out var ours))
                    {
                        owners[signal.Sender] = ours = bus.ProcessOf(signal.Sender) == app.Id;
                    }

                    // GTK 4.14 sends a new window's StateChanged (showing, visible, active, focused) and, for a main
                    // window, Window.activate; the registry announces the application itself (ChildrenChanged on its
                    // root, from the registry's name), which is not a window yet.
                    var relevant = signal.Interface.EndsWith(".Window", StringComparison.Ordinal)
                        || (signal.Member == "StateChanged" && (signal.Detail.StartsWith("showing", StringComparison.Ordinal)
                            || signal.Detail.StartsWith("active", StringComparison.Ordinal) || signal.Detail.StartsWith("visible", StringComparison.Ordinal)));
                    if (ours && relevant && signal.At >= app.Started)
                    {
                        evented = app.Since(signal.At);
                        kind = $"{signal.Member} {signal.Detail.Split(' ')[0]}";
                    }
                }

                if (polled is null)
                {
                    foreach (var (node, nodeRole, name) in Reading.WindowsOf(bus, app.Id))
                    {
                        if (accept(nodeRole, name))
                        {
                            polled = app.Elapsed;
                            window = node;
                            role = nodeRole;
                            title = name;
                            break;
                        }
                    }
                }

                if (viewable is null)
                {
                    foreach (var w in X11.WindowsOf(x11, app.Id))
                    {
                        if (w.Viewable && (knownX11 is null || !knownX11.Contains(w.Window)))
                        {
                            viewable = app.Elapsed;
                            x11Window = w.Window;
                            break;
                        }
                    }
                }

                // Events can be missing (none came for this window): stop waiting for them once the rest is in.
                if (polled is not null && viewable is not null && evented is null && app.Elapsed > polled + 500)
                {
                    break;
                }

                Thread.Sleep(PollMs);
            }
        }
        finally
        {
            X11.CloseDisplay(x11);
        }

        if (evented is null && Environment.GetEnvironmentVariable("SPIKE_DEBUG") == "1")
        {
            foreach (var signal in all)
            {
                Console.WriteLine($"  signal +{app.Since(signal.At):0.0} {signal.Sender} (pid {bus.ProcessOf(signal.Sender)}) {signal.Path} {signal.Interface}.{signal.Member} {signal.Detail}");
            }
        }

        return new Seen(polled, evented, kind, viewable, window, role, title, x11Window);
    }

    public static string Show(double? value) => value?.ToString("0.0") ?? "-";
}
