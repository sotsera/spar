using System.Diagnostics;

namespace Spar.Hosting;

/// <summary>
/// The rate limit of the <c>report-uri</c> endpoint (plan section 3b, "Flood limits"): a token bucket that lets a
/// burst of <see cref="Burst"/> reports through, then <see cref="PerSecond"/> a second; a report past it is answered
/// without being read. Used from one thread, the one that answers requests. The telemetry relay's route uses one too, with
/// its own numbers.
/// </summary>
internal sealed class ReportLimiter
{
    /// <summary>The reports a page may send at once, as when it loads with a few violations.</summary>
    public const int Burst = 20;

    /// <summary>The reports a second after the burst; the browser sends one per distinct violation.</summary>
    public const int PerSecond = 5;

    private readonly int _burst;
    private readonly int _perSecond;
    private double _tokens;
    private long _last;

    /// <summary>A limiter of <paramref name="burst"/> at once, then <paramref name="perSecond"/> a second (the reports' by default).</summary>
    public ReportLimiter(int burst = Burst, int perSecond = PerSecond)
    {
        _burst = burst;
        _perSecond = perSecond;
        _tokens = burst;
    }

    /// <summary>Takes one report's place at <paramref name="timestamp"/> (a <see cref="Stopwatch"/> timestamp); false past the limit.</summary>
    public bool TryTake(long timestamp)
    {
        if (_last != 0)
        {
            var seconds = (double)(timestamp - _last) / Stopwatch.Frequency;
            _tokens = Math.Min(_burst, _tokens + (seconds * _perSecond));
        }

        _last = timestamp;
        if (_tokens < 1)
        {
            return false;
        }

        _tokens--;
        return true;
    }
}
