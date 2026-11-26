using System.Diagnostics;
using System.Globalization;

namespace Spar.Testing;

/// <summary>
/// A scene's clock (decision "M2c's decide-first questions", item 4): it reads the scene's instant when the scene
/// starts and runs in real time from there, so timers and timeouts work and a page's "edited 5 minutes ago" reads the
/// same every run. Timers are the system's; nothing is frozen or fast-forwarded.
/// </summary>
internal sealed class SceneClock : TimeProvider
{
    private readonly DateTimeOffset _start;
    private readonly long _startedAt = Stopwatch.GetTimestamp();

    public SceneClock(DateTimeOffset start)
    {
        _start = start;
        RealStart = DateTimeOffset.UtcNow;
    }

    /// <summary>The real time the clock started, which the page's clock counts from too.</summary>
    public DateTimeOffset RealStart { get; }

    public override DateTimeOffset GetUtcNow() => _start + Stopwatch.GetElapsedTime(_startedAt);

    /// <summary>
    /// The page's half: a script of the page's own world, run at the start of every document, whose <c>Date</c> and
    /// <c>Date.now</c> give the scene's instant plus the real time since the scene started, so a navigation does not
    /// start the clock again. <c>Date</c> with arguments, <c>Date.parse</c> and <c>Date.UTC</c> are the real ones, and
    /// <c>instanceof Date</c> still holds; <c>Intl</c>'s own "now" and <c>performance.now()</c> are not changed.
    /// </summary>
    public string PageScript()
    {
        var start = _start.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        var realStart = RealStart.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        return $$"""
            (() => {
              const Real = Date;
              const now = () => {{start}} + (Real.now() - {{realStart}});
              function SceneDate(...args) {
                if (!new.target) {
                  return new Real(now()).toString();
                }

                return Reflect.construct(Real, args.length === 0 ? [now()] : args, new.target);
              }

              SceneDate.prototype = Real.prototype;
              SceneDate.now = now;
              SceneDate.parse = Real.parse;
              SceneDate.UTC = Real.UTC;
              Object.defineProperty(Real.prototype, "constructor", { value: SceneDate, writable: true, configurable: true });
              globalThis.Date = SceneDate;
            })();
            """;
    }
}

/// <summary>A scene's page settings, for the Window surface: the clock script and the fixed viewport.</summary>
internal sealed record ScenePage(string PageScript, (int Width, int Height) Viewport);
