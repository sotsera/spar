using System.Diagnostics;

namespace Spar.PageHeartbeat;

/// <summary>
/// The design under test, host-neutral. While armed (a document exists) and not paused (the window is not hidden), every
/// tick posts <c>{prefix}:{seq}</c>; the injected script answers <c>{prefix}-pong:{seq}:{visibility}</c>. A tick that
/// finds the previous ping unanswered counts a miss; <see cref="MissLimit"/> consecutive misses mark the page
/// unresponsive. Any answer resets the count and, after an alarm, reports the page responsive again. Every tick runs on
/// the UI thread, posts one short message and never waits.
/// </summary>
internal sealed class Heartbeat
{
    private readonly Func<string, int> _send;
    private readonly Dictionary<long, long> _sentAt = [];
    private long _seq;
    private long _latest;
    private bool _awaiting;
    private int _missed;
    private string _pausedFor = "";

    public Heartbeat(string prefix, int intervalMs, int missLimit, Func<string, int> send)
    {
        Prefix = prefix;
        IntervalMs = intervalMs;
        MissLimit = missLimit;
        _send = send;
    }

    public event Action<int>? Unresponsive;

    public event Action<double>? Responsive;

    public string Prefix { get; }

    public int IntervalMs { get; }

    public int MissLimit { get; }

    public bool Armed { get; private set; }

    public bool IsUnresponsive { get; private set; }

    public double UnresponsiveSince { get; private set; }

    public void Arm(string reason)
    {
        Armed = true;
        _awaiting = false;
        _missed = 0;
        Log.Line("HB-ARM", $"hb={Prefix} reason={reason}");
    }

    public void Disarm(string reason)
    {
        if (Armed)
        {
            Armed = false;
            _awaiting = false;
            _missed = 0;
            Log.Line("HB-DISARM", $"hb={Prefix} reason={reason}");
        }
    }

    /// <summary>Pauses pinging (hidden or minimised window); misses do not span a pause.</summary>
    public void SetPaused(string reason)
    {
        if (reason == _pausedFor)
        {
            return;
        }

        Log.Line(reason.Length > 0 ? "HB-PAUSE" : "HB-RESUME", $"hb={Prefix} reason={(reason.Length > 0 ? reason : _pausedFor)}");
        _pausedFor = reason;
        _awaiting = false;
        _missed = 0;
    }

    public void Tick()
    {
        if (!Armed || _pausedFor.Length > 0)
        {
            return;
        }

        if (_awaiting)
        {
            _missed++;
            Log.Line("HB-MISS", $"hb={Prefix} seq={_latest} missed={_missed}");
            if (_missed == MissLimit && !IsUnresponsive)
            {
                IsUnresponsive = true;
                UnresponsiveSince = Log.Now;
                Log.Line("HB-UNRESPONSIVE", $"hb={Prefix} missed={_missed} intervalMs={IntervalMs}");
                Unresponsive?.Invoke(_missed);
            }
        }

        _latest = ++_seq;
        var started = Stopwatch.GetTimestamp();
        var hr = _send($"{Prefix}:{_latest}");
        var postUs = Stopwatch.GetElapsedTime(started).TotalMicroseconds;
        _sentAt[_latest] = Stopwatch.GetTimestamp();
        _awaiting = true;
        Log.Line("HB-PING", $"hb={Prefix} seq={_latest} postUs={postUs:F0} hr=0x{hr:X8} outstanding={_sentAt.Count}");
    }

    public void OnPong(long seq, string visibility)
    {
        var rtt = _sentAt.Remove(seq, out var sent) ? Stopwatch.GetElapsedTime(sent).TotalMilliseconds : double.NaN;

        // Answers arrive in order; older pings still in the table were answered in the same burst or lost.
        foreach (var old in _sentAt.Keys.Where(k => k < seq).ToList())
        {
            _sentAt.Remove(old);
        }

        Log.Line("HB-PONG", $"hb={Prefix} seq={seq} rttMs={rtt:F1} vis={visibility} latest={seq == _latest}");
        if (seq == _latest)
        {
            _awaiting = false;
        }

        _missed = 0;
        if (IsUnresponsive)
        {
            IsUnresponsive = false;
            var lasted = Log.Now - UnresponsiveSince;
            Log.Line("HB-RESPONSIVE", $"hb={Prefix} afterMs={lasted:F0}");
            Responsive?.Invoke(lasted);
        }
    }

    /// <summary>Drives <see cref="Tick"/> on the caller's synchronization context (the UI thread).</summary>
    public async Task RunAsync(CancellationToken cancellation)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(IntervalMs));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellation))
            {
                Tick();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
