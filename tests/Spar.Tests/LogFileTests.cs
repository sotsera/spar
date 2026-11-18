using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spar.Logging;

namespace Spar.Tests;

/// <summary>
/// The app's log file (decision "Logging's public shape"): the plain-text lines, the writer and its bounded queue, and
/// the provider and factory in the app's services. Each test writes under its own folder in <c>artifacts/</c>.
/// </summary>
public sealed class LogFileTests : IDisposable
{
    private static readonly DateTime At = new(2026, 11, 17, 22, 41, 7, 318, DateTimeKind.Utc);

    private readonly string _root = Path.Combine(TestFolders.Artifacts(), "test-host", "log-file", Guid.NewGuid().ToString("N")[..12]);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void A_line_has_the_time_in_UTC_a_fixed_width_level_the_category_and_the_message()
    {
        var text = new StringBuilder();
        LogLine.Append(text, new LogEntry(At, LogLevel.Information, "Spar.Host", "Host started.", null));
        LogLine.Append(text, new LogEntry(At, LogLevel.Trace, "SqlTool.Copy", "row 1", null));
        LogLine.Append(text, new LogEntry(At, LogLevel.Critical, "SqlTool", "gone", null));

        Assert.Equal(
            [
                "2026-11-17T22:41:07.318Z INFO  Spar.Host  Host started.",
                "2026-11-17T22:41:07.318Z TRACE SqlTool.Copy  row 1",
                "2026-11-17T22:41:07.318Z CRIT  SqlTool  gone",
                "",
            ],
            text.ToString().Split(LogLine.NewLine));
        Assert.Equal(OperatingSystem.IsWindows() ? "\r\n" : "\n", LogLine.NewLine);
    }

    [Fact]
    public void No_message_can_start_a_line_of_its_own()
    {
        var forged = "first\n2026-11-17T22:41:07.318Z ERROR Spar.Host  forged\r\nthird\rfourth" + (char)0x2028 + "fifth\u0085sixth";
        var text = new StringBuilder();
        LogLine.Append(text, new LogEntry(At, LogLevel.Warning, "Evil\nCategory", forged + "\u001b[31m\u0000", null));

        var lines = text.ToString().Split(LogLine.NewLine);
        Assert.Equal(
            [
                "2026-11-17T22:41:07.318Z WARN  Evil\\nCategory  first",
                "    2026-11-17T22:41:07.318Z ERROR Spar.Host  forged",
                "    third",
                "    fourth",
                "    fifth",
                "    sixth\\u001b[31m\\u0000",
                "",
            ],
            lines);
    }

    [Fact]
    public void An_exception_follows_on_indented_lines_with_its_type_message_and_stack()
    {
        Exception thrown;
        try
        {
            throw new InvalidOperationException("The copy failed.");
        }
        catch (InvalidOperationException exception)
        {
            thrown = exception;
        }

        var text = new StringBuilder();
        LogLine.Append(text, new LogEntry(At, LogLevel.Error, "SqlTool.Copy", "Copy stopped", thrown));

        var lines = text.ToString().Split(LogLine.NewLine);
        Assert.Equal("2026-11-17T22:41:07.318Z ERROR SqlTool.Copy  Copy stopped", lines[0]);
        Assert.Equal("    System.InvalidOperationException: The copy failed.", lines[1]);
        Assert.Contains(nameof(An_exception_follows_on_indented_lines_with_its_type_message_and_stack), lines[2], StringComparison.Ordinal);
        Assert.All(lines[1..^1], line => Assert.StartsWith(LogLine.Indent, line, StringComparison.Ordinal));
    }

    [Fact]
    public void A_very_long_message_is_cut_and_says_how_much_was_left_out()
    {
        var text = new StringBuilder();
        LogLine.Append(text, new LogEntry(At, LogLevel.Information, "App", new string('x', LogLine.MaxMessage + 10), null));

        Assert.EndsWith("x … (10 more characters)" + LogLine.NewLine, text.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Entries_reach_the_file_in_order_and_close_flushes_them()
    {
        var file = LogFile.Open(_root);
        for (var i = 0; i < 1_000; i++)
        {
            file.Write(LogLevel.Information, "App", $"line {i}");
        }

        await file.CloseAsync();

        var lines = File.ReadAllLines(Path.Combine(_root, LogFile.FileName));
        Assert.Equal(Enumerable.Range(0, 1_000).Select(i => $"line {i}"), lines.Select(line => line[(line.IndexOf("App  ", StringComparison.Ordinal) + 5)..]));
        Assert.Equal(0, file.Dropped);
    }

    [Fact]
    public async Task A_full_queue_drops_the_newest_entries_counts_them_and_says_so_in_the_file()
    {
        var file = LogFile.Open(_root, queueLength: 1);
        for (var i = 0; i < 100_000; i++)
        {
            file.Write(LogLevel.Information, "App", "flood");
        }

        await file.CloseAsync();

        var dropped = file.Dropped;
        var lines = File.ReadAllLines(Path.Combine(_root, LogFile.FileName));
        Assert.True(dropped > 0, "Nothing was dropped.");
        var notes = lines.Where(line => line.Contains("WARN  Spar.Log  ", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(notes);
        Assert.Equal(dropped, notes.Sum(note => long.Parse(note.Split("  ")[^1].Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Equal(100_000 - dropped, lines.Length - notes.Count);
    }

    [Fact]
    public async Task Writing_costs_the_caller_a_few_microseconds_and_never_waits_for_the_disk()
    {
        await using var provider = Services(new LoggingOptions(), configureApp: null);
        var logger = provider.GetRequiredService<ILogger<LogFileTests>>();
        for (var i = 0; i < 1_000; i++)
        {
            logger.LogInformation("Warm-up {Index}", i);
        }

        const int count = 10_000;
        var clock = Stopwatch.StartNew();
        for (var i = 0; i < count; i++)
        {
            logger.LogInformation("Copied {Rows} rows of {Table}", i, "dbo.Orders");
        }

        var perLine = clock.Elapsed.TotalMicroseconds / count;
        TestContext.Current.TestOutputHelper?.WriteLine($"Caller's cost per line: {perLine:0.00} µs ({count} lines)");
        Assert.True(perLine < 50, $"A line cost the caller {perLine:0.00} µs.");
    }

    [Fact]
    public void A_file_over_the_limit_is_moved_aside_when_a_run_opens_it()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, LogFile.FileName);
        File.WriteAllBytes(path, new byte[LogFile.RolloverBytes]);

        var file = LogFile.Open(_root);
        file.Write(LogLevel.Information, "App", "fresh");
        file.Close();

        Assert.Equal(LogFile.RolloverBytes, new FileInfo(Path.Combine(_root, "app.1.log")).Length);
        Assert.EndsWith("App  fresh", Assert.Single(File.ReadAllLines(path)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Spars_levels_hold_without_the_apps_own_logging_and_the_services_close_the_file()
    {
        var options = new LoggingOptions().Level(LogLevel.Information).Level("SqlTool.Copy", LogLevel.Debug);

        await using (var provider = Services(options, configureApp: null))
        {
            WriteLines(provider);
        }

        Assert.Equal(["INFO  " + typeof(LogFileTests).FullName + "  typed 1", "DEBUG SqlTool.Copy.Jobs  debug of a copy"], FileLines());
    }

    [Fact]
    public async Task Spars_levels_hold_under_the_apps_own_AddLogging_whose_minimum_they_replace()
    {
        // The app's own configuration sets Warning; spar.Logging's levels are configured after it and win.
        var options = new LoggingOptions().Level(LogLevel.Information).Level("SqlTool.Copy", LogLevel.Debug);
        var other = new CapturedLog();

        await using (var provider = Services(options, services => services.AddLogging(logging => logging
            .SetMinimumLevel(LogLevel.Warning)
            .AddProvider(new CapturedProvider(other)))))
        {
            WriteLines(provider);
        }

        Assert.Equal(["INFO  " + typeof(LogFileTests).FullName + "  typed 1", "DEBUG SqlTool.Copy.Jobs  debug of a copy"], FileLines());

        // The levels are the app's: its other providers get the same lines.
        Assert.Equal(["typed 1", "debug of a copy"], other.Entries.Select(e => e.Message));
    }

    [Fact]
    public async Task Logging_json_asking_for_Debug_reaches_an_app_category_under_the_apps_own_AddLogging()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, LoggingFile.FileName), """{"categories":{"SqlTool.Other":"Debug"}}""");

        await using (var provider = Services(new LoggingOptions(), services => services.AddLogging(l => l.SetMinimumLevel(LogLevel.Error))))
        {
            provider.GetRequiredService<ILoggerFactory>().CreateLogger("SqlTool.Other").LogDebug("asked for by support");
            provider.GetRequiredService<ILoggerFactory>().CreateLogger("SqlTool.Monitor").LogDebug("not asked for");
        }

        Assert.Equal(["DEBUG SqlTool.Other  asked for by support"], FileLines());
    }

    [Fact]
    public async Task A_rule_the_app_adds_for_one_provider_still_applies_to_it()
    {
        var options = new LoggingOptions().Level(LogLevel.Debug);

        await using (var provider = Services(options, services => services.Configure<LoggerFilterOptions>(filters =>
            filters.Rules.Add(new LoggerFilterRule("SparFile", "Noisy", LogLevel.Error, filter: null)))))
        {
            var factory = provider.GetRequiredService<ILoggerFactory>();
            factory.CreateLogger("Noisy.Thing").LogWarning("kept out of the file");
            factory.CreateLogger("Quiet").LogDebug("in the file");
        }

        Assert.Equal(["DEBUG Quiet  in the file"], FileLines());
    }

    [Fact]
    public void Without_the_file_no_provider_is_added_and_no_file_is_written()
    {
        var services = new ServiceCollection();
        SparLogging.Add(services, LogLevels.BuildDefault(development: false), file: null, ownsFile: true);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ILoggerFactory>().CreateLogger("App").LogError("nowhere to go");

        Assert.Empty(provider.GetServices<ILoggerProvider>());
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public void The_hosts_early_logger_filters_as_the_services_will()
    {
        var file = LogFile.Open(_root);
        var levels = LogLevels.Resolve(new LoggingOptions().Level(LogLevel.Warning).Level("Spar.Host", LogLevel.Debug), false, null);

        SparLogging.EarlyLogger(file, levels, SparLogging.HostCategory).LogDebug("early");
        SparLogging.EarlyLogger(file, levels, "Other").LogInformation("filtered");
        file.Close();

        Assert.Equal(["DEBUG Spar.Host  early"], FileLines());
    }

    private ServiceProvider Services(LoggingOptions options, Action<IServiceCollection>? configureApp)
    {
        var services = new ServiceCollection();
        configureApp?.Invoke(services);
        var levels = SparLogging.Levels(options, _root, development: false, out _);
        SparLogging.Add(services, levels, () => LogFile.Open(_root), ownsFile: true);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private static void WriteLines(ServiceProvider provider)
    {
        var typed = provider.GetRequiredService<ILogger<LogFileTests>>();
        var factory = provider.GetRequiredService<ILoggerFactory>();
        typed.LogDebug("not written");
        typed.LogInformation("typed {Value}", 1);
        factory.CreateLogger("SqlTool.Copy.Jobs").LogDebug("debug of a copy");
        factory.CreateLogger("SqlTool.Other").LogDebug("not written either");
    }

    /// <summary>The file's lines after the time: level, category and message.</summary>
    private string[] FileLines() =>
        [.. File.ReadAllLines(Path.Combine(_root, LogFile.FileName)).Select(line => line[(line.IndexOf(' ', StringComparison.Ordinal) + 1)..])];

    private sealed class CapturedProvider(CapturedLog log) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => log;

        public void Dispose()
        {
        }
    }
}

/// <summary>Where tests write: the repository's disposable <c>artifacts/</c>, or the temporary folder outside it.</summary>
internal static class TestFolders
{
    public static string Artifacts()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Spar.slnx")))
            {
                return Path.Combine(directory.FullName, "artifacts");
            }
        }

        return Path.Combine(Path.GetTempPath(), "spar-artifacts");
    }
}
