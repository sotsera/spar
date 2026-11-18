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
    public void Writing_costs_the_caller_a_few_microseconds_and_never_waits_for_the_disk()
    {
        var file = LogFile.Open(_root);
        var logger = new FileLogger(file, "App", LogLevel.Information);
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
        file.Close();

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
    public async Task The_apps_loggers_write_to_the_file_at_the_files_levels_and_the_services_close_it()
    {
        var services = new ServiceCollection();
        var levels = new LogLevels(LogLevel.Information, [new LogLevelRule("SqlTool.Copy", LogLevel.Debug)]);
        SparLogging.AddFile(services, () => LogFile.Open(_root), levels, ownsFile: true);
        SparLogging.AddFactory(services);
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        var typed = provider.GetRequiredService<ILogger<LogFileTests>>();
        var factory = provider.GetRequiredService<ILoggerFactory>();
        typed.LogDebug("not written");
        typed.LogInformation("typed {Value}", 1);
        factory.CreateLogger("SqlTool.Copy.Jobs").LogDebug("debug of a copy");
        factory.CreateLogger("SqlTool.Other").LogDebug("not written either");
        await provider.DisposeAsync();

        var lines = File.ReadAllLines(Path.Combine(_root, LogFile.FileName));
        Assert.Equal(2, lines.Length);
        Assert.EndsWith($"INFO  {typeof(LogFileTests).FullName}  typed 1", lines[0], StringComparison.Ordinal);
        Assert.EndsWith("DEBUG SqlTool.Copy.Jobs  debug of a copy", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void An_apps_own_factory_is_kept_and_gets_Spars_provider_from_the_services()
    {
        var services = new ServiceCollection();
        var own = new CapturedLog();
        services.AddSingleton<ILoggerFactory>(own);
        SparLogging.AddFactory(services);
        SparLogging.AddFile(services, () => LogFile.Open(_root), LogLevels.BuildDefault(development: false), ownsFile: true);
        using var provider = services.BuildServiceProvider();

        Assert.Same(own, provider.GetRequiredService<ILoggerFactory>());
        Assert.IsType<SparFileLoggerProvider>(Assert.Single(provider.GetServices<ILoggerProvider>()));
    }

    [Fact]
    public void Spars_factory_sends_each_line_to_every_provider_added_ones_included()
    {
        using var factory = new SparLoggerFactory([]);
        var logger = factory.CreateLogger("App");
        var added = new CapturedLog();

        logger.LogInformation("before");
        factory.AddProvider(new CapturedProvider(added));
        logger.LogInformation("after");

        Assert.Same(logger, factory.CreateLogger("App"));
        Assert.Equal(["after"], added.Entries.Select(e => e.Message));
    }

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
