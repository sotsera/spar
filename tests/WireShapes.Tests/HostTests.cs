using Microsoft.Extensions.DependencyInjection;
using Spar.Testing;

namespace WireShapes.Tests;

/// <summary>What the test host gives each app instance: its own clock, and its own log.</summary>
public sealed class HostTests
{
    [Fact]
    public async Task A_test_clock_is_the_clock_of_the_app_and_of_Spar()
    {
        var time = new FixedTime(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));

        await using var app = await SparTestApp.StartAsync<Program>(o => o.Time(time), TestContext.Current.CancellationToken);

        Assert.Same(time, app.Time);
        Assert.Same(time, app.Services.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public async Task Without_a_test_clock_the_app_has_the_systems()
    {
        await using var app = await SparTestApp.StartAsync<Program>(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Same(TimeProvider.System, app.Services.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public async Task Each_app_instance_starts_with_an_empty_log_of_its_own()
    {
        await using var first = await SparTestApp.StartAsync<Program>(cancellationToken: TestContext.Current.CancellationToken);
        await using var second = await SparTestApp.StartAsync<Program>(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(first.Log);
        Assert.Empty(second.Log);
        Assert.Equal(0, first.LogDropped);
    }

    /// <summary>A clock stopped at one moment.</summary>
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
