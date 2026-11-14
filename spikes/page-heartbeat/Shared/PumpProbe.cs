using System.Diagnostics;

namespace Spar.PageHeartbeat;

/// <summary>
/// UI-thread stall probe: a dedicated thread posts a callback to the UI synchronization context, waits until it ran, then
/// posts the next after about 1 ms. The delay from post to run is the pump latency. Every <c>windowMs</c> it logs the
/// window's count, median, p99 and max, labelled with the current phase.
/// </summary>
internal sealed class PumpProbe
{
    private readonly SynchronizationContext _ui;
    private readonly int _windowMs;
    private readonly List<double> _window = [];
    private readonly Dictionary<string, List<double>> _phases = [];
    private readonly Lock _lock = new();
    private volatile bool _stop;
    private string _phase = "start";

    public PumpProbe(SynchronizationContext ui, int windowMs)
    {
        _ui = ui;
        _windowMs = windowMs;
    }

    public string Phase
    {
        get => _phase;
        set
        {
            lock (_lock)
            {
                _phase = value;
            }
        }
    }

    public void Start() => new Thread(Run) { IsBackground = true, Name = "pump probe" }.Start();

    public void Stop()
    {
        _stop = true;
        lock (_lock)
        {
            foreach (var (phase, values) in _phases)
            {
                Log.Line("PUMP-PHASE", $"phase={phase} {Stats.Of(values)}");
            }
        }
    }

    private void Run()
    {
        using var ran = new ManualResetEventSlim();
        var windowStart = Stopwatch.GetTimestamp();
        while (!_stop)
        {
            ran.Reset();
            var posted = Stopwatch.GetTimestamp();
            double delay = 0;
            _ui.Post(_ =>
            {
                delay = Stopwatch.GetElapsedTime(posted).TotalMilliseconds;
                ran.Set();
            }, null);
            if (!ran.Wait(TimeSpan.FromSeconds(30)))
            {
                Log.Line("PUMP-STALL", "noRunFor=30000ms");
                continue;
            }

            lock (_lock)
            {
                _window.Add(delay);
                if (!_phases.TryGetValue(_phase, out var list))
                {
                    _phases[_phase] = list = [];
                }

                list.Add(delay);
                if (Stopwatch.GetElapsedTime(windowStart).TotalMilliseconds >= _windowMs)
                {
                    Log.Line("PUMP", $"phase={_phase} {Stats.Of(_window)}");
                    _window.Clear();
                    windowStart = Stopwatch.GetTimestamp();
                }
            }

            Thread.Sleep(1);
        }
    }
}
