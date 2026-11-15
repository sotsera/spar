using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SampleApp.Orders;
using Spar.Testing;

namespace SampleApp.Tests;

public sealed class FakeOrders(int count) : IOrderDatabase
{
    public Task<int> CountOpenOrdersAsync(CancellationToken cancellationToken) => Task.FromResult(count);
}

public sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

internal static class TestApps
{
    public static readonly DateTimeOffset Now = new(2026, 11, 15, 9, 30, 0, TimeSpan.Zero);

    /// <summary>The usual test setup: fixed time and a fake order database.</summary>
    public static Task<SparTestApp> StartAsync(SparTestSurface surface = SparTestSurface.None, int orders = 3) =>
        SparTestApp.StartAsync(AppEntry.Run, options =>
        {
            options.Surface = surface;
            options.Services(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new FixedTime(Now)));
                services.Replace(ServiceDescriptor.Singleton<IOrderDatabase>(new FakeOrders(orders)));
            });
        }, TestContext.Current.CancellationToken);
}
