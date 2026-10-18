using AwesomeAssertions;

namespace Sotsera.Spar.HotReloadProbe.Tests;

[CollectionDefinition("Watch", DisableParallelization = true)]
public sealed class WatchTestGroup;

[Collection("Watch")]
public sealed class HotReloadTests
{
    [Fact(Timeout = 180_000)]
    public async Task SupportedEditsPreserveProcessAndStateAndReportActualUpdates()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var session = new WatchSession("body");
        var before = await session.WaitForSampleAsync(sample => sample.Counter >= 3, cancellationToken);

        await session.ReplaceAsync("GreetingProvider.cs", "Hello from Spar v1", "Hello from Spar v2", cancellationToken);
        var after = await session.WaitForSampleAsync(
            sample => sample.Greeting == "Hello from Spar v2" && sample.Update.Revision > 0,
            cancellationToken);

        after.ProcessId.Should().Be(before.ProcessId);
        after.ProcessStartedAtUtc.Should().Be(before.ProcessStartedAtUtc);
        after.InstanceId.Should().Be(before.InstanceId);
        after.Counter.Should().BeGreaterThan(before.Counter);
        after.Update.ClearCacheCount.Should().BeGreaterThan(0);
        after.Update.CallbackThreadId.Should().BeGreaterThan(0);
        after.Update.UpdatedTypes.Should().Contain(type => type.EndsWith(".GreetingProvider", StringComparison.Ordinal));

        // Closely spaced edits may be coalesced; the final behavior matters, not an exact callback count.
        await session.ReplaceAsync("GreetingProvider.cs", "Spar v2", "Spar v3", cancellationToken);
        await session.ReplaceAsync("GreetingProvider.cs", "Spar v3", "Spar v4", cancellationToken);
        var latest = await session.WaitForSampleAsync(
            sample => sample.Greeting == "Hello from Spar v4" && sample.Update.Revision > after.Update.Revision,
            cancellationToken);

        latest.ProcessId.Should().Be(before.ProcessId);
        latest.InstanceId.Should().Be(before.InstanceId);
        latest.Counter.Should().BeGreaterThan(after.Counter);
        latest.Update.Revision.Should().BeGreaterThan(after.Update.Revision);
    }

    [Fact(Timeout = 180_000)]
    public async Task CompilationErrorsKeepPreviouslyAppliedCodeAndRecoverAfterCorrection()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var session = new WatchSession("compile");
        var before = await session.WaitForSampleAsync(sample => sample.Counter >= 3, cancellationToken);

        await session.ReplaceAsync("GreetingProvider.cs", "return \"Hello from Spar v1\";", "return ;", cancellationToken);
        var diagnosticAt = await session.WaitForCompilationErrorAsync(cancellationToken);
        var duringError = await session.WaitForSampleAsync(sample => sample.ObservedAt > diagnosticAt, cancellationToken);

        duringError.Greeting.Should().Be(before.Greeting);
        duringError.ProcessId.Should().Be(before.ProcessId);
        duringError.InstanceId.Should().Be(before.InstanceId);
        duringError.Counter.Should().BeGreaterThan(before.Counter);
        duringError.Update.Revision.Should().Be(before.Update.Revision);

        await session.ReplaceAsync("GreetingProvider.cs", "return ;", "return \"Hello after correction\";", cancellationToken);
        var corrected = await session.WaitForSampleAsync(
            sample => sample.Greeting == "Hello after correction" && sample.Update.Revision > before.Update.Revision,
            cancellationToken);

        corrected.ProcessId.Should().Be(before.ProcessId);
        corrected.InstanceId.Should().Be(before.InstanceId);
        corrected.Counter.Should().BeGreaterThan(duringError.Counter);
    }

    [Fact(Timeout = 180_000)]
    public async Task UnsupportedBaseTypeEditRestartsTheOwnedApplication()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var session = new WatchSession("restart");
        var before = await session.WaitForSampleAsync(sample => sample.Counter >= 4, cancellationToken);

        await session.ReplaceAsync(
            "ProbeState.cs",
            "class ProbeState : InitialProbeBase",
            "class ProbeState : ReplacementProbeBase",
            cancellationToken);

        var restarted = await session.WaitForSampleAsync(sample => sample.InstanceId != before.InstanceId, cancellationToken);

        restarted.ProcessStartedAtUtc.Should().NotBe(before.ProcessStartedAtUtc);
        restarted.Counter.Should().BeLessThan(before.Counter);
        restarted.Update.Revision.Should().Be(0);
        restarted.Greeting.Should().Be(before.Greeting);
    }
}
