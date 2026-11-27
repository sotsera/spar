using Spar.Testing;
using static Spar.Testing.SparAssertions;

namespace Spar.WindowTests;

/// <summary>
/// Modals and the close order in real windows (decision "M3d's decide-first questions", item 4): a modal takes its
/// opener's input until it closes, and answers it with the value its page gave <c>closeWith</c>, or <c>dismissed</c>;
/// a modal may show another and the app's other windows stay usable; closing the main window asks every other window first, the latest first, each through its
/// own guard, and one that keeps itself open keeps the app open; a window with a modal open closes the modal first.
/// WindowHost declares <c>confirm</c>, a modal, beside its <c>note</c> and <c>panel</c> kinds.
/// </summary>
[Trait("Surface", "Window")]
public sealed class ModalWindowTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_modal_takes_its_openers_input_and_answers_it_with_the_closeWith_value()
    {
        await using var host = await HostPage.OpenAsync();
        await host.ClickAsync("Open the panel");
        var panel = await host.App.WindowAsync("panel", cancellationToken: Token);
        await Expect(panel.Page.GetByText("This window: /panel")).ToHaveTextAsync("This window: /panel null");

        await host.ClickAsync("Show the confirm modal");
        var modal = await host.App.WindowAsync("confirm", cancellationToken: Token);
        await Expect(modal.Page.GetByText("This window: /confirm")).ToHaveTextAsync("""This window: /confirm {"question":"Sure?"}""");
        var openerTakesInput = await host.App.Window.TakesInputAsync();
        var modalTakesInput = await modal.Window.TakesInputAsync();
        var panelTakesInput = await panel.Window.TakesInputAsync();
        if (OperatingSystem.IsLinux())
        {
            // A real click (XTEST): the panel, another window of the app, still takes its own while the modal is open.
            await panel.Page.GetByRole(SparRole.Button, new() { Name = "Echo", Exact = true }).ClickAsync();
            await Expect(panel.Page.GetByRole(SparRole.Status)).ToHaveTextAsync("Echo: hello");
        }
        else
        {
            // The DevTools protocol's input reaches a disabled window's renderer: EnableWindow blocks a person's input,
            // which a window test does not send, so the opener is checked with IsWindowEnabled above.
            await host.ClickAsync("Echo");
            Assert.True(await host.Page.EvaluateAsync<bool>("window.hostClicks.includes('echo')"));
        }

        await modal.Page.GetByRole(SparRole.Button, new() { Name = "Answer yes", Exact = true }).ClickAsync();
        await modal.Window.Closed.WaitAsync(TimeSpan.FromSeconds(10), Token);

        await Expect(host.Page.GetByText("Confirm 1:")).ToHaveTextAsync("""Confirm 1: {"answer":"yes"}""");
        Assert.False(openerTakesInput);
        Assert.True(modalTakesInput);
        Assert.True(panelTakesInput);
        Assert.True(await host.App.Window.TakesInputAsync());
        Assert.Equal(["panel-1", "confirm-1"], host.App.Windows.Select(w => w.Id));
        host.AssertClean();
    }

    [Fact]
    public async Task A_modal_closed_any_other_way_is_dismissed_and_a_modal_may_show_another()
    {
        await using var host = await HostPage.OpenAsync();
        await host.ClickAsync("Show the confirm modal");
        var first = await host.App.WindowAsync("confirm", cancellationToken: Token);
        await Expect(first.Page.GetByText("This window: /confirm")).ToHaveTextAsync("""This window: /confirm {"question":"Sure?"}""");

        await first.Page.GetByRole(SparRole.Button, new() { Name = "Show the confirm modal", Exact = true }).ClickAsync();
        var second = await host.App.WindowAsync("confirm", 1, Token);
        var takesInput = (Main: await host.App.Window.TakesInputAsync(), First: await first.Window.TakesInputAsync(), Second: await second.Window.TakesInputAsync());

        // Closing the first closes its own modal first, which answers the first's page before it goes.
        var closed = await first.Window.CloseAsync();
        await second.Window.Closed.WaitAsync(TimeSpan.FromSeconds(10), Token);

        await Expect(host.Page.GetByText("Confirm 1:")).ToHaveTextAsync("Confirm 1: dismissed");
        Assert.Equal((false, false, true), takesInput);
        Assert.Equal(["confirm-1", "confirm-2"], new[] { first.Id, second.Id });
        Assert.Equal(CloseOutcome.Closed, closed.Outcome);
        Assert.True(await host.App.Window.TakesInputAsync());
    }

    [Fact]
    public async Task Closing_the_main_window_asks_the_others_first_and_one_that_keeps_itself_open_keeps_the_app_open()
    {
        await using var host = await HostPage.OpenAsync();
        await host.ClickAsync("Open a note");
        var note = await host.App.WindowAsync("note", cancellationToken: Token);
        await host.ClickAsync("Open the panel");
        var panel = await host.App.WindowAsync("panel", cancellationToken: Token);
        await Expect(note.Page.GetByText("This window: /note")).ToHaveTextAsync("""This window: /note {"text":"hello","n":1}""");
        await Expect(panel.Page.GetByText("This window: /panel")).ToHaveTextAsync("This window: /panel null");
        await note.Page.GetByLabel("Close guard").SelectOptionAsync("keep");
        var activity = host.Activity;

        // The panel, opened last, is asked first and closes; the note keeps itself open, so the main window stays.
        var kept = await host.App.Window.CloseAsync();
        await panel.Window.Closed.WaitAsync(TimeSpan.FromSeconds(10), Token);
        await host.ClickAsync("Echo");
        await Expect(host.Status).ToHaveTextAsync("Echo: hello");
        await Expect(note.Page.GetByText("This window: /note")).ToBeVisibleAsync();

        await note.Page.GetByLabel("Close guard").SelectOptionAsync("none");
        var closed = await host.App.Window.CloseAsync();

        Assert.Equal(CloseOutcome.Kept, kept.Outcome);
        Assert.Equal(CloseOutcome.Closed, closed.Outcome);
        Assert.True(note.Window.Closed.IsCompleted);
        Assert.Contains("close user automated keep", HostPage.NotesOf(activity));
    }

    [Fact]
    public async Task A_window_with_a_modal_open_closes_the_modal_first()
    {
        await using var host = await HostPage.OpenAsync();
        await host.ClickAsync("Show the confirm modal");
        var modal = await host.App.WindowAsync("confirm", cancellationToken: Token);

        // Its guard counts once its page is connected: the params it shows come over the bridge.
        await Expect(modal.Page.GetByText("This window: /confirm")).ToHaveTextAsync("""This window: /confirm {"question":"Sure?"}""");
        await modal.Page.GetByLabel("Close guard").SelectOptionAsync("keep");

        // The modal's guard keeps it, so its owner stays open too; then it lets it go.
        var kept = await host.App.Window.CloseAsync();
        var stillBlocked = !await host.App.Window.TakesInputAsync();
        await modal.Page.GetByLabel("Close guard").SelectOptionAsync("none");
        var closed = await host.App.Window.CloseAsync();

        Assert.Equal(CloseOutcome.Kept, kept.Outcome);
        Assert.True(stillBlocked);
        Assert.Equal(CloseOutcome.Closed, closed.Outcome);
        Assert.True(modal.Window.Closed.IsCompleted);
    }
}
