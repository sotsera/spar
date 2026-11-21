using Microsoft.Extensions.Logging;

namespace Spar.Hosting;

/// <summary>
/// The window's UI thread, as the stall probe sees it: the host calls <see cref="Ran"/> as each of its callbacks on that
/// thread ends (<c>UiThread.Enter</c> on Windows). Production builds keep only this interface and the calls, which do
/// nothing without a probe, unless the app keeps the probe (<see cref="DiagnosticsOptions.KeepStallProbe"/>).
/// </summary>
internal interface IUiThreadWatch
{
    /// <summary>How many times the probe's own work has run on the UI thread; a callback during which it ran pumped.</summary>
    long Ticks { get; }

    /// <summary>The probe's clock, for a callback's start.</summary>
    long Now();

    /// <summary>A Spar callback named <paramref name="name"/> ran on the UI thread from <paramref name="started"/>.</summary>
    void Ran(string name, long started, long ticksAtStart);

    /// <summary>Starts probing; <paramref name="post"/> queues work on the UI thread behind what is already there.</summary>
    void Start(Action<Action> post);

    void Stop();
}

/// <summary>
/// One Spar callback on a window's UI thread, from the host's <c>Enter</c> until it is disposed: the stall probe learns
/// how long it ran. A callback during which the thread pumped (a modal dialog's loop) is not counted as a stall.
/// </summary>
internal readonly ref struct CallbackScope
{
    private readonly IUiThreadWatch? _probe;
    private readonly string? _name;
    private readonly long _started;
    private readonly long _ticks;

    public CallbackScope(IUiThreadWatch probe, string name)
    {
        _probe = probe;
        _name = name;
        _started = probe.Now();
        _ticks = probe.Ticks;
    }

    public void Dispose() => _probe?.Ran(_name!, _started, _ticks);
}

/// <summary>One stall of a window's UI thread: how late the probe's work ran, and the slowest Spar callback meanwhile.</summary>
/// <param name="Delay">How long the probe's work waited in the UI thread's queue.</param>
/// <param name="Callback">The slowest Spar callback that ran meanwhile; null when the time went outside Spar's callbacks.</param>
/// <param name="CallbackDuration">How long that callback ran.</param>
internal readonly record struct UiStall(TimeSpan Delay, string? Callback, TimeSpan CallbackDuration)
{
    /// <summary>The stall's length: the probe's delay, or the callback's own time when the probe was posted late in it.</summary>
    public TimeSpan Length => Delay > CallbackDuration ? Delay : CallbackDuration;
}

/// <summary>
/// The UI-thread stall probe (plan section 3, "The UI thread is never blocked"; decision "M1b's remaining questions
/// without a spike"): a timer posts the probe's work through the UI thread's queue, one at a time, and when it runs
/// more than <see cref="WarnAboveMilliseconds"/> late the probe logs a warning naming the slowest Spar callback that ran
/// meanwhile. Development and Test builds always have it; a Production build only when the app keeps it, and then it
/// only logs. Window tests read <see cref="Longest"/> and fail on a stall over <see cref="WindowTestLimitMilliseconds"/>.
/// </summary>
internal sealed class UiStallProbe : IUiThreadWatch
{
    /// <summary>A delay above this is logged as a stall.</summary>
    public const int WarnAboveMilliseconds = 50;

    /// <summary>The longest stall a window test accepts, until M4 sets the budget from measurements.</summary>
    public const int WindowTestLimitMilliseconds = 250;

    /// <summary>The pause between one probe's run and the next post; the timer's own resolution (about 16 ms) adds to it.</summary>
    private const int PeriodMilliseconds = 20;

    /// <summary>How many of the latest stalls are kept for tests.</summary>
    private const int KeptStalls = 64;

    private static readonly Action<ILogger, double, string, double, Exception?> StalledInCallback = LoggerMessage.Define<double, string, double>(
        LogLevel.Warning, new EventId(40, "UiThreadStalled"), "The UI thread stalled {Milliseconds} ms; the slowest Spar callback meanwhile was {Callback} ({CallbackMilliseconds} ms)");

    private static readonly Action<ILogger, double, Exception?> StalledOutside = LoggerMessage.Define<double>(
        LogLevel.Warning, new EventId(41, "UiThreadStalledOutsideSpar"), "The UI thread stalled {Milliseconds} ms outside Spar's callbacks");

    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly Action _check;
    private readonly Lock _gate = new();
    private readonly Queue<UiStall> _recent = new();
    private Action<Action>? _post;
    private ITimer? _timer;
    private long _postedAt;
    private long _ticks;
    private string? _slowest;
    private long _slowestStarted;
    private long _slowestElapsed;
    private TimeSpan _longest;
    private int _count;
    private volatile bool _stopped;

    public UiStallProbe(ILogger logger, TimeProvider time)
    {
        _logger = logger;
        _time = time;
        _check = Check;
    }

    /// <summary>Raised on the UI thread for each stall, after it is logged.</summary>
    public event Action<UiStall>? Stalled;

    public long Ticks => _ticks;

    /// <summary>The longest stall seen so far.</summary>
    public TimeSpan Longest
    {
        get
        {
            lock (_gate)
            {
                return _longest;
            }
        }
    }

    /// <summary>How many stalls were seen so far.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _count;
            }
        }
    }

    /// <summary>The latest stalls, oldest first.</summary>
    public IReadOnlyList<UiStall> Recent()
    {
        lock (_gate)
        {
            return [.. _recent];
        }
    }

    public long Now() => _time.GetTimestamp();

    public void Start(Action<Action> post)
    {
        _post = post;
        _timer = _time.CreateTimer(static probe => ((UiStallProbe)probe!).Post(), this, TimeSpan.FromMilliseconds(PeriodMilliseconds), Timeout.InfiniteTimeSpan);
    }

    public void Stop()
    {
        _stopped = true;
        _timer?.Dispose();
    }

    /// <summary>
    /// On the UI thread, as a callback ends. One during which the probe ran (a modal dialog's loop) did not stall it. A
    /// callback nested in this one that took most of its time keeps its name, being the more precise of the two.
    /// </summary>
    public void Ran(string name, long started, long ticksAtStart)
    {
        if (_ticks != ticksAtStart)
        {
            return;
        }

        var elapsed = _time.GetTimestamp() - started;
        var nestedExplainsIt = _slowest is not null && _slowestStarted >= started && _slowestElapsed * 2 >= elapsed;
        if (elapsed > _slowestElapsed && !nestedExplainsIt)
        {
            _slowestElapsed = elapsed;
            _slowestStarted = started;
            _slowest = name;
        }
    }

    /// <summary>On the timer's thread: the next probe goes into the UI thread's queue, stamped.</summary>
    private void Post()
    {
        if (_stopped)
        {
            return;
        }

        Volatile.Write(ref _postedAt, _time.GetTimestamp());
        _post!(_check);
    }

    /// <summary>On the UI thread: how late the probe ran, and what ran meanwhile.</summary>
    private void Check()
    {
        var delay = _time.GetElapsedTime(Volatile.Read(ref _postedAt));
        _ticks++;
        var callbackDuration = _time.GetElapsedTime(0, _slowestElapsed);

        // A callback is named only when it was long enough to be the stall; short ones are no explanation.
        var callback = callbackDuration.TotalMilliseconds > WarnAboveMilliseconds ? _slowest : null;
        _slowest = null;
        _slowestElapsed = 0;

        var stall = new UiStall(delay, callback, callback is null ? TimeSpan.Zero : callbackDuration);
        if (stall.Length.TotalMilliseconds > WarnAboveMilliseconds)
        {
            Report(stall);
        }

        if (!_stopped)
        {
            _timer?.Change(TimeSpan.FromMilliseconds(PeriodMilliseconds), Timeout.InfiniteTimeSpan);
        }
    }

    private void Report(UiStall stall)
    {
        lock (_gate)
        {
            _count++;
            if (stall.Length > _longest)
            {
                _longest = stall.Length;
            }

            if (_recent.Count == KeptStalls)
            {
                _recent.Dequeue();
            }

            _recent.Enqueue(stall);
        }

        var milliseconds = Math.Round(stall.Length.TotalMilliseconds, 1);
        if (stall.Callback is { } callback)
        {
            StalledInCallback(_logger, milliseconds, callback, Math.Round(stall.CallbackDuration.TotalMilliseconds, 1), null);
        }
        else
        {
            StalledOutside(_logger, milliseconds, null);
        }

        Stalled?.Invoke(stall);
    }
}
