using Microsoft.Extensions.Logging;
using Spar.Hosting;

namespace Spar.Tests;

/// <summary>
/// The UI-thread stall probe (plan section 3, "The UI thread is never blocked"), driven by a clock and a timer the test
/// moves: what counts as a stall, which callback it names, and what window tests can read.
/// </summary>
public sealed class UiStallProbeTests
{
    private readonly ProbeTime _time = new();
    private readonly CapturedLog _log = new();
    private readonly Queue<Action> _queue = new();

    [Fact]
    public void Work_that_runs_promptly_is_no_stall()
    {
        var probe = Started();

        for (var round = 0; round < 10; round++)
        {
            _time.Fire();
            _time.Advance(TimeSpan.FromMilliseconds(UiStallProbe.WarnAboveMilliseconds));
            RunQueue();
        }

        Assert.Equal(0, probe.Count);
        Assert.Empty(_log.Entries);
        Assert.Equal(10, probe.Ticks);
    }

    [Fact]
    public void A_late_probe_is_a_stall_outside_Spars_callbacks_when_none_ran_long()
    {
        var probe = Started();
        _time.Fire();
        var started = probe.Now();
        _time.Advance(TimeSpan.FromMilliseconds(5));
        probe.Ran("WebMessageReceived", started, probe.Ticks);
        _time.Advance(TimeSpan.FromMilliseconds(115));

        RunQueue();

        var stall = Assert.Single(probe.Recent());
        Assert.Null(stall.Callback);
        Assert.Equal(120, stall.Length.TotalMilliseconds);
        var entry = Assert.Single(_log.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal("The UI thread stalled 120 ms outside Spar's callbacks", entry.Message);
    }

    [Fact]
    public void The_slowest_Spar_callback_meanwhile_is_named_with_its_own_time()
    {
        var probe = Started();
        Callback(probe, "NavigationStarting", 10);
        _time.Fire();
        Callback(probe, "WebResourceRequested", 300);
        Callback(probe, "heartbeat", 1);

        RunQueue();

        var stall = Assert.Single(probe.Recent());
        Assert.Equal("WebResourceRequested", stall.Callback);
        Assert.Equal(TimeSpan.FromMilliseconds(301), stall.Delay);
        Assert.Equal(TimeSpan.FromMilliseconds(300), stall.CallbackDuration);
        Assert.True(stall.Length.TotalMilliseconds > UiStallProbe.WindowTestLimitMilliseconds);
        Assert.Equal(stall.Length, probe.Longest);
        Assert.Contains("the slowest Spar callback meanwhile was WebResourceRequested (300 ms)", Assert.Single(_log.Entries).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_callback_that_ran_a_modal_loop_while_the_probe_kept_running_is_no_stall()
    {
        var probe = Started();
        var started = probe.Now();
        var ticks = probe.Ticks;

        // A modal dialog inside the callback: the thread pumps, so the probe keeps running on time.
        for (var round = 0; round < 5; round++)
        {
            _time.Fire();
            _time.Advance(TimeSpan.FromMilliseconds(10));
            RunQueue();
            _time.Advance(TimeSpan.FromMilliseconds(500));
        }

        probe.Ran("await continuation", started, ticks);
        _time.Fire();
        RunQueue();

        Assert.Equal(0, probe.Count);
    }

    [Fact]
    public void A_nested_callback_that_took_most_of_the_time_is_the_one_named()
    {
        var probe = Started();
        _time.Fire();
        var outer = probe.Now();
        _time.Advance(TimeSpan.FromMilliseconds(4));
        Callback(probe, "development stall trigger", 300);
        _time.Advance(TimeSpan.FromMilliseconds(3));
        probe.Ran("bridge ui-ready", outer, probe.Ticks);

        RunQueue();

        var stall = Assert.Single(probe.Recent());
        Assert.Equal(("development stall trigger", TimeSpan.FromMilliseconds(300)), (stall.Callback, stall.CallbackDuration));
    }

    [Fact]
    public void A_stall_found_by_the_callback_alone_still_counts()
    {
        var probe = Started();
        Callback(probe, "development stall trigger", 400);
        _time.Fire();
        _time.Advance(TimeSpan.FromMilliseconds(2));

        RunQueue();

        var stall = Assert.Single(probe.Recent());
        Assert.Equal(TimeSpan.FromMilliseconds(400), stall.Length);
        Assert.Equal("development stall trigger", stall.Callback);
    }

    [Fact]
    public void Only_one_probe_is_queued_at_a_time_and_stop_ends_it()
    {
        var probe = Started();

        _time.Fire();
        _time.Fire();
        Assert.Single(_queue);
        Assert.False(_time.Armed);
        RunQueue();
        Assert.True(_time.Armed);

        probe.Stop();
        _time.Fire();
        Assert.Empty(_queue);
        Assert.True(_time.Disposed);
    }

    [Fact]
    public void The_stalls_window_tests_read_are_kept_and_raised()
    {
        var probe = Started();
        var raised = new List<UiStall>();
        probe.Stalled += raised.Add;

        for (var round = 0; round < 70; round++)
        {
            _time.Fire();
            _time.Advance(TimeSpan.FromMilliseconds(60 + round));
            RunQueue();
        }

        Assert.Equal(70, probe.Count);
        Assert.Equal(70, raised.Count);
        Assert.Equal(64, probe.Recent().Count);
        Assert.Equal(TimeSpan.FromMilliseconds(129), probe.Longest);
    }

    private UiStallProbe Started()
    {
        var probe = new UiStallProbe(_log, _time);
        probe.Start(_queue.Enqueue);
        return probe;
    }

    /// <summary>A Spar callback on the UI thread that runs <paramref name="milliseconds"/>.</summary>
    private void Callback(UiStallProbe probe, string name, int milliseconds)
    {
        var started = probe.Now();
        var ticks = probe.Ticks;
        _time.Advance(TimeSpan.FromMilliseconds(milliseconds));
        probe.Ran(name, started, ticks);
    }

    private void RunQueue()
    {
        while (_queue.TryDequeue(out var work))
        {
            work();
        }
    }

    /// <summary>A clock and one timer that move only when the test moves them.</summary>
    private sealed class ProbeTime : TimeProvider
    {
        private long _ticks;
        private ProbeTimer? _timer;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public bool Armed => _timer?.Armed == true;

        public bool Disposed => _timer?.Disposed == true;

        public override long GetTimestamp() => _ticks;

        public void Advance(TimeSpan by) => _ticks += by.Ticks;

        /// <summary>The timer's due time came: its callback runs if it is armed, as a one-shot timer's would.</summary>
        public void Fire() => _timer?.Fire();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _timer = new ProbeTimer(callback, state) { Armed = dueTime != Timeout.InfiniteTimeSpan };
            return _timer;
        }

        private sealed class ProbeTimer(TimerCallback callback, object? state) : ITimer
        {
            public bool Armed { get; set; }

            public bool Disposed { get; private set; }

            public void Fire()
            {
                if (!Armed || Disposed)
                {
                    return;
                }

                Armed = false;
                callback(state);
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                Armed = !Disposed && dueTime != Timeout.InfiniteTimeSpan;
                return !Disposed;
            }

            public void Dispose() => Disposed = true;

            public ValueTask DisposeAsync()
            {
                Disposed = true;
                return ValueTask.CompletedTask;
            }
        }
    }
}
