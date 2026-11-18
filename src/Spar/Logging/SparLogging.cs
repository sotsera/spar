using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Spar.Logging;

/// <summary>
/// How a host gives the app its logging (decision "Logging's public shape"): the levels of the run, read once at start,
/// a logger factory unless the app brought its own, and the file's provider.
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
    /// Spar's logger factory and <see cref="ILogger{TCategoryName}"/>, after the app's registrations, so an app that
    /// called <c>AddLogging</c> keeps Microsoft.Extensions.Logging's factory.
    /// </summary>
    public static void AddFactory(IServiceCollection services)
    {
        services.TryAddSingleton<ILoggerFactory, SparLoggerFactory>();
        services.TryAdd(ServiceDescriptor.Singleton(typeof(ILogger<>), typeof(Logger<>)));
    }

    /// <summary>
    /// The log file's provider, created with the services' first logger. <paramref name="ownsFile"/> says whether the
    /// services close the file when they are disposed (a test host) or the host does at process exit (a desktop host,
    /// whose last lines come after the services are gone).
    /// </summary>
    public static void AddFile(IServiceCollection services, Func<LogFile> file, LogLevels levels, bool ownsFile) =>
        services.AddSingleton<ILoggerProvider>(_ => new SparFileLoggerProvider(file(), levels, ownsFile));
}
