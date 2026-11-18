using Microsoft.Extensions.Logging;

namespace Spar.Logging;

/// <summary>One level for the categories a name matches; a null category is the default level.</summary>
internal sealed class LogLevelRule(string? category, LogLevel level)
{
    public string? Category { get; } = category;

    public LogLevel Level { get; } = level;
}

/// <summary>
/// The levels of the log file for this run: the build's default, the app's <c>spar.Logging(…)</c>, then the per-user
/// <c>logging.json</c>, which overrides both (<see cref="LoggingFile"/>). A category gets the level of the longest
/// rule it matches (case-insensitive prefix, with at most one <c>*</c>), the later of two equally long ones, and the
/// default level when none matches: the rules of Microsoft.Extensions.Logging's filters, so a category means the same
/// in Spar's file as in the app's other providers.
/// </summary>
internal sealed class LogLevels
{
    /// <summary>A Production build's default: what support needs without filling the disk.</summary>
    public const LogLevel ProductionDefault = LogLevel.Information;

    /// <summary>A Development or Test build's default: the app's own debug lines, while it is being written.</summary>
    public const LogLevel DevelopmentDefault = LogLevel.Debug;

    /// <summary>The longest category name a rule may have.</summary>
    public const int MaxCategoryLength = 256;

    private readonly LogLevelRule[] _rules;

    public LogLevels(LogLevel defaultLevel, IReadOnlyList<LogLevelRule> rules)
    {
        Default = defaultLevel;
        _rules = [.. rules];
    }

    /// <summary>The level of a category no rule matches.</summary>
    public LogLevel Default { get; }

    /// <summary>The build's default levels, before any configuration: what a host uses until it has read them.</summary>
    public static LogLevels BuildDefault(bool development) => new(development ? DevelopmentDefault : ProductionDefault, []);

    /// <summary>
    /// This run's levels: the build's default, then the app's choices, then the file's, each overriding the one before
    /// for the default and for a category named the same.
    /// </summary>
    public static LogLevels Resolve(LoggingOptions options, bool development, LoggingFileSettings? file)
    {
        var defaultLevel = file?.Level ?? options.DefaultLevel ?? (development ? DevelopmentDefault : ProductionDefault);
        var rules = new List<LogLevelRule>(options.Categories);
        if (file is not null)
        {
            rules.AddRange(file.Categories);
        }

        return new LogLevels(defaultLevel, rules);
    }

    /// <summary>The lowest level written for <paramref name="category"/>.</summary>
    public LogLevel For(string category)
    {
        LogLevelRule? best = null;
        foreach (var rule in _rules)
        {
            if (Matches(rule.Category!, category) && (best is null || rule.Category!.Length >= best.Category!.Length))
            {
                best = rule;
            }
        }

        return best?.Level ?? Default;
    }

    /// <summary>Throws for a value that is not a level.</summary>
    public static void Check(LogLevel level)
    {
        if (level is < LogLevel.Trace or > LogLevel.None)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "Not a log level.");
        }
    }

    /// <summary>Why a category name cannot be a rule, or null when it can.</summary>
    public static string? CategoryProblem(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return "A category is a non-empty name, such as \"SqlTool.Copy\".";
        }

        if (category.Length > MaxCategoryLength)
        {
            return $"A category name has at most {MaxCategoryLength} characters.";
        }

        var star = category.IndexOf('*', StringComparison.Ordinal);
        if (star >= 0 && category.IndexOf('*', star + 1) >= 0)
        {
            return $"The category \"{category}\" has more than one '*'.";
        }

        return null;
    }

    /// <summary>The level names <c>logging.json</c> takes: the enum's, case-insensitive.</summary>
    public static bool TryParse(string name, out LogLevel level)
    {
        level = name.ToUpperInvariant() switch
        {
            "TRACE" => LogLevel.Trace,
            "DEBUG" => LogLevel.Debug,
            "INFORMATION" => LogLevel.Information,
            "WARNING" => LogLevel.Warning,
            "ERROR" => LogLevel.Error,
            "CRITICAL" => LogLevel.Critical,
            "NONE" => LogLevel.None,
            _ => (LogLevel)(-1),
        };
        return level != (LogLevel)(-1);
    }

    private static bool Matches(string rule, string category)
    {
        var star = rule.IndexOf('*', StringComparison.Ordinal);
        if (star < 0)
        {
            return category.StartsWith(rule, StringComparison.OrdinalIgnoreCase);
        }

        var prefix = rule.AsSpan(0, star);
        var suffix = rule.AsSpan(star + 1);
        return category.Length >= prefix.Length + suffix.Length
            && category.AsSpan().StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && category.AsSpan().EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
    }
}
