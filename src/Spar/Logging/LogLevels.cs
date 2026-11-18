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
/// <c>logging.json</c> (<see cref="LoggingFile"/>). They become Microsoft.Extensions.Logging's filter rules in two
/// steps. <see cref="ApplyApp"/>, configured after the app's own registrations, sets the default as the minimum level
/// and adds each category as a rule for every provider; a rule the app configured for every category itself (a
/// configuration's <c>"Default"</c>, an <c>AddFilter</c> without a category) beats that minimum, as the app chose.
/// <see cref="ApplyFile"/> runs after every configuration of the filter options, the app's included, whatever the
/// order of registration: the file's level becomes the minimum and a rule for every category, and its categories are
/// rules after all others, so the per-user switch always holds (decision "Configuration stays the app's choice").
/// Rules match as Microsoft.Extensions.Logging's do (a case-insensitive prefix, at most one <c>*</c>, the longest
/// category winning, the later of two equal ones), so a category rule of the app's still beats the file's level, and
/// the file's categories beat the app's rules for the same category. A rule for one provider still applies to it.
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
    private readonly LogLevelRule[] _fileRules;

    public LogLevels(LogLevel defaultLevel, IReadOnlyList<LogLevelRule> rules)
        : this(defaultLevel, rules, fileLevel: null, fileRules: [])
    {
    }

    public LogLevels(LogLevel defaultLevel, IReadOnlyList<LogLevelRule> rules, LogLevel? fileLevel, IReadOnlyList<LogLevelRule> fileRules)
    {
        Default = defaultLevel;
        _rules = [.. rules];
        FileLevel = fileLevel;
        _fileRules = [.. fileRules];
    }

    /// <summary>The app's default level: the build's, or the one <c>spar.Logging(…)</c> set.</summary>
    public LogLevel Default { get; }

    /// <summary>The app's category rules, in the order it set them.</summary>
    public IReadOnlyList<LogLevelRule> Rules => _rules;

    /// <summary>The level <c>logging.json</c> sets for every category, or null when it sets none.</summary>
    public LogLevel? FileLevel { get; }

    /// <summary>The category rules of <c>logging.json</c>, in its order.</summary>
    public IReadOnlyList<LogLevelRule> FileRules => _fileRules;

    /// <summary>Whether <c>logging.json</c> sets a level or a category, so <see cref="ApplyFile"/> has work to do.</summary>
    public bool HasFileLevels => FileLevel is not null || _fileRules.Length > 0;

    /// <summary>The build's default levels, before any configuration.</summary>
    public static LogLevels BuildDefault(bool development) => new(development ? DevelopmentDefault : ProductionDefault, []);

    /// <summary>This run's levels: the build's default, then the app's choices, then the file's.</summary>
    public static LogLevels Resolve(LoggingOptions options, bool development, LoggingFileSettings? file)
    {
        var defaultLevel = options.DefaultLevel ?? (development ? DevelopmentDefault : ProductionDefault);
        return new LogLevels(defaultLevel, options.Categories, file?.Level, file?.Categories ?? []);
    }

    /// <summary>
    /// The levels of <c>Program.cs</c>, configured after the app's own registrations: the minimum level, and one rule per
    /// category for every provider, after the rules already there, so the later wins between equals.
    /// </summary>
    public void ApplyApp(LoggerFilterOptions options)
    {
        options.MinLevel = Default;
        AddRules(options, _rules);
    }

    /// <summary>
    /// The levels of <c>logging.json</c>, after every other configuration of the filter options: its level as the
    /// minimum and as a rule for every category and provider, added last so it beats the app's own rule for every
    /// category (a configuration's <c>"Default"</c>), then its categories, last too.
    /// </summary>
    public void ApplyFile(LoggerFilterOptions options)
    {
        if (FileLevel is { } level)
        {
            options.MinLevel = level;
            options.Rules.Add(new LoggerFilterRule(providerName: null, categoryName: null, level, filter: null));
        }

        AddRules(options, _fileRules);
    }

    /// <summary>The filter options of these levels alone, for the host's lines before the app's services exist.</summary>
    public LoggerFilterOptions ToFilterOptions()
    {
        var options = new LoggerFilterOptions();
        ApplyApp(options);
        ApplyFile(options);
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

    private static void AddRules(LoggerFilterOptions options, LogLevelRule[] rules)
    {
        foreach (var rule in rules)
        {
            options.Rules.Add(new LoggerFilterRule(providerName: null, rule.Category, rule.Level, filter: null));
        }
    }
}
