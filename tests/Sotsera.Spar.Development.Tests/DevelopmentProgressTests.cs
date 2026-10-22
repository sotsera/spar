using System.Threading.Channels;
using AwesomeAssertions;
using Sotsera.Spar.Development;

namespace Sotsera.Spar.Development.Tests;

public sealed class DevelopmentProgressTests
{
    [Fact(Timeout = 30000)]
    [Trait("LongRunning", "true")]
    public async Task ClosedProgressOwnerCannotStartAChildAfterForcedStop()
    {
        var directory = Path.Combine(Path.GetTempPath(), "spar-progress-" + Guid.NewGuid().ToString("N"));
        var events = new List<DevelopmentEvent>();
        var settings = new DevelopmentSettings
        {
            SessionDirectory = directory,
            Project = Path.Combine(directory, "must-not-build.csproj")
        };
        await using var session = new DevelopmentSession(settings, value =>
        {
            events.Add(value);
            if (value.Kind == "session-phase")
            {
                throw new IOException("The progress owner's output closed.");
            }
        });

        var result = await session.RunAsync(Channel.CreateBounded<DevelopmentCommand>(1).Reader,
            cancellationToken: TestContext.Current.CancellationToken);

        result.Should().Be(1);
        events.Should().ContainSingle(value => value.Kind == "session-phase" && value.Code == "building");
        events.Should().NotContain(value => value.Kind == "process-started");
        events.Should().ContainSingle(value => value.Kind == "owned-processes-ended");
        events.Should().ContainSingle(value => value.Kind == "session-stopped" && value.Code == "forced_cleanup");
    }
}
