using Spar.Testing;
using static Spar.Testing.SparAssertions;

namespace Spar.WindowTests;

/// <summary>
/// Windows of the app's kinds in real windows (decision "M3d's decide-first questions", items 3 and 4): the page opens
/// a kind, its window shows the kind's route with the params its opener gave, a single-instance kind is focused
/// rather than opened again, a window's allow-list refuses the commands it lacks before they run, a window closes on
/// its own, and the main window's close takes the others with it. WindowHost declares a <c>note</c> kind, any number,
/// and one <c>panel</c>, which may call only <c>host.echo</c>.
/// </summary>
[Trait("Surface", "Window")]
public sealed class AppWindowTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_kinds_window_shows_its_route_with_the_params_its_opener_gave()
    {
        await using var host = await HostPage.OpenAsync();

        await host.ClickAsync("Open a note");
        var first = await host.App.WindowAsync("note", cancellationToken: Token);
        await host.ClickAsync("Open a note");
        var second = await host.App.WindowAsync("note", 1, Token);

        await Expect(first.Page.GetByText("This window: /note")).ToHaveTextAsync("""This window: /note {"text":"hello","n":1}""");
        await Expect(second.Page.GetByText("This window: /note")).ToHaveTextAsync("""This window: /note {"text":"hello","n":2}""");
        await Expect(host.Page.GetByText("This window: /")).ToHaveTextAsync("This window: / null");
        await Expect(host.Page.GetByText("Note 2: opened")).ToBeVisibleAsync();
        Assert.Equal(["note-1", "note-2"], host.App.Windows.Select(w => w.Id));
        host.AssertClean();
    }

    [Fact]
    public async Task A_single_instance_kind_is_focused_rather_than_opened_again()
    {
        await using var host = await HostPage.OpenAsync();

        await host.ClickAsync("Open the panel");
        var panel = await host.App.WindowAsync("panel", cancellationToken: Token);
        await Expect(host.Page.GetByText("Panel: opened")).ToBeVisibleAsync();
        await host.Page.EvaluateAsync("document.getElementById('window-status').textContent = 'again', true");
        await host.ClickAsync("Open the panel");

        await Expect(host.Page.GetByText("Panel: opened")).ToBeVisibleAsync();
        Assert.Equal(["panel-1"], host.App.Windows.Select(w => w.Id));
        await Expect(panel.Page.GetByText("This window: /panel")).ToHaveTextAsync("This window: /panel null");
    }

    [Fact]
    public async Task A_windows_allow_list_refuses_the_commands_it_lacks_and_lets_Spars_own_through()
    {
        await using var host = await HostPage.OpenAsync();
        await host.ClickAsync("Open the panel");
        var panel = await host.App.WindowAsync("panel", cancellationToken: Token);
        var status = panel.Page.GetByRole(SparRole.Status);

        await panel.Page.GetByRole(SparRole.Button, new() { Name = "Echo", Exact = true }).ClickAsync();
        await Expect(status).ToHaveTextAsync("Echo: hello");
        await panel.Page.GetByRole(SparRole.Button, new() { Name = "Start a slow call", Exact = true }).ClickAsync();
        await Expect(status).ToHaveTextAsync("Slow call: rejected forbidden-command");

        // The main window has no list: the same call runs there.
        await host.ClickAsync("Start a slow call");
        await Expect(host.Status).ToHaveTextAsync("Slow call: slow done");
    }

    [Fact]
    public async Task A_window_closes_on_its_own_and_the_main_windows_close_takes_the_others()
    {
        await using var host = await HostPage.OpenAsync();
        await host.ClickAsync("Open a note");
        var note = await host.App.WindowAsync("note", cancellationToken: Token);
        await host.ClickAsync("Open the panel");
        var panel = await host.App.WindowAsync("panel", cancellationToken: Token);

        var closed = await note.Window.CloseAsync();
        await note.Window.Closed.WaitAsync(TimeSpan.FromSeconds(10), Token);
        await host.ClickAsync("Echo");
        await Expect(host.Status).ToHaveTextAsync("Echo: hello");
        await host.App.Window.CloseAsync();
        await panel.Window.Closed.WaitAsync(TimeSpan.FromSeconds(10), Token);

        Assert.Equal(CloseOutcome.Closed, closed.Outcome);
    }
}
