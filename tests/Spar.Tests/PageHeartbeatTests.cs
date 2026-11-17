using Spar.Hosting;

namespace Spar.Tests;

/// <summary>
/// The hung-page heartbeat's rules (plan section 3, "Hung pages"; decision "What "Wait" does after the hung-page
/// dialog"), as every host applies them: one ping outstanding, three misses, the visibility gate, the Wait schedule,
/// and one state for the heartbeat's alarm and the engine's report.
/// </summary>
public sealed class PageHeartbeatTests
{
    private long _now = 1_000;

    [Fact]
    public void An_unarmed_document_is_never_pinged_and_never_alarms()
    {
        var heartbeat = new PageHeartbeat();

        for (var tick = 0; tick < 20; tick++)
        {
            Assert.Equal(HeartbeatAction.None, Tick(heartbeat));
        }

        Assert.False(heartbeat.IsUnresponsive);
    }

    [Fact]
    public void A_page_that_answers_every_ping_is_pinged_once_per_tick_and_never_alarms()
    {
        var heartbeat = Armed();

        for (var tick = 0; tick < 50; tick++)
        {
            Assert.Equal(HeartbeatAction.Ping, Tick(heartbeat));
            Assert.Equal(HeartbeatAction.None, heartbeat.Answered(heartbeat.Outstanding));
        }

        Assert.False(heartbeat.IsUnresponsive);
    }

    [Fact]
    public void Three_missed_ticks_make_the_page_unresponsive_with_one_ping_outstanding()
    {
        var heartbeat = Armed();
        Assert.Equal(HeartbeatAction.Ping, Tick(heartbeat));
        var pingedAt = _now;
        var seq = heartbeat.Outstanding;

        Assert.Equal(HeartbeatAction.None, Tick(heartbeat));
        Assert.Equal(HeartbeatAction.None, Tick(heartbeat));
        Assert.Equal(HeartbeatAction.Unresponsive, Tick(heartbeat));

        Assert.True(heartbeat.IsUnresponsive);
        Assert.True(heartbeat.IsAsking);
        Assert.Equal(pingedAt, heartbeat.HangSince);
        Assert.Equal(seq, heartbeat.Outstanding);
        for (var tick = 0; tick < 100; tick++)
        {
            Assert.NotEqual(HeartbeatAction.Ping, Tick(heartbeat));
        }

        Assert.Equal(seq, heartbeat.Outstanding);
    }

    [Fact]
    public void Finite_work_shorter_than_three_ticks_does_not_alarm()
    {
        var heartbeat = Armed();
        Assert.Equal(HeartbeatAction.Ping, Tick(heartbeat));

        Assert.Equal(HeartbeatAction.None, Tick(heartbeat));
        Assert.Equal(HeartbeatAction.None, Tick(heartbeat));
        Assert.Equal(HeartbeatAction.None, heartbeat.Answered(heartbeat.Outstanding));
        Assert.Equal(HeartbeatAction.Ping, Tick(heartbeat));
        Assert.Equal(HeartbeatAction.None, Tick(heartbeat));
        Assert.Equal(HeartbeatAction.None, Tick(heartbeat));

        Assert.False(heartbeat.IsUnresponsive);
    }

    [Fact]
    public void A_hidden_window_counts_no_misses_and_forgets_its_ping()
    {
        var heartbeat = Armed();
        Assert.Equal(HeartbeatAction.Ping, Tick(heartbeat));
        Assert.Equal(HeartbeatAction.None, Tick(heartbeat));

        for (var tick = 0; tick < 30; tick++)
        {
            Assert.Equal(HeartbeatAction.None, Tick(heartbeat, visible: false));
        }

        Assert.Equal(0, heartbeat.Outstanding);
        Assert.Equal(HeartbeatAction.Ping, Tick(heartbeat));
        Assert.Equal(HeartbeatAction.None, Tick(heartbeat));
        Assert.Equal(HeartbeatAction.None, Tick(heartbeat));
        Assert.Equal(HeartbeatAction.Unresponsive, Tick(heartbeat));
    }

    [Fact]
    public void After_Wait_the_dialog_comes_back_after_20_40_80_then_every_160_seconds()
    {
        var heartbeat = Unresponsive();
        var asked = new List<int>();

        heartbeat.Waited();
        var sinceWait = 0;
        while (asked.Count < 6)
        {
            sinceWait++;
            if (Tick(heartbeat) == HeartbeatAction.AskAgain)
            {
                asked.Add(sinceWait * PageHeartbeat.IntervalMilliseconds / 1000);
                sinceWait = 0;
                heartbeat.Waited();
            }
        }

        Assert.Equal([20, 40, 80, 160, 160, 160], asked);
    }

    [Fact]
    public void While_the_dialog_is_open_it_is_not_asked_again()
    {
        var heartbeat = Unresponsive();

        for (var tick = 0; tick < 100; tick++)
        {
            Assert.Equal(HeartbeatAction.None, Tick(heartbeat));
        }
    }

    [Fact]
    public void An_answer_ends_the_hang_and_resets_the_schedule()
    {
        var heartbeat = Unresponsive();
        heartbeat.Waited();

        Assert.Equal(HeartbeatAction.Recovered, heartbeat.Answered(heartbeat.Outstanding));

        Assert.False(heartbeat.IsUnresponsive);
        Assert.False(heartbeat.IsAsking);
        Assert.Equal(HeartbeatAction.Ping, Tick(heartbeat));
        Assert.Equal(HeartbeatAction.None, Tick(heartbeat));
        Assert.Equal(HeartbeatAction.None, Tick(heartbeat));
        Assert.Equal(HeartbeatAction.Unresponsive, Tick(heartbeat));
        heartbeat.Waited();
        var ticks = 0;
        while (Tick(heartbeat) != HeartbeatAction.AskAgain)
        {
            ticks++;
        }

        Assert.Equal(PageHeartbeat.AskAgainAfterTicks(0) - 1, ticks);
    }

    [Fact]
    public void The_engines_report_and_the_heartbeat_are_one_state_with_one_dialog()
    {
        var heartbeat = Armed();
        Assert.Equal(HeartbeatAction.Ping, Tick(heartbeat));

        Assert.Equal(HeartbeatAction.Unresponsive, heartbeat.ReportedUnresponsive(_now));
        Assert.Equal(HeartbeatAction.None, Tick(heartbeat));
        Assert.Equal(HeartbeatAction.None, Tick(heartbeat));
        Assert.Equal(HeartbeatAction.None, heartbeat.ReportedUnresponsive(_now));
        heartbeat.Waited();
        Assert.Equal(HeartbeatAction.None, heartbeat.ReportedUnresponsive(_now));

        Assert.Equal(HeartbeatAction.Recovered, heartbeat.Answered(heartbeat.Outstanding));
    }

    [Fact]
    public void An_engine_report_without_a_ping_out_sends_one_to_see_the_recovery()
    {
        var heartbeat = Armed();

        Assert.Equal(HeartbeatAction.Unresponsive, heartbeat.ReportedUnresponsive(_now));
        Assert.Equal(_now, heartbeat.HangSince);
        Assert.Equal(HeartbeatAction.Ping, Tick(heartbeat));
        Assert.Equal(HeartbeatAction.Recovered, heartbeat.Answered(heartbeat.Outstanding));
    }

    [Fact]
    public void A_ping_out_of_turn_serves_a_close_request_and_keeps_one_outstanding()
    {
        var heartbeat = Armed();

        var seq = heartbeat.PingNow(_now);
        Assert.NotNull(seq);
        Assert.Null(heartbeat.PingNow(_now));
        Assert.Equal(HeartbeatAction.None, Tick(heartbeat));

        Assert.Equal(HeartbeatAction.None, heartbeat.Answered(seq.Value));
        Assert.Equal(0, heartbeat.Outstanding);
        Assert.Null(new PageHeartbeat().PingNow(_now));
    }

    [Fact]
    public void Answers_never_sent_or_from_a_disarmed_document_are_ignored()
    {
        var heartbeat = Unresponsive();

        Assert.Equal(HeartbeatAction.None, heartbeat.Answered(heartbeat.Outstanding + 1));
        Assert.Equal(HeartbeatAction.None, heartbeat.Answered(0));
        Assert.True(heartbeat.IsUnresponsive);

        var seq = heartbeat.Outstanding;
        heartbeat.Disarm();
        Assert.Equal(HeartbeatAction.None, heartbeat.Answered(seq));
        Assert.False(heartbeat.IsUnresponsive);
    }

    [Fact]
    public void A_late_answer_to_a_forgotten_ping_still_shows_the_page_is_alive()
    {
        var heartbeat = Armed();
        Assert.Equal(HeartbeatAction.Ping, Tick(heartbeat));
        var forgotten = heartbeat.Outstanding;
        Assert.Equal(HeartbeatAction.None, Tick(heartbeat, visible: false));
        Assert.Equal(HeartbeatAction.Ping, Tick(heartbeat));
        Assert.Equal(HeartbeatAction.None, Tick(heartbeat));

        Assert.Equal(HeartbeatAction.None, heartbeat.Answered(forgotten));
        Assert.Equal(HeartbeatAction.None, Tick(heartbeat));
        Assert.Equal(HeartbeatAction.None, Tick(heartbeat));

        Assert.False(heartbeat.IsUnresponsive);
    }

    private HeartbeatAction Tick(PageHeartbeat heartbeat, bool visible = true)
    {
        _now += PageHeartbeat.IntervalMilliseconds;
        return heartbeat.Tick(visible, _now);
    }

    private static PageHeartbeat Armed()
    {
        var heartbeat = new PageHeartbeat();
        heartbeat.Arm();
        return heartbeat;
    }

    private PageHeartbeat Unresponsive()
    {
        var heartbeat = Armed();
        Assert.Equal(HeartbeatAction.Ping, Tick(heartbeat));
        Assert.Equal(HeartbeatAction.None, Tick(heartbeat));
        Assert.Equal(HeartbeatAction.None, Tick(heartbeat));
        Assert.Equal(HeartbeatAction.Unresponsive, Tick(heartbeat));
        return heartbeat;
    }
}
