using System.Collections.Concurrent;
using Spar.Testing;
using Spar;
using static Spar.Testing.SparAssertions;

namespace Spar.WindowTests;

/// <summary>
/// The required host tests of plan section 5 on the real window: a close during the controller's creation, during a
/// long call, with a call that ignores its cancellation, a navigation with calls in flight, one of two windows on the
/// shared environment closed, and repeated opening and closing. Each ends with the backend's work final, late replies
/// dropped, the window's renderer gone (the window's own check) and the profile reusable (a window opened afterwards
/// works). The end of the OS session has its own class, <see cref="SessionEndHostTests"/>.
/// </summary>
[Trait("Surface", "Window")]
public sealed class HostTests
{
    [Fact]
    public async Task Closing_while_the_controller_is_created_ends_cleanly()
    {
        WindowSupport.SkipUnlessSupported();
        var phases = new ConcurrentQueue<string>();
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var app = await SparTestApp.StartAsync<Program>(options =>
        {
            options.Surface = SparTestSurface.Window;
            options.WaitForPage = false;
            options.WindowPhase = phase =>
            {
                phases.Enqueue(phase);
                if (phase == "controller requested")
                {
                    requested.TrySetResult();
                }
            };
        }, TestContext.Current.CancellationToken);

        await requested.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        await app.Window.RequestCloseAsync();

        Assert.Equal(0, await app.Window.Closed.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.Window.Opened);
        Assert.DoesNotContain("first navigation completed", phases);
        await AssertProfileReusableAsync();
    }

    [Fact]
    public async Task Closing_during_a_long_call_cancels_it_and_ends_cleanly()
    {
        await using var host = await HostPage.OpenAsync();
        await host.ClickAsync("Start a long call");
        await host.WaitForRunningAsync(1);

        await host.App.Window.RequestCloseAsync();

        Assert.Equal(0, await host.App.Window.Closed.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken));
        Assert.Equal(0, host.Activity.Running);
        Assert.Contains("cancelled hold", host.Activity.Events);
        Assert.Equal(CloseReason.User, Assert.Single(host.Activity.CloseRequests).Reason);
        await AssertProfileReusableAsync();
    }

    [Fact]
    public async Task Closing_during_a_call_that_ignores_cancellation_waits_for_it()
    {
        await using var host = await HostPage.OpenAsync();
        await host.ClickAsync("Start a slow call");
        await host.WaitForRunningAsync(1);

        await host.App.Window.RequestCloseAsync();

        // The teardown lets cancelled calls settle (3 s at most): this one ends on its own after 800 ms.
        Assert.Equal(0, await host.App.Window.Closed.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken));
        Assert.Equal(0, host.Activity.Running);
        Assert.Contains("finished slow", host.Activity.Events);
    }

    [Fact]
    public async Task A_navigation_with_calls_in_flight_cancels_them_and_drops_their_late_replies()
    {
        await using var host = await HostPage.OpenAsync();
        await host.ClickAsync("Start a long call");
        await host.ClickAsync("Start a slow call");
        await host.WaitForRunningAsync(2);

        await host.Page.GotoAsync("/other.html");

        // The old document's calls: the long one is cancelled, the slow one finishes after the navigation, and its
        // reply goes nowhere (the new document has its own session and sees nothing of it).
        await Waits.UntilAsync(() => host.Activity.Running == 0, TimeSpan.FromSeconds(5), "the old document's calls to end");
        Assert.Contains("cancelled hold", host.Activity.Events);
        Assert.Contains("finished slow", host.Activity.Events);
        await Expect(host.Page.GetByRole(SparRole.Status)).ToHaveTextAsync("Ready");

        await host.Page.GetByRole(SparRole.Button, new() { Name = "Echo" }).ClickAsync();
        await Expect(host.Page.GetByRole(SparRole.Status)).ToHaveTextAsync("Echo: again");
        host.AssertClean();
    }

    [Fact]
    public async Task Closing_one_of_two_windows_leaves_the_other_working()
    {
        await using var first = await HostPage.OpenAsync();
        await using var second = await HostPage.OpenAsync();

        await first.App.Window.RequestCloseAsync();
        Assert.Equal(0, await first.App.Window.Closed.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken));

        await second.ClickAsync("Echo");
        await Expect(second.Status).ToHaveTextAsync("Echo: hello");
        second.AssertClean();
    }

    [Fact]
    public async Task Windows_open_and_close_twenty_times_in_a_row()
    {
        for (var round = 0; round < 20; round++)
        {
            await using var host = await HostPage.OpenAsync();
            await host.ClickAsync("Echo");
            await Expect(host.Status).ToHaveTextAsync("Echo: hello");
            host.AssertClean();
        }
    }

    [Fact]
    public async Task The_page_renderer_is_found_and_is_gone_once_the_window_closed()
    {
        await using var host = await HostPage.OpenAsync();
        var renderers = await host.App.Window.RendererProcessesAsync();
        Assert.NotEmpty(renderers);
        var processes = renderers.Select(System.Diagnostics.Process.GetProcessById).ToList();

        await host.App.Window.RequestCloseAsync();
        await host.App.Window.Closed.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

        // The window's own check waited for them; each process the page had is gone.
        Assert.All(processes, process => Assert.True(process.HasExited, $"renderer {process.Id} still runs"));
    }

    /// <summary>A window opened afterwards on the same shared environment loads and calls its backend.</summary>
    private static async Task AssertProfileReusableAsync()
    {
        await using var next = await HostPage.OpenAsync();
        await next.ClickAsync("Echo");
        await Expect(next.Status).ToHaveTextAsync("Echo: hello");
        next.AssertClean();
    }
}
