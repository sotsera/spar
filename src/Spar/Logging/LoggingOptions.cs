using Microsoft.Extensions.Logging;
using Spar.Logging;

namespace Spar;

/// <summary>
/// The levels of the app's log file (<c>docs/app-model.md</c> 3.13), in <c>spar.Logging(l => …)</c>: a default, and
/// levels for categories, matched by prefix as Microsoft.Extensions.Logging filters match them, so <c>"SqlTool.Copy"</c>
/// covers <c>SqlTool.Copy.Jobs</c> too and the longest match wins. Without a default, a Production build writes
/// Information and above, a Development or Test build Debug and above. A <c>logging.json</c> in the app's per-user
/// folder overrides these at start, without a new build. The levels apply to the file only: other providers the app
/// adds keep their own filters.
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

    /// <summary>The lowest level written for every category without a level of its own; <see cref="LogLevel.None"/> writes nothing.</summary>
    public LoggingOptions Level(LogLevel level)
    {
        LogLevels.Check(level);
        DefaultLevel = level;
        return this;
    }

    /// <summary>
    /// The lowest level written for <paramref name="category"/> and the categories it is a prefix of (case-insensitive),
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
}
