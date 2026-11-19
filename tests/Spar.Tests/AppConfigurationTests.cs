using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spar.Hosting;
using Spar.Logging;
using Spar.Runtime;

namespace Spar.Tests;

/// <summary>
/// An app that brings its own configuration (decision "Configuration stays the app's choice"): <c>logging.json</c>'s
/// level and categories hold over the app's <c>"Default"</c> and filters, registered before or after Spar's; the app's
/// own rules otherwise override <c>spar.Logging</c>; and <c>spar.ResolveFolders()</c> gives <c>Program.cs</c> the
/// folders the services get, in a desktop run's shape and under the test host.
/// </summary>
public sealed class AppConfigurationTests : IDisposable
{
    private readonly string _root = Path.Combine(TestFolders.Artifacts(), "test-host", "app-configuration", Guid.NewGuid().ToString("N")[..12]);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void The_apps_Default_overrides_the_level_of_Program_cs_and_silences_Spars_Information_lines()
    {
        var lines = Write(new LoggingOptions().Level(LogLevel.Debug), file: null, AppConfigurationBefore("Warning"), after: null);

        Assert.Equal(["Warning Spar.Host", "Warning SqlTool.Copy"], lines);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_files_level_beats_the_apps_Default_registered_before_or_after_Spar(bool appFirst)
    {
        var app = AppConfigurationBefore("Warning");
        var lines = appFirst
            ? Write(new LoggingOptions(), """{"level":"Information"}""", app, after: null)
            : Write(new LoggingOptions(), """{"level":"Information"}""", before: null, app);

        Assert.Equal(["Information Spar.Host", "Warning Spar.Host", "Information SqlTool.Copy", "Warning SqlTool.Copy"], lines);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_files_level_beats_the_apps_filter_for_every_category_and_its_minimum(bool appFirst)
    {
        Action<IServiceCollection> app = services => services.AddLogging(logging => logging
            .SetMinimumLevel(LogLevel.Error)
            .AddFilter((string?)null, LogLevel.Error));
        var lines = appFirst
            ? Write(new LoggingOptions(), """{"level":"Warning"}""", app, after: null)
            : Write(new LoggingOptions(), """{"level":"Warning"}""", before: null, app);

        Assert.Equal(["Warning Spar.Host", "Warning SqlTool.Copy"], lines);
    }

    [Fact]
    public void A_category_rule_of_the_app_beats_the_files_level_and_the_files_categories_beat_the_apps()
    {
        // The app's category rules are more specific than the file's level; the file names the same category to win.
        var app = AppConfiguration(new() { ["Logging:LogLevel:Default"] = "Warning", ["Logging:LogLevel:Spar"] = "Error" });

        var levelOnly = Write(new LoggingOptions(), """{"level":"Debug"}""", app, after: null);
        var withCategory = Write(new LoggingOptions(), """{"level":"Debug","categories":{"Spar":"Information"}}""", app, after: null);

        Assert.Equal(["Debug SqlTool.Copy", "Information SqlTool.Copy", "Warning SqlTool.Copy"], levelOnly);
        Assert.Equal(
            ["Information Spar.Host", "Warning Spar.Host", "Debug SqlTool.Copy", "Information SqlTool.Copy", "Warning SqlTool.Copy"],
            withCategory);
    }

    [Fact]
    public void A_category_level_of_Program_cs_keeps_Spars_lines_under_the_apps_Default()
    {
        var lines = Write(new LoggingOptions().Level("Spar", LogLevel.Information), file: null, AppConfigurationBefore("Warning"), after: null);

        Assert.Equal(["Information Spar.Host", "Warning Spar.Host", "Warning SqlTool.Copy"], lines);
    }

    [Fact]
    public void A_provider_rule_of_the_app_still_applies_to_its_provider()
    {
        var app = AppConfiguration(new() { [$"Logging:{typeof(Recorder).FullName}:LogLevel:Default"] = "Error" });

        var lines = Write(new LoggingOptions(), """{"level":"Debug"}""", app, after: null);

        Assert.Empty(lines);
    }

    [Fact]
    public void The_files_level_still_holds_after_the_apps_configuration_reloads()
    {
        var memory = new MemoryConfigurationSource { InitialData = new Dictionary<string, string?> { ["Logging:LogLevel:Default"] = "Warning" } };
        var configuration = new ConfigurationBuilder().Add(memory).Build();
        var recorder = new Recorder();
        using var provider = Services(new LoggingOptions(), """{"level":"Information"}""", recorder, services =>
            services.AddLogging(logging => logging.AddConfiguration(configuration.GetSection("Logging"))), after: null);
        var factory = provider.GetRequiredService<ILoggerFactory>();
        var host = factory.CreateLogger("Spar.Host");
        var copy = factory.CreateLogger("SqlTool.Copy");
        copy.LogWarning("before the reload");

        // The app raises its default and adds a category: the category shows the reload took effect.
        var memoryProvider = configuration.Providers.OfType<MemoryConfigurationProvider>().Single();
        memoryProvider.Set("Logging:LogLevel:Default", "Critical");
        memoryProvider.Set("Logging:LogLevel:SqlTool", "Critical");
        configuration.Reload();
        copy.LogWarning("after the reload");
        host.LogInformation("after the reload");

        Assert.Equal(["Warning SqlTool.Copy", "Information Spar.Host"], recorder.Lines);
    }

    [Fact]
    public void ResolveFolders_gives_the_desktop_folders_and_refuses_later_folder_choices()
    {
        var data = Path.Combine(_root, "elsewhere", "data");
        var builder = new SparBuilder([], appId: "com.example.SqlTool");
        FolderOptions? kept = null;
        builder.Folders(f => kept = f.Data(data));

        var early = builder.ResolveFolders();

        Assert.Equal(SparFolders.ForApp("com.example.SqlTool", SparDevelopment.IsSupported, new FolderOptions().Data(data)), early);
        Assert.Equal(data, early.Data);
        Assert.Same(early, builder.ResolveFolders());
        var refused = Assert.Throws<InvalidOperationException>(() => builder.Folders(f => f.Logs(_root)));
        Assert.Contains("already resolved by spar.ResolveFolders()", refused.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => kept!.Logs(_root));
        Assert.Equal(early, builder.ResolveFolders());
    }

    [Fact]
    public async Task Under_the_test_host_ResolveFolders_gives_the_test_root_the_services_get()
    {
        var session = new SparTestSession { DataRoot = _root, AllowedExternals = new HashSet<Type>() };
        SparFolders? early = null;
        var run = Task.Run(() =>
        {
            SparTestSession.Current = session;
            var app = SparApplication.Create(
                [],
                spar =>
                {
                    spar.Folders(f => f.Data(Path.Combine(Path.GetTempPath(), "never-used")));
                    early = spar.ResolveFolders();
                },
                new SparCatalog("test", [], _ => { }) { AppId = "com.example.SqlTool" });
            return app.Run();
        });

        var running = await session.Started.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        var service = running.Services.GetRequiredService<SparFolders>();
        session.Stop();
        Assert.Equal(0, await run);

        Assert.Equal(SparFolders.ForTest(_root), early);
        Assert.Equal(early, service);
        Assert.Equal(early, running.Folders);
    }

    /// <summary>The standard shape, <c>{"Logging":{"LogLevel":{"Default":…}}}</c>, through the app's own AddLogging.</summary>
    private static Action<IServiceCollection> AppConfigurationBefore(string level) =>
        AppConfiguration(new() { ["Logging:LogLevel:Default"] = level });

    private static Action<IServiceCollection> AppConfiguration(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return services => services.AddLogging(logging => logging.AddConfiguration(configuration.GetSection("Logging")));
    }

    /// <summary>
    /// One Debug, Information and Warning line each from Spar's host and the app's category, with the app's own
    /// registrations <paramref name="before"/> and <paramref name="after"/> Spar's; the lines that passed the filters.
    /// </summary>
    private string[] Write(LoggingOptions options, string? file, Action<IServiceCollection>? before, Action<IServiceCollection>? after)
    {
        var recorder = new Recorder();
        using (var provider = Services(options, file, recorder, before, after))
        {
            var factory = provider.GetRequiredService<ILoggerFactory>();
            foreach (var category in (string[])["Spar.Host", "SqlTool.Copy"])
            {
                var logger = factory.CreateLogger(category);
                logger.LogDebug("debug");
                logger.LogInformation("information");
                logger.LogWarning("warning");
            }
        }

        return [.. recorder.Lines];
    }

    private ServiceProvider Services(
        LoggingOptions options,
        string? file,
        Recorder recorder,
        Action<IServiceCollection>? before,
        Action<IServiceCollection>? after)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, LoggingFile.FileName);
        if (file is null)
        {
            File.Delete(path);
        }
        else
        {
            File.WriteAllText(path, file);
        }

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerProvider>(recorder);
        before?.Invoke(services);
        var levels = SparLogging.Levels(options, _root, development: false, out var problem);
        Assert.Null(problem);
        SparLogging.Add(services, levels, file: null, ownsFile: true);
        after?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    /// <summary>A provider that records each line it gets as its level and category.</summary>
    private sealed class Recorder : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _lines = new();

        public IReadOnlyCollection<string> Lines => _lines;

        public ILogger CreateLogger(string categoryName) => new CategoryLogger(_lines, categoryName);

        public void Dispose()
        {
        }

        private sealed class CategoryLogger(ConcurrentQueue<string> lines, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                lines.Enqueue($"{logLevel} {category}");
        }
    }
}
