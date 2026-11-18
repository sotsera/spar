using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spar.Logging;
using Windows.Win32;
using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;

namespace Spar.Windows;

/// <summary>
/// The host's instrumentation (plan section 3, "Instrumentation from the first slice"): a run id, monotonic times from
/// the host's start, and the start-up and shutdown phases, under the category <c>Spar.Host</c>. Its lines go to the
/// app's log file from the host's first moment (<see cref="UseFile"/>), through the app's logging once its services
/// exist (whose factory carries the same file), and, when the process has a standard error handle (a console, or a
/// launcher that redirected it), there too. Each phase is logged once, with its time from the start; a later repeat (a
/// recovered browser) is logged again as a new phase.
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
    private readonly bool _standardError;
    private LogFile? _file;
    private ILogger? _fileLogger;
    private ILogger? _appLogger;

    /// <param name="standardError">
    /// Whether the lines also go to standard error. A window in a test process writes only to its app's own log: the
    /// process's standard error is shared by every app the tests run (plan section 5).
    /// </param>
    public HostLog(bool standardError = true)
    {
        _standardError = standardError;
        RunId = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
        Logger = new LocalLogger(this);
    }

    /// <summary>The run's id, in every line of the host, so one run's lines can be found in a shared log.</summary>
    public string RunId { get; }

    /// <summary>
    /// The host's logger, the same object for the whole run: standard error, and the log file directly until the app's
    /// logging exists, then through it.
    /// </summary>
    public ILogger Logger { get; }

    /// <summary>Milliseconds since the host started, monotonic.</summary>
    public double Elapsed => Stopwatch.GetElapsedTime(_started).TotalMilliseconds;

    /// <summary>The app's log file, once <see cref="UseFile"/> opened it.</summary>
    public LogFile? File => _file;

    /// <summary>The log file's path, once <see cref="UseFile"/> opened it.</summary>
    public string? FilePath => _file?.Path;

    /// <summary>
    /// Writes every line to the app's log file in <paramref name="logsFolder"/> too, from now on (before anything can
    /// fail at start-up), at the run's levels. The app's services get the same file (<see cref="SparLogging.Add"/>).
    /// </summary>
    public void UseFile(string logsFolder, LogLevels levels)
    {
        _file ??= LogFile.Open(logsFolder);
        _fileLogger = SparLogging.EarlyLogger(_file, levels, SparLogging.HostCategory);
    }

    /// <summary>Ends the log file: what was logged reaches the disk, within a short limit. Called once, at process exit.</summary>
    public void Close() => _file?.Close();

    /// <summary>
    /// Sends the host's lines through the app's logging once its services exist: its factory carries the log file, so
    /// the file gets each line once, and the app's own providers get them too.
    /// </summary>
    public void UseAppLogging(IServiceProvider services)
    {
        if (services.GetService<ILoggerFactory>()?.CreateLogger(SparLogging.HostCategory) is { } app)
        {
            _appLogger = app;
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

        PhaseReached(Logger, RunId, phase, Math.Round(Elapsed, 1), null);
        return true;
    }

    /// <summary>Logs a phase every time it is reached (shutdown steps, recoveries).</summary>
    public void Step(string phase) => PhaseReached(Logger, RunId, phase, Math.Round(Elapsed, 1), null);

    public void Info(string message) => Note(Logger, RunId, message, null);

    public void Warn(string message) => Warned(Logger, RunId, message, null);

    public void Error(string message, Exception? exception = null) => Failed(Logger, RunId, message, exception);

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
    /// The host's lines: on standard error at Information and above, when the process has one (a desktop executable
    /// gets it only from a console or a launcher that redirected it, and otherwise the writes go nowhere), with the
    /// time from the host's start; and to the app's logging, or the log file before it exists, at the file's level.
    /// </summary>
    private sealed class LocalLogger(HostLog log) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) =>
            (log._standardError && logLevel >= LogLevel.Information) || Target()?.IsEnabled(logLevel) == true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            // The generic part stays this small: it is compiled again for every kind of message state in the image.
            if (log._standardError && logLevel >= LogLevel.Information && logLevel != LogLevel.None)
            {
                WriteStandardError(logLevel, formatter(state, exception), exception);
            }

            Target()?.Log(logLevel, eventId, state, exception, formatter);
        }

        private ILogger? Target() => log._appLogger ?? log._fileLogger;

        private void WriteStandardError(LogLevel logLevel, string message, Exception? exception)
        {
            var time = log.Elapsed.ToString("0.0", CultureInfo.InvariantCulture);
            var line = $"spar {Level(logLevel)} +{time} ms: {message}";
            Console.Error.WriteLine(exception is null ? line : line + Environment.NewLine + exception);
        }

        private static string Level(LogLevel level) => level switch
        {
            LogLevel.Warning => "warn",
            LogLevel.Error or LogLevel.Critical => "fail",
            _ => "info",
        };
    }
}
