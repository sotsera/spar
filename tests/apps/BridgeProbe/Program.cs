using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spar;

var app = SparApplication.Create(args, spar =>
{
    spar.Window(w => w.Size(900, 640));

    // The foreign frame of the bridge test, served by the test on loopback: another origin than spar://app.
    spar.ContentSecurity(csp => csp.FrameSrc("http://127.0.0.1:*"));
    spar.Services.AddSingleton<BridgeProbe.Loads>();

    // For the start-up tests (M1c step 2): the acceleration the test asks for, later launches' arguments in the log,
    // and, when asked, the app's own start-up message instead of Spar's, on standard error.
    if (Environment.GetEnvironmentVariable("BRIDGE_PROBE_ACCELERATION") is { Length: > 0 } acceleration)
    {
        spar.Browser(b => b.HardwareAcceleration(Enum.Parse<HardwareAcceleration>(acceleration)));
    }

    spar.OnRelaunch((relaunch, services) => services.GetRequiredService<ILogger<BridgeProbe.ProbeService>>()
        .LogInformation("probe relaunch: [{Arguments}]", string.Join(" | ", relaunch.Arguments)));
    spar.OnStartupProblem(problem =>
    {
        if (Environment.GetEnvironmentVariable("BRIDGE_PROBE_OWN_STARTUP_MESSAGE") != "1")
        {
            return false;
        }

        Console.Error.WriteLine($"probe: own start-up message for {problem.Kind}, {problem.Component}");
        return true;
    });
});
return app.Run();

namespace BridgeProbe
{
    /// <summary>How many times the first page has started, across reloads and web processes.</summary>
    public sealed class Loads
    {
        private int _count;

        public int Next() => Interlocked.Increment(ref _count);
    }

    /// <summary>What the page asks and reports; each note is a line in the app's log, which the test reads.</summary>
    [SparContract]
    public sealed class ProbeService(ILogger<ProbeService> log, Loads loads)
    {
        [SparCommand]
        public Task<string> EchoAsync(string text) => Task.FromResult(text);

        /// <summary>What the page saw, as a log line the test reads: <c>probe note: &lt;text&gt;</c>.</summary>
        [SparCommand]
        public Task NoteAsync(string text)
        {
            log.LogInformation("probe note: {Text}", text);
            return Task.CompletedTask;
        }

        /// <summary>
        /// The steps the test asks for (<c>BRIDGE_PROBE_PLAN</c>), comma-separated, and which start of the first page
        /// this is (1 for the first): a page cannot tell a reload after a lost web process from a first load.
        /// </summary>
        [SparCommand]
        public Task<Plan> PlanAsync() =>
            Task.FromResult(new Plan(Environment.GetEnvironmentVariable("BRIDGE_PROBE_PLAN") ?? "", loads.Next()));

        /// <summary>The foreign frame's address (<c>BRIDGE_PROBE_FRAME</c>), served by the test; empty for none.</summary>
        [SparCommand]
        public Task<string> FrameAsync() => Task.FromResult(Environment.GetEnvironmentVariable("BRIDGE_PROBE_FRAME") ?? "");
    }

    public sealed record Plan(string Steps, int Load);
}
