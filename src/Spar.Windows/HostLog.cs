using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spar.Runtime;
using Windows.Win32;
using Windows.Win32.Foundation;
using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;

namespace Spar.Windows;

/// <summary>
/// The host's instrumentation (plan section 3, "Instrumentation from the first slice"): a run id, monotonic times from
/// the host's start, and the start-up and shutdown phases, through the app's logging (category <c>Spar.Host</c>) and,
/// when the process has a standard error handle (a console, or a launcher that redirected it), there too. Each phase
/// is logged once, with its time from the start; a later repeat (a recovered browser) is logged again as a new phase.
/// </summary>
internal sealed class HostLog
{
    private static readonly Action<ILogger, string, string, double, Exception?> PhaseReached = LoggerMessage.Define<string, string, double>(
        LogLevel.Information, new EventId(1, "Phase"), "[{RunId}] {Phase} at {Milliseconds} ms");

    private static readonly Action<ILogger, string, string, Exception?> Note = LoggerMessage.Define<string, string>(
        LogLevel.Information, new EventId(2, "Note"), "[{RunId}] {Message}");

    private static readonly Action<ILogger, string, string, Exception?> Warned = LoggerMessage.Define<string, string>(
        LogLevel.Warning, new EventId(3, "Warning"), "[{RunId}] {Message}");

    private static readonly Action<ILogger, string, string, Exception?> Failed = LoggerMessage.Define<string, string>(
        LogLevel.Error, new EventId(4, "Error"), "[{RunId}] {Message}");

    private readonly long _started = Stopwatch.GetTimestamp();
    private readonly Lock _gate = new();
    private readonly HashSet<string> _reached = new(StringComparer.Ordinal);
    private ILogger _logger;
    private LogFile? _file;

    public HostLog()
    {
        RunId = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
        _logger = new LocalLogger(this);
    }

    /// <summary>The run's id, in every line, so one run's lines can be found in a shared log.</summary>
    public string RunId { get; }

    /// <summary>The host's logger: the app's logging and standard error, for the parts of Spar the host runs.</summary>
    public ILogger Logger => _logger;

    /// <summary>Milliseconds since the host started, monotonic.</summary>
    public double Elapsed => Stopwatch.GetElapsedTime(_started).TotalMilliseconds;

    /// <summary>The per-user log file, once <see cref="UseFile"/> opened it.</summary>
    public string? FilePath => _file?.Path;

    /// <summary>
    /// Writes every line to the per-user log file in <paramref name="logsFolder"/> too, from now on (before anything can
    /// fail at start-up).
    /// </summary>
    public void UseFile(string logsFolder) => _file ??= LogFile.Open(logsFolder);

    /// <summary>Ends the log file: what was logged reaches the disk, within a short limit. Called once, at process exit.</summary>
    public void Close() => _file?.Close();

    /// <summary>Adds the app's own logging once its services exist; until then standard error and the file get the lines.</summary>
    public void UseAppLogging(IServiceProvider services)
    {
        var app = services.GetService<ILoggerFactory>()?.CreateLogger("Spar.Host");
        if (app is not null)
        {
            _logger = BothLoggers.Of(app, new LocalLogger(this));
        }
    }

    /// <summary>Logs a phase the first time it is reached; returns whether it was the first time.</summary>
    public bool Phase(string phase)
    {
        lock (_gate)
        {
            if (!_reached.Add(phase))
            {
                return false;
            }
        }

        PhaseReached(_logger, RunId, phase, Math.Round(Elapsed, 1), null);
        return true;
    }

    /// <summary>Logs a phase every time it is reached (shutdown steps, recoveries).</summary>
    public void Step(string phase) => PhaseReached(_logger, RunId, phase, Math.Round(Elapsed, 1), null);

    public void Info(string message) => Note(_logger, RunId, message, null);

    public void Warn(string message) => Warned(_logger, RunId, message, null);

    public void Error(string message, Exception? exception = null) => Failed(_logger, RunId, message, exception);

    /// <summary>How long the process ran before the host started, from the process's creation time.</summary>
    public static unsafe double ProcessAgeMilliseconds()
    {
        FILETIME created, exited, kernel, user;
        if (!PInvoke.GetProcessTimes(PInvoke.GetCurrentProcess(), &created, &exited, &kernel, &user))
        {
            return double.NaN;
        }

        PInvoke.GetSystemTimePreciseAsFileTime(out var now);
        return (Ticks(now) - Ticks(created)) / (double)TimeSpan.TicksPerMillisecond;
    }

    private static long Ticks(FILETIME time) => ((long)(uint)time.dwHighDateTime << 32) | (uint)time.dwLowDateTime;

    /// <summary>
    /// The host's own lines: on standard error, when the process has one (a desktop executable gets it only from a
    /// console or a launcher that redirected it, and otherwise the writes go nowhere), and in the per-user log file,
    /// with the wall-clock time there, since a file outlives the run.
    /// </summary>
    private sealed class LocalLogger(HostLog log) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var time = log.Elapsed.ToString("0.0", CultureInfo.InvariantCulture);
            var line = $"spar {Level(logLevel)} +{time} ms: {formatter(state, exception)}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            Console.Error.WriteLine(line);
            log._file?.Write(DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture) + " " + line);
        }

        private static string Level(LogLevel level) => level switch
        {
            LogLevel.Warning => "warn",
            LogLevel.Error or LogLevel.Critical => "fail",
            _ => "info",
        };
    }
}
