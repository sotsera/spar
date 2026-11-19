using System.Text;
using Microsoft.Extensions.Logging;
using Spar.Logging;

namespace Spar.Tests;

/// <summary>
/// The log file's levels (decision "Logging's public shape"): the build's default, <c>spar.Logging(…)</c> with the
/// rules of Microsoft.Extensions.Logging's filters, and the per-user <c>logging.json</c>, read strictly and never
/// stopping the start.
/// </summary>
public sealed class LogLevelsTests : IDisposable
{
    private readonly string _root = Path.Combine(TestFolders.Artifacts(), "test-host", "log-levels", Guid.NewGuid().ToString("N")[..12]);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Without_a_choice_Production_writes_Information_and_Development_Debug()
    {
        var options = new LoggingOptions();

        Assert.Equal(LogLevel.Information, Lowest(LogLevels.Resolve(options, development: false, file: null), "SqlTool"));
        Assert.Equal(LogLevel.Debug, Lowest(LogLevels.Resolve(options, development: true, file: null), "SqlTool"));
    }

    [Fact]
    public void A_category_level_covers_the_names_it_is_a_prefix_of_and_the_longest_match_wins()
    {
        var options = new LoggingOptions()
            .Level(LogLevel.Warning)
            .Level("SqlTool", LogLevel.Information)
            .Level("sqltool.copy", LogLevel.Debug)
            .Level("SqlTool.*.Jobs", LogLevel.Trace);
        var levels = LogLevels.Resolve(options, development: false, file: null);

        // The last: Microsoft.Extensions.Logging lets a wildcard's prefix and suffix overlap, so "SqlTool.*.Jobs" also
        // matches "SqlTool.Jobs".
        Assert.Equal(
            [LogLevel.Warning, LogLevel.Information, LogLevel.Debug, LogLevel.Debug, LogLevel.Trace, LogLevel.Trace],
            new[] { "Spar.Host", "SqlTool.Monitor", "SqlTool.Copy", "SqlTool.CopyJobs", "SqlTool.Recipes.Jobs", "SqlTool.Jobs" }
                .Select(category => Lowest(levels, category)));
    }

    [Fact]
    public void Of_two_rules_for_the_same_category_the_later_wins()
    {
        var options = new LoggingOptions().Level("SqlTool", LogLevel.Error).Level("SqlTool", LogLevel.Trace);

        Assert.Equal(LogLevel.Trace, Lowest(LogLevels.Resolve(options, development: false, file: null), "SqlTool.Copy"));
    }

    [Fact]
    public void The_file_overrides_the_default_and_the_categories_it_names_and_keeps_the_others()
    {
        var options = new LoggingOptions().Level(LogLevel.Warning).Level("SqlTool.Copy", LogLevel.Information).Level("SqlTool.Monitor", LogLevel.Error);
        var file = LoggingFile.Parse("""{"level":"Debug","categories":{"SqlTool.Copy":"Trace"}}"""u8, out _)!;
        var levels = LogLevels.Resolve(options, development: false, file);

        Assert.Equal([LogLevel.Debug, LogLevel.Trace, LogLevel.Error], new[] { "App", "SqlTool.Copy", "SqlTool.Monitor" }.Select(category => Lowest(levels, category)));
    }

    [Fact]
    public void A_bad_level_or_category_in_Program_cs_throws_at_once()
    {
        var options = new LoggingOptions();

        Assert.Throws<ArgumentOutOfRangeException>(() => options.Level((LogLevel)42));
        Assert.Throws<ArgumentException>(() => options.Level(" ", LogLevel.Debug));
        Assert.Throws<ArgumentException>(() => options.Level("a*b*c", LogLevel.Debug));
    }

    [Theory]
    [InlineData("""{"level":"Debug"}""", LogLevel.Debug, 0)]
    [InlineData("""{"categories":{"SqlTool.Copy":"trace","Spar":"None"}}""", null, 2)]
    [InlineData("""{}""", null, 0)]
    [InlineData(" { \"level\" : \"WARNING\" } \n", LogLevel.Warning, 0)]
    public void The_file_takes_its_shape_with_either_property_and_any_case_of_a_level(string json, LogLevel? level, int categories)
    {
        var settings = LoggingFile.Parse(Encoding.UTF8.GetBytes(json), out var reason);

        Assert.Null(reason);
        Assert.Equal((level, categories), (settings!.Level, settings.Categories.Count));
    }

    [Theory]
    [InlineData("""{"level":"Verbose"}""", "a level is one of")]
    [InlineData("""{"level":3}""", "a level is one of")]
    [InlineData("""{"Level":"Debug"}""", "\"Level\" is unknown")]
    [InlineData("""{"level":"Debug","level":"Trace"}""", "\"level\" is unknown or appears twice")]
    [InlineData("""{"categories":["SqlTool"]}""", "\"categories\" is not an object")]
    [InlineData("""{"categories":{"SqlTool":"Debug","sqltool":"Trace"}}""", "appears twice")]
    [InlineData("""{"categories":{"a*b*":"Debug"}}""", "more than one '*'")]
    [InlineData("""{"categories":{"":"Debug"}}""", "non-empty name")]
    [InlineData("""{"level":"Debug"} {}""", "not valid JSON")]
    [InlineData("""{"level":"Debug" // more""", "not valid JSON")]
    [InlineData("""["Debug"]""", "not a JSON object")]
    [InlineData("", "not valid JSON")]
    public void Anything_else_refuses_the_whole_file_with_the_reason(string json, string reason)
    {
        Assert.Null(LoggingFile.Parse(Encoding.UTF8.GetBytes(json), out var why));
        Assert.Contains(reason, why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_byte_order_mark_is_not_part_of_the_file()
    {
        byte[] json = [0xEF, 0xBB, 0xBF, .. """{"level":"Error"}"""u8];

        Assert.Equal(LogLevel.Error, LoggingFile.Parse(json, out _)!.Level);
    }

    [Fact]
    public void More_than_a_hundred_categories_are_refused()
    {
        var categories = string.Join(",", Enumerable.Range(0, LoggingFile.MaxCategories + 1).Select(i => $"\"C{i}\":\"Debug\""));

        Assert.Null(LoggingFile.Parse(Encoding.UTF8.GetBytes("{\"categories\":{" + categories + "}}"), out var reason));
        Assert.Contains("more than 100 categories", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void No_file_is_no_problem_and_a_refused_one_gives_one_warning_naming_it_and_its_shape()
    {
        Directory.CreateDirectory(_root);
        Assert.Null(LoggingFile.Read(_root, out var none));
        Assert.Null(none);

        var path = Path.Combine(_root, LoggingFile.FileName);
        File.WriteAllText(path, """{"level":"Loud"}""");
        var levels = SparLogging.Levels(new LoggingOptions().Level(LogLevel.Warning), _root, development: false, out var problem);

        Assert.Equal(LogLevel.Warning, levels.Default);
        Assert.Equal(
            $"The log settings file {path} was ignored: a level is one of \"Trace\", \"Debug\", \"Information\", \"Warning\", " +
            $"\"Error\", \"Critical\" or \"None\". Its shape is {LoggingFile.Shape}; the levels from Program.cs apply.",
            problem);
    }

    [Fact]
    public void A_file_too_large_is_not_read()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, LoggingFile.FileName), "{\"level\":\"Debug\"" + new string(' ', LoggingFile.MaxBytes) + "}");

        Assert.Null(LoggingFile.Read(_root, out var problem));
        Assert.Contains("larger than 64 KiB", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void The_file_is_read_from_the_data_root()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, LoggingFile.FileName), """{"categories":{"SqlTool.Copy":"Trace"}}""");

        var levels = SparLogging.Levels(new LoggingOptions(), _root, development: false, out var problem);

        Assert.Null(problem);
        Assert.Equal((LogLevel.Information, LogLevel.Trace), (Lowest(levels, "App"), Lowest(levels, "SqlTool.Copy")));
    }

    /// <summary>The lowest level a category gets from these levels under Microsoft.Extensions.Logging's own rule selection.</summary>
    private static LogLevel Lowest(LogLevels levels, string category)
    {
        using var factory = new LoggerFactory([new AnyProvider()], levels.ToFilterOptions());
        var logger = factory.CreateLogger(category);
        for (var level = LogLevel.Trace; level < LogLevel.None; level++)
        {
            if (logger.IsEnabled(level))
            {
                return level;
            }
        }

        return LogLevel.None;
    }

    private sealed class AnyProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturedLog();

        public void Dispose()
        {
        }
    }
}
