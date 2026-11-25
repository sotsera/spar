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

    /// <summary>The page's origin as <c>location.origin</c> reads it: <c>https://app.localhost</c> on Windows, <c>spar://app</c> on Linux.</summary>
    public string Origin => App.Window.Origin.GetLeftPart(UriPartial.Authority);

    /// <summary>An address of the page's origin, such as <c>Address("/other.html")</c>.</summary>
    public string Address(string path) => new Uri(App.Window.Origin, path).ToString();

    public static async Task<HostPage> OpenAsync(Action<SparTestOptions>? configure = null)
    {
        await WindowSupport.SkipUnlessSupportedAsync();
        var app = await SparTestApp.StartAsync<Program>(options =>
        {
            options.Surface = SparTestSurface.Window;
            configure?.Invoke(options);
        }, TestContext.Current.CancellationToken);
        var page = new HostPage(app);
        await Expect(page.Status).ToHaveTextAsync("Ready");
        return page;
    }

    /// <summary>Picks the page's close guard: none, close, keep, ask, late-keep, slow, silent, dialog or throw.</summary>
    public Task GuardAsync(string mode) => Page.GetByLabel("Close guard").SelectOptionAsync(mode);

    /// <summary>What the page's close guard told the backend, in order: <c>"close user automated keep"</c> and so on.</summary>
    public IReadOnlyList<string> GuardNotes => NotesOf(Activity);

    /// <summary>The page guard's notes, from an <see cref="Activity"/> held from before the window closed (its services may be gone).</summary>
    public static List<string> NotesOf(Activity activity) =>
        [.. activity.Events.Where(e => e.StartsWith("note close ", StringComparison.Ordinal)).Select(e => e["note ".Length..])];

    /// <summary>
    /// Starts the page's 12 s hang and returns once the page has told the backend it starts: the page's thread is then
    /// blocked, or blocks in its next task. The button is clicked from script, not with trusted input: a trusted click
    /// returns only when the renderer acknowledges it, and on CI's Windows runner a test window's slow frames let the
    /// hang start first, so the click itself waited out the 12 s (CloseTests' hung-page cases, about one run in three).
    /// </summary>
    public async Task HangAsync()
    {
        await Page.EvaluateAsync("document.getElementById('hang').click(), true");
        await Waits.UntilAsync(() => Activity.Events.Contains("note hang started"), TimeSpan.FromSeconds(5), "the page's hang to start");
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
