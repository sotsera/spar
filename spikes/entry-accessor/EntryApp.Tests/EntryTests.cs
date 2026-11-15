using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SampleApp.Orders;
using SampleApp.Tests;
using Spar;
using Spar.Testing;

namespace EntryApp.Tests;

/// <summary>Stand-in for Spar.Testing's generic overload: the app's Program is the type argument.</summary>
internal static class SparTest
{
    public static Task<SparTestApp> StartAsync<TApp>(Action<SparTestOptions>? configure = null)
        where TApp : ISparEntryPoint =>
        SparTestApp.StartAsync(static args => TApp.Run(args), configure, TestContext.Current.CancellationToken);
}

public sealed class FakeOrders(int count) : IOrderDatabase
{
    public Task<int> CountOpenOrdersAsync(CancellationToken cancellationToken) => Task.FromResult(count);
}

public sealed class EntryTests(ITestOutputHelper output)
{
    [Fact]
    public void The_test_build_makes_Program_public_and_an_entry_point()
    {
        Assert.True(typeof(Program).IsPublic);
        Assert.True(typeof(ISparEntryPoint).IsAssignableFrom(typeof(Program)));
    }

    [Fact]
    public async Task StartAsync_of_Program_runs_the_apps_own_composition()
    {
        var clock = Stopwatch.StartNew();
        await using var app = await SparTest.StartAsync<Program>(o =>
            o.Services(s => s.Replace(ServiceDescriptor.Singleton<IOrderDatabase>(new FakeOrders(42)))));
        var notes = new NotesClient(app);

        var added = await notes.AddAsync("through the generated entry point");
        var orders = await notes.OrderCountAsync();

        Assert.True(added.IsSuccess);
        Assert.Equal(("ok", "42"), (orders.Status, orders.Payload));
        output.WriteLine($"SPIKE start+add+count {clock.Elapsed.TotalMilliseconds:F1}ms");
    }

    [Fact]
    public async Task Two_apps_at_once_stay_apart()
    {
        var apps = await Task.WhenAll(
            SparTest.StartAsync<Program>(o => o.Services(s => s.Replace(ServiceDescriptor.Singleton<IOrderDatabase>(new FakeOrders(1))))),
            SparTest.StartAsync<Program>(o => o.Services(s => s.Replace(ServiceDescriptor.Singleton<IOrderDatabase>(new FakeOrders(2))))));
        try
        {
            Assert.Equal("1", (await new NotesClient(apps[0]).OrderCountAsync()).Payload);
            Assert.Equal("2", (await new NotesClient(apps[1]).OrderCountAsync()).Payload);
        }
        finally
        {
            foreach (var app in apps)
            {
                await app.DisposeAsync();
            }
        }
    }
}
