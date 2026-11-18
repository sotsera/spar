using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Spar.Logging;

/// <summary>
/// How a host gives the app its logging (decision "Logging's public shape"): Microsoft.Extensions.Logging's factory,
/// the run's levels as its filter rules, read once at start, and the log file's provider unless the app opted out.
/// </summary>
internal static class SparLogging
{
    /// <summary>The category of the host's own lines.</summary>
    public const string HostCategory = "Spar.Host";

    /// <summary>
    /// This run's levels: the build's default, <c>spar.Logging(…)</c>, then <c>logging.json</c> in
    /// <paramref name="dataRoot"/>. A refused file leaves the levels from <c>Program.cs</c>, with the warning's text in
    /// <paramref name="problem"/> for the host to log once.
    /// </summary>
    public static LogLevels Levels(LoggingOptions options, string dataRoot, bool development, out string? problem)
    {
        var file = LoggingFile.Read(dataRoot, out problem);
        return LogLevels.Resolve(options, development, file);
    }

    /// <summary>
    /// The app's logging, after the app's own registrations: the factory (<c>AddLogging</c>, which an app may have
    /// called already), the levels of <c>Program.cs</c> configured after the app's own filters, those of
    /// <c>logging.json</c> after every configuration (a post-configuration, so they hold whatever the app configured and
    /// in whatever order, a configuration reloaded while the app runs included), and the file's provider when
    /// <paramref name="file"/> is given. <paramref name="ownsFile"/> says whether the services close the file when they
    /// are disposed (a test host) or the host does at process exit (a desktop host, whose last lines come after the
    /// services are gone).
    /// </summary>
    public static void Add(IServiceCollection services, LogLevels levels, Func<LogFile>? file, bool ownsFile)
    {
        services.AddLogging();
        services.Configure<LoggerFilterOptions>(levels.ApplyApp);
        if (levels.HasFileLevels)
        {
            services.PostConfigure<LoggerFilterOptions>(levels.ApplyFile);
        }

        if (file is not null)
        {
            services.AddSingleton<ILoggerProvider>(_ => new SparFileLoggerProvider(file(), ownsFile));
        }
    }

    /// <summary>
    /// A logger for the host's lines before the app's services exist: the file's provider alone, behind a factory with
    /// the same levels, so the first lines are filtered as the app asked.
    /// </summary>
    public static ILogger EarlyLogger(LogFile file, LogLevels levels, string category)
    {
        var factory = new LoggerFactory([new SparFileLoggerProvider(file, ownsFile: false)], levels.ToFilterOptions());
        return factory.CreateLogger(category);
    }
}
