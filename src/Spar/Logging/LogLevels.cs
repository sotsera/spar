using Microsoft.Extensions.Logging;

namespace Spar.Logging;

/// <summary>One level for the categories a name matches; a null category is the default level.</summary>
internal sealed class LogLevelRule(string? category, LogLevel level)
{
    public string? Category { get; } = category;

    public LogLevel Level { get; } = level;
}

/// <summary>
/// The app's log levels for this run: the build's default, the app's <c>spar.Logging(…)</c>, then the per-user
/// <c>logging.json</c>, which overrides both (<see cref="LoggingFile"/>). They become Microsoft.Extensions.Logging's
/// filter rules (<see cref="Apply"/>): the default is the minimum level, and each category is a rule for every
/// provider, matched as those rules match (a case-insensitive prefix, at most one <c>*</c>, the longest rule winning,
/// the later of two equal ones). A rule the app adds for one provider still applies to that provider.
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

    /// <summary>The category rules, in the order they apply: the app's, then the file's.</summary>
    public IReadOnlyList<LogLevelRule> Rules => _rules;

    /// <summary>The build's default levels, before any configuration.</summary>
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

    /// <summary>
    /// Writes these levels into the factory's filter options, after the app's own configuration: the minimum level,
    /// and one rule per category for every provider, after the rules already there, so the later wins between equals.
    /// </summary>
    public void Apply(LoggerFilterOptions options)
    {
        options.MinLevel = Default;
        foreach (var rule in _rules)
        {
            options.Rules.Add(new LoggerFilterRule(providerName: null, rule.Category, rule.Level, filter: null));
        }
    }

    /// <summary>The filter options of these levels alone, for the host's lines before the app's services exist.</summary>
    public LoggerFilterOptions ToFilterOptions()
    {
        var options = new LoggerFilterOptions();
        Apply(options);
        return options;
    }

    /// <summary>Throws for a value that is not a level.</summary>
    public static void Check(LogLevel level)
    {
        if (level is < LogLevel.Trace or > LogLevel.None)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "Not a log level.");
        }
    }

    /// <summary>
    /// Why a category name cannot be a rule, or null when it can. Two stars would make Microsoft.Extensions.Logging
    /// throw at the first logger, so they are refused here, where the app or the file can be told.
    /// </summary>
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
}
