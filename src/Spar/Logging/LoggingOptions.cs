using Microsoft.Extensions.Logging;
using Spar.Logging;

namespace Spar;

/// <summary>
/// The app's log levels and its log file (<c>docs/app-model.md</c> 3.13), in <c>spar.Logging(l => …)</c>: a default
/// level, and levels for categories, matched by prefix as Microsoft.Extensions.Logging's filter rules match them, so
/// <c>"SqlTool.Copy"</c> covers <c>SqlTool.Copy.Jobs</c> too and the longest match wins. Without a default, a
/// Production build logs Information and above, a Development or Test build Debug and above. A <c>logging.json</c> in
/// the app's per-user folder overrides these at start, without a new build. The levels are the app's: they apply to
/// every provider, the log file and any the app adds; a rule the app adds for one provider still applies to it.
/// </summary>
public sealed class LoggingOptions
{
    private readonly List<LogLevelRule> _categories = [];

    internal LoggingOptions()
    {
    }

    /// <summary>The default level the app set, or null for the build's default.</summary>
    internal LogLevel? DefaultLevel { get; private set; }

    /// <summary>The category levels the app set, in the order it set them.</summary>
    internal IReadOnlyList<LogLevelRule> Categories => _categories;

    /// <summary>Whether Spar writes the app's log file; false after <see cref="WithoutFile"/>.</summary>
    internal bool WritesFile { get; private set; } = true;

    /// <summary>
    /// The lowest level logged for every category without a level of its own; <see cref="LogLevel.None"/> logs nothing.
    /// It is the app's minimum level, so a <c>SetMinimumLevel</c> in the app's own <c>AddLogging</c> is replaced by it.
    /// </summary>
    public LoggingOptions Level(LogLevel level)
    {
        LogLevels.Check(level);
        DefaultLevel = level;
        return this;
    }

    /// <summary>
    /// The lowest level logged for <paramref name="category"/> and the categories it is a prefix of (case-insensitive),
    /// such as <c>Level("SqlTool.Copy", LogLevel.Debug)</c>. One <c>*</c> may stand for any text, as in
    /// <c>"SqlTool.*.Jobs"</c>. A later call for the same category replaces the earlier one.
    /// </summary>
    public LoggingOptions Level(string category, LogLevel level)
    {
        LogLevels.Check(level);
        if (LogLevels.CategoryProblem(category) is { } problem)
        {
            throw new ArgumentException(problem, nameof(category));
        }

        _categories.Add(new LogLevelRule(category, level));
        return this;
    }

    /// <summary>
    /// Writes no log file: Spar adds no file provider, and the host's own lines, start-up failures included, go only to
    /// standard error and to the providers the app adds once its services exist. <b>Risk:</b> support then has no local
    /// file to ask a user for. A failure before the app's services exist (a missing WebView2 Runtime, a relaunch that
    /// cannot reach the running app) is recorded nowhere a user can find it unless the app was started from a console,
    /// and its message no longer names a log file. Use it only for an app that sends its logs to a sink of its own
    /// (OpenTelemetry, a central service) and can live without those early lines; <c>Level(LogLevel.None)</c> keeps the
    /// file but writes nothing to it.
    /// </summary>
    public LoggingOptions WithoutFile()
    {
        WritesFile = false;
        return this;
    }
}
