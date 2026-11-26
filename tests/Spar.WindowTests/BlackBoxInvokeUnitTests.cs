using System.Diagnostics;
using Spar.Testing;

namespace Spar.WindowTests;

/// <summary>
/// The black-box locator's invoke without an app, on a fake accessibility client: it waits for the page to settle
/// before an app's first action, waits for the browser's confirmation (or the element gone), and fails on a press
/// that is never confirmed after pressing exactly once (decision "Black-box invokes are confirmed"). With the fast
/// tests (no trait).
/// </summary>
public sealed class BlackBoxInvokeUnitTests
{
    [Fact]
    public async Task An_unconfirmed_invoke_fails_after_one_press_and_is_never_repeated()
    {
        var automation = new FakeAutomation { Confirms = false };
        var app = Launched(automation);

        var failure = await Assert.ThrowsAsync<TimeoutException>(() => Button(app).InvokeAsync(new() { Timeout = 300 }));

        Assert.Contains("did not confirm", failure.Message, StringComparison.Ordinal);
        Assert.Contains("It was not pressed again", failure.Message, StringComparison.Ordinal);
        Assert.Equal(1, automation.Invokes);
        Assert.True(automation.LastConfirmation!.Disposed, "The confirmation's registration was not removed.");
    }

    [Fact]
    public async Task A_confirmed_invoke_returns_and_removes_its_registration()
    {
        var automation = new FakeAutomation { Confirms = true };
        var app = Launched(automation);

        await Button(app).InvokeAsync(new() { Timeout = 2000 });

        Assert.Equal(1, automation.Invokes);
        Assert.True(automation.LastConfirmation!.Disposed);
    }

    [Fact]
    public async Task A_press_that_removed_its_element_counts_as_taken()
    {
        // A dialog's button that closes it, a link that leaves the page: the browser drops the Invoked event with the node.
        var automation = new FakeAutomation { Confirms = false, Gone = true };
        var app = Launched(automation);

        await Button(app).InvokeAsync(new() { Timeout = 2000 });

        Assert.Equal(1, automation.Invokes);
        Assert.True(automation.LastConfirmation!.Disposed);
    }

    [Fact]
    public async Task The_first_invoke_waits_until_the_page_has_settled_and_later_ones_do_not()
    {
        var automation = new FakeAutomation { Confirms = true, PageAfter = 3, PageSettleTime = TimeSpan.FromMilliseconds(400) };
        var app = Launched(automation);

        var clock = Stopwatch.StartNew();
        await Button(app).InvokeAsync();
        var first = clock.Elapsed;
        clock.Restart();
        await Button(app).InvokeAsync();
        var second = clock.Elapsed;

        // The page appeared at the third look (25 ms apart), then 400 ms passed before the press.
        Assert.Equal(3, automation.PageLooks);
        Assert.True(first >= TimeSpan.FromMilliseconds(390), $"The first invoke took {first.TotalMilliseconds:0} ms.");
        Assert.True(second < TimeSpan.FromMilliseconds(200), $"The second invoke took {second.TotalMilliseconds:0} ms.");
        Assert.True(automation.InvokedAfterPage >= TimeSpan.FromMilliseconds(390), $"Pressed {automation.InvokedAfterPage.TotalMilliseconds:0} ms after the page appeared.");
    }

    [Fact]
    public async Task A_page_that_never_appears_fails_the_invoke_without_pressing()
    {
        var automation = new FakeAutomation { Confirms = true, PageAfter = int.MaxValue };
        var app = Launched(automation);

        var failure = await Assert.ThrowsAsync<TimeoutException>(() => Button(app).InvokeAsync(new() { Timeout = 200 }));

        Assert.Contains("never appeared in the window's accessibility tree", failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, automation.Invokes);
    }

    private static SparBlackBoxLocator Button(SparBlackBoxApp app) => app.Window.GetByRole(SparRole.Button, new() { Name = "Save" });

    private static SparBlackBoxApp Launched(FakeAutomation automation) => new(
        new SparProject("Fake", "com.example.Fake", "Fake.exe", ""),
        new SparBlackBoxOptions(),
        BlackBoxFolders.For(Path.Combine(Path.GetTempPath(), "spar-fake-root")),
        ownsRoot: false,
        new FakeProcess(),
        automation,
        Stopwatch.StartNew());

    private sealed class FakeAutomation : IBlackBoxAutomation
    {
        private long _pageSeen;

        public bool Confirms { get; init; }

        public bool Gone { get; init; }

        public int PageAfter { get; init; } = 1;

        public int PageLooks { get; private set; }

        public int Invokes { get; private set; }

        public TimeSpan InvokedAfterPage { get; private set; }

        public FakeConfirmation? LastConfirmation { get; private set; }

        public string? Desktop => null;

        public TimeSpan PageSettleTime { get; init; } = TimeSpan.Zero;

        public Task<bool> HasPageAsync(int processId, CancellationToken cancellationToken = default)
        {
            PageLooks++;
            var shown = PageLooks >= PageAfter;
            if (shown && _pageSeen == 0)
            {
                _pageSeen = Stopwatch.GetTimestamp();
            }

            return Task.FromResult(shown);
        }

        public Task<ElementOutcome> InvokeAsync(int processId, ElementTarget target, CancellationToken cancellationToken = default)
        {
            Invokes++;
            InvokedAfterPage = Stopwatch.GetElapsedTime(_pageSeen);
            LastConfirmation = new FakeConfirmation(Confirms, Gone);
            return Task.FromResult(new ElementOutcome(ElementStatus.Done, Confirmation: LastConfirmation));
        }

        public Task<string?> TitleAsync(int processId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<FoundElement>> FindAsync(int processId, ElementTarget target, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ElementOutcome> ValueAsync(int processId, ElementTarget target, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> CloseWindowAsync(int processId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DialogContent?> FindDialogAsync(int processId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> PressAsync(DialogContent dialog, string button, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ElementOutcome> BoundsAsync(int processId, ElementTarget target, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ElementOutcome> FocusAsync(int processId, ElementTarget target, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<WindowParts?> WindowPartsAsync(int processId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<TopLevelWindow>> TopLevelWindowsAsync(IReadOnlyList<int> processIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ScreenRect?> DialogButtonBoundsAsync(DialogContent dialog, string button, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> EndSessionAsync(int processId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string> DumpAsync(IReadOnlyList<int> processIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeConfirmation(bool confirms, bool gone) : IInvokeConfirmation
    {
        public Task<bool> IsGoneAsync() => Task.FromResult(gone);

        public bool Disposed { get; private set; }

        public Task Invoked { get; } = confirms ? Task.CompletedTask : new TaskCompletionSource().Task;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeProcess : IBlackBoxProcess
    {
        public int ProcessId => 1;

        public string StandardOutput => "";

        public string StandardError => "";

        public Task<int> Exited { get; } = new TaskCompletionSource<int>().Task;

        public IReadOnlyList<int> ProcessIds => [1];

        public void Kill()
        {
        }

        public Task<bool> WaitForTreeAsync(TimeSpan limit) => Task.FromResult(true);

        public Task<bool> WaitForOutputAsync(TimeSpan limit) => Task.FromResult(true);

        public void SuspendProcess()
        {
        }

        public void Dispose()
        {
        }
    }
}
