using System.Diagnostics;

namespace Spar.Hosting;

/// <summary>
/// The rate limit of the <c>report-uri</c> endpoint (plan section 3b, "Flood limits"): a token bucket that lets a
/// burst of <see cref="Burst"/> reports through, then <see cref="PerSecond"/> a second; a report past it is answered
/// without being read. Used from one thread, the one that answers requests.
/// </summary>
internal sealed class ReportLimiter
{
    /// <summary>The reports a page may send at once, as when it loads with a few violations.</summary>
    public const int Burst = 20;

    /// <summary>The reports a second after the burst; the browser sends one per distinct violation.</summary>
    public const int PerSecond = 5;

    private double _tokens = Burst;
    private long _last;

    /// <summary>Takes one report's place at <paramref name="timestamp"/> (a <see cref="Stopwatch"/> timestamp); false past the limit.</summary>
    public bool TryTake(long timestamp)
    {
        if (_last != 0)
        {
            var seconds = (double)(timestamp - _last) / Stopwatch.Frequency;
            _tokens = Math.Min(Burst, _tokens + (seconds * PerSecond));
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
