using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spar;
#if CONFIG_V1
using Microsoft.Extensions.Configuration;
#endif
#if CONFIG_V2
using Microsoft.Extensions.Options;
#endif

#if CONFIG_V1
// The app's own configuration, before Spar's host starts. Spar does not expose its data root before the services exist,
// so the app repeats the rule of SparFolders: %LOCALAPPDATA%\<app id> in Production.
var dataRoot = Path.Combine(
    Environment.GetEnvironmentVariable("LOCALAPPDATA") is { Length: > 0 } local
        ? local
        : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "com.example.spikes.ConfigCost");
#if CONFIG_V3
const bool reload = true;
#elif !CONFIG_STREAM
const bool reload = false;
#endif
#if CONFIG_STREAM
// The file read by the app and handed over as a stream: no file provider, no watcher, no globbing.
var settingsPath = Path.Combine(dataRoot, "appsettings.json");
using Stream settings = File.Exists(settingsPath) ? File.OpenRead(settingsPath) : new MemoryStream("{}"u8.ToArray());
var configuration = new ConfigurationBuilder()
    .AddJsonStream(settings)
#else
var configuration = new ConfigurationBuilder()
    .AddJsonFile(Path.Combine(dataRoot, "appsettings.json"), optional: true, reloadOnChange: reload)
#endif
#if CONFIG_V2
    .AddEnvironmentVariables("CONFIGCOST_")
#endif
    .Build();
#endif

var app = SparApplication.Create(args, spar =>
{
#if CONFIG_V1
    // The standard shape, {"Logging":{"LogLevel":{"Default":…,"Category":…}}}, applied through the app's own AddLogging.
    spar.Services.AddLogging(logging => logging.AddConfiguration(configuration.GetSection("Logging")));
#endif
#if CONFIG_V2
    spar.Services.AddSingleton<IConfiguration>(configuration);
    spar.Services.Configure<ConfigCost.ProbeOptions>(configuration.GetSection("Probe"));
#endif
});
return app.Run();

namespace ConfigCost
{
    /// <summary>An options class bound from the "Probe" section by the configuration binding generator.</summary>
    public sealed class ProbeOptions
    {
        public string Greeting { get; set; } = "default";

        public int Count { get; set; }

        public List<string> Tags { get; set; } = [];
    }

    /// <summary>
    /// The page asks for the run's details and reports them to standard error; with CONFIG_COST_TICK=1 a timer logs a
    /// Debug line under ConfigCost.Ticker every 250 ms, to see a level change while the app runs.
    /// </summary>
    [SparContract]
    public sealed class ProbeService(ILogger<ProbeService> logger, IServiceProvider services)
    {
        private static readonly Action<ILogger, string, Exception?> DebugLine = LoggerMessage.Define<string>(
            LogLevel.Debug, new EventId(1, "DebugLine"), "Debug from the app: {Text}");

        private static readonly Action<ILogger, string, Exception?> InformationLine = LoggerMessage.Define<string>(
            LogLevel.Information, new EventId(2, "InformationLine"), "Information from the app: {Text}");

        private static readonly Action<ILogger, int, Exception?> Tick = LoggerMessage.Define<int>(
            LogLevel.Debug, new EventId(3, "Tick"), "tick {Number}");

        private static Timer? s_ticker;

        [SparCommand]
        public Task<string> DetailsAsync()
        {
            DebugLine(logger, "details asked", null);
            InformationLine(logger, "details asked", null);
            if (Environment.GetEnvironmentVariable("CONFIG_COST_TICK") == "1" && s_ticker is null)
            {
                var ticker = services.GetRequiredService<ILoggerFactory>().CreateLogger("ConfigCost.Ticker");
                var number = 0;
                s_ticker = new Timer(_ => Tick(ticker, Interlocked.Increment(ref number), null), null, 0, 250);
            }

#if CONFIG_V2
            var options = services.GetRequiredService<IOptions<ProbeOptions>>().Value;
            var configuration = services.GetRequiredService<IConfiguration>();
            return Task.FromResult(
                $"variant 2+; options greeting={options.Greeting} count={options.Count} tags={string.Join(",", options.Tags)}; " +
                $"Logging:LogLevel:Default={configuration["Logging:LogLevel:Default"]}");
#elif CONFIG_STREAM
            return Task.FromResult("variant 4");
#elif CONFIG_V1
            return Task.FromResult("variant 1");
#else
            return Task.FromResult("variant 0");
#endif
        }

        [SparCommand]
        public Task ReportAsync(string text)
        {
            Console.Error.WriteLine($"probe: {text}");
            return Task.CompletedTask;
        }
    }
}
