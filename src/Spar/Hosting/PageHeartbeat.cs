namespace Spar.Hosting;

/// <summary>What a host does after an input to <see cref="PageHeartbeat"/>.</summary>
internal enum HeartbeatAction
{
    None,

    /// <summary>Send a <c>ping</c> with <see cref="PageHeartbeat.Outstanding"/> as its <c>seq</c>.</summary>
    Ping,

    /// <summary>The page has just become unresponsive: show the dialog.</summary>
    Unresponsive,

    /// <summary>The user chose Wait earlier, and the page still does not answer: show the dialog again.</summary>
    AskAgain,

    /// <summary>The page answered again: close an open dialog.</summary>
    Recovered,
}

/// <summary>
/// The rules of the hung-page heartbeat (plan section 3, "Hung pages"), for every host and without timers or windows,
/// so they can be tested on their own. The host calls <see cref="Tick"/> every <see cref="IntervalMilliseconds"/> on the
/// window's UI thread, and reports answers and the engine's own reports. At most one ping is outstanding, always: a
/// tick that finds it unanswered counts a miss, and three misses in a row (15–20 s after the page stopped) make the
/// page unresponsive. A window that is not visible is not pinged and counts no misses. After the user chooses Wait, the
/// dialog comes back after 20 s, then 40 s, 80 s and every 160 s while the page stays silent (decision "What "Wait"
/// does after the hung-page dialog"). Any answer ends the hang.
/// </summary>
internal sealed class PageHeartbeat
{
    /// <summary>The time between pings (plan section 3: 5 s × 3 misses, confirmed by <c>docs/evidence/page-heartbeat</c>).</summary>
    public const int IntervalMilliseconds = 5_000;

    /// <summary>Unanswered ticks in a row before the page counts as unresponsive.</summary>
    public const int MissesBeforeUnresponsive = 3;

    private long _lastSeq;
    private long _outstandingSince;
    private int _misses;
    private int _ticksSinceWait;
    private int _nextAskAfter;
    private int _waits;

    /// <summary>Whether the current document answers pings (its <c>hello</c> listed <c>ping</c>).</summary>
    public bool IsArmed { get; private set; }

    /// <summary>The <c>seq</c> of the ping waiting for its answer; 0 when none is.</summary>
    public long Outstanding { get; private set; }

    /// <summary>Whether the page counts as unresponsive, from the first alarm until it answers or goes away.</summary>
    public bool IsUnresponsive { get; private set; }

    /// <summary>Whether the host's dialog is open for this hang, between an alarm and the user's Wait.</summary>
    public bool IsAsking { get; private set; }

    /// <summary>When the hang was first seen: the send time of the first ping it missed, or the report's time.</summary>
    public long HangSince { get; private set; }

    /// <summary>How many visible ticks after a Wait the dialog comes back: 20, 40, 80 s, then every 160 s at 5 s a tick.</summary>
    public static int AskAgainAfterTicks(int waits) => waits switch
    {
        0 => 4,
        1 => 8,
        2 => 16,
        _ => 32,
    };

    /// <summary>A document that answers pings is loaded: the count starts again for it.</summary>
    public void Arm()
    {
        Reset();
        IsArmed = true;
    }

    /// <summary>The document is gone, or never answers pings: nothing is counted until the next one arms it.</summary>
    public void Disarm()
    {
        Reset();
        IsArmed = false;
    }

    /// <summary>One heartbeat interval passed; <paramref name="visible"/> is false for a minimised or hidden window.</summary>
    public HeartbeatAction Tick(bool visible, long now)
    {
        if (!visible)
        {
            // A hidden or suspended page answers late or never, so nothing it misses meanwhile counts. A hang already
            // seen stays, with its schedule paused.
            if (!IsUnresponsive)
            {
                Outstanding = 0;
                _misses = 0;
            }

            return HeartbeatAction.None;
        }

        if (Outstanding == 0)
        {
            return IsArmed ? SendPing(now) : AskAgainIfDue();
        }

        if (IsUnresponsive)
        {
            return AskAgainIfDue();
        }

        _misses++;
        return _misses < MissesBeforeUnresponsive ? HeartbeatAction.None : BecomeUnresponsive(_outstandingSince);
    }

    /// <summary>
    /// A ping out of turn, for a close request that checks whether the page still answers. Null when one is already
    /// outstanding (its answer serves) or the page does not answer pings.
    /// </summary>
    public long? PingNow(long now)
    {
        if (!IsArmed || Outstanding != 0)
        {
            return null;
        }

        _ = SendPing(now);
        return Outstanding;
    }

    /// <summary>The page answered <paramref name="seq"/>. Any answer to a ping that was sent shows the page is alive.</summary>
    public HeartbeatAction Answered(long seq)
    {
        if (!IsArmed || seq < 1 || seq > _lastSeq)
        {
            return HeartbeatAction.None;
        }

        if (seq == Outstanding)
        {
            Outstanding = 0;
        }

        _misses = 0;
        if (!IsUnresponsive)
        {
            return HeartbeatAction.None;
        }

        EndHang();
        return HeartbeatAction.Recovered;
    }

    /// <summary>
    /// The page was found unresponsive another way: the engine's own report (WebView2's
    /// <c>RenderProcessUnresponsive</c>), or a close request it did not answer. One state and one dialog with the
    /// heartbeat's alarm, so a report during a hang already seen changes nothing.
    /// </summary>
    public HeartbeatAction ReportedUnresponsive(long now) =>
        IsUnresponsive ? HeartbeatAction.None : BecomeUnresponsive(Outstanding != 0 ? _outstandingSince : now);

    /// <summary>The user chose Wait: the dialog comes back on the schedule while the page stays silent.</summary>
    public void Waited()
    {
        if (!IsUnresponsive)
        {
            return;
        }

        IsAsking = false;
        _ticksSinceWait = 0;
        _nextAskAfter = AskAgainAfterTicks(_waits);
        _waits++;
    }

    private HeartbeatAction SendPing(long now)
    {
        _lastSeq++;
        Outstanding = _lastSeq;
        _outstandingSince = now;
        return HeartbeatAction.Ping;
    }

    private HeartbeatAction BecomeUnresponsive(long since)
    {
        IsUnresponsive = true;
        IsAsking = true;
        HangSince = since;
        _waits = 0;
        return HeartbeatAction.Unresponsive;
    }

    private HeartbeatAction AskAgainIfDue()
    {
        if (!IsUnresponsive || IsAsking)
        {
            return HeartbeatAction.None;
        }

        _ticksSinceWait++;
        if (_ticksSinceWait < _nextAskAfter)
        {
            return HeartbeatAction.None;
        }

        IsAsking = true;
        return HeartbeatAction.AskAgain;
    }

    private void EndHang()
    {
        IsUnresponsive = false;
        IsAsking = false;
        HangSince = 0;
        _waits = 0;
        _ticksSinceWait = 0;
        _nextAskAfter = 0;
    }

    private void Reset()
    {
        EndHang();
        Outstanding = 0;
        _misses = 0;
    }
}
