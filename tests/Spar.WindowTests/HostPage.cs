using Microsoft.Extensions.DependencyInjection;
using Spar.Testing;
using WindowHost;
using static Spar.Testing.SparAssertions;

namespace Spar.WindowTests;

/// <summary>The test app started on the Window surface, with what the tests read of its page and its backend.</summary>
internal sealed class HostPage : IAsyncDisposable
{
    private HostPage(SparTestApp app)
    {
        App = app;
    }

    public SparTestApp App { get; }

    public SparPage Page => App.Page;

    /// <summary>What the backend saw: calls started and ended, and close requests.</summary>
    public Activity Activity => App.Services.GetRequiredService<Activity>();

    public SparLocator Status => Page.GetByRole(SparRole.Status);

    public static async Task<HostPage> OpenAsync(Action<SparTestOptions>? configure = null)
    {
        WindowSupport.SkipUnlessSupported();
        var app = await SparTestApp.StartAsync<Program>(options =>
        {
            options.Surface = SparTestSurface.Window;
            configure?.Invoke(options);
        }, TestContext.Current.CancellationToken);
        var page = new HostPage(app);
        await Expect(page.Status).ToHaveTextAsync("Ready");
        return page;
    }

    /// <summary>Clicks a button by its name.</summary>
    public Task ClickAsync(string name) => Page.GetByRole(SparRole.Button, new() { Name = name, Exact = true }).ClickAsync();

    /// <summary>Waits until the backend has <paramref name="count"/> calls running.</summary>
    public async Task WaitForRunningAsync(int count)
    {
        var limit = DateTime.UtcNow.AddSeconds(5);
        while (Activity.Running != count)
        {
            Assert.True(DateTime.UtcNow < limit, $"The backend has {Activity.Running} calls running, not {count}.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Nothing refused or dropped by the bridge, no CSP violation, no page error, no console error.</summary>
    public void AssertClean()
    {
        Assert.True(App.Problems.Count == 0, string.Join(Environment.NewLine, App.Problems));
        Assert.Empty(Page.PageErrors);
        Assert.DoesNotContain(Page.ConsoleMessages, message => message.Type is "error" or "warning");
    }

    public ValueTask DisposeAsync() => App.DisposeAsync();
}
