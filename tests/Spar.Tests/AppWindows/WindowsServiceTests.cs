using Spar.AppWindows;
using Spar.Bridge;
using Spar.Files;

namespace Spar.Tests.AppWindows;

/// <summary>
/// <see cref="IWindows"/> and the page's own window operations (decision "M3d's decide-first questions", item 8): the
/// app's windows for its C# code, opened, focused or refused, with their events on the pool; and
/// <c>spar.windowAction</c>, which reaches the calling window's handle and refuses what the host would not take.
/// </summary>
public sealed class WindowsServiceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_app_opens_and_focuses_windows_of_its_kinds_and_hears_them_open_and_close()
    {
        var (service, set, host) = Connected();
        var main = new FakeControl();
        set.AttachMain(main);
        var opened = new List<string>();
        var closed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Opened += (_, window) => { lock (opened) { opened.Add(window.Id); } };
        service.Closed += (_, window) => closed.TrySetResult(window.Id);

        var note = await service.OpenAsync("note", """{"n":1}""", Token);
        var panel = await service.OpenAsync("panel", cancellationToken: Token);
        var again = await service.OpenAsync("panel", cancellationToken: Token);
        var all = service.All.Select(w => w.Id).ToList();
        set.Closed("note-1");
        var closedId = await closed.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        service.Main.Minimize();

        Assert.Equal("note-1", Opened(note).Id);
        Assert.Same(Opened(panel), Opened(again));
        Assert.Equal(["main", "note-1", "panel-1"], all);
        Assert.Equal("""{"n":1}""", WindowCommands.Text(host.Windows[0].Params));
        Assert.Equal("main", host.Windows[0].OpenerId);
        Assert.Equal(["panel-1"], host.Focused);
        Assert.Equal("note-1", closedId);
        Assert.False(Opened(note).IsOpen);
        Assert.Equal(["main", "panel-1"], service.All.Select(w => w.Id));
        Assert.Equal(["minimize"], main.Calls);
        await Task.Delay(50, Token);
        lock (opened)
        {
            Assert.Equal(["note-1", "panel-1"], opened.Order());
        }
    }

    [Fact]
    public async Task A_modal_an_unknown_kind_and_params_that_are_not_an_object_are_refused_and_no_host_opens_none()
    {
        var (service, _, _) = Connected();
        var alone = new SparWindows();

        await Assert.ThrowsAsync<ArgumentException>(() => service.OpenAsync("confirm", cancellationToken: Token));
        await Assert.ThrowsAsync<ArgumentException>(() => service.OpenAsync("nothing", cancellationToken: Token));
        await Assert.ThrowsAsync<ArgumentException>(() => service.OpenAsync("note", "[1,2]", Token));
        await Assert.ThrowsAsync<ArgumentException>(() => service.OpenAsync("note", "{", Token));
        var none = await alone.OpenAsync("note", cancellationToken: Token);

        Assert.Throws<InvalidOperationException>(() => alone.Main);
        Assert.Empty(alone.All);
        Assert.IsType<WindowOpenError.NotOpened>(none switch
        {
            IAppWindow => null,
            WindowOpenError error => error,
        });
    }

    [Fact]
    public async Task A_page_changes_its_own_window_and_a_request_the_host_would_refuse_is_refused()
    {
        var control = new FakeControl();
        var handle = new AppWindowHandle("note-1", "note");
        handle.Attach(control);
        await using var page = await TestBridge.OpenAsync(window: new HostWindow(TimeProvider.System, null) { Handle = handle, Id = "note-1" });
        await using var browser = await TestBridge.OpenAsync(window: new HostWindow(TimeProvider.System, null));

        string[] good =
        [
            """{"action":"minimize"}""",
            """{"action":"setSize","width":800,"height":600}""",
            """{"action":"setTitle","title":"Report"}""",
            """{"action":"close"}""",
        ];
        string[] bad =
        [
            """{"action":"fly"}""",
            """{"action":"setSize","width":0,"height":600}""",
            """{"action":"setSize","width":800}""",
            $$"""{"action":"setTitle","title":"{{new string('t', 1_025)}}"}""",
        ];
        var answers = new List<BridgeMessage>();
        var id = 1;
        foreach (var request in good.Concat(bad))
        {
            page.Session.Receive(TestBridge.Call(id++, "spar", "windowAction", request));
            answers.Add(await page.Sent.NextAsync());
        }

        browser.Session.Receive(TestBridge.Call(1, "spar", "windowAction", """{"action":"focus"}"""));
        var noWindow = await browser.Sent.NextAsync();

        Assert.All(answers[..4], answer => Assert.True(answer.Ok));
        Assert.All(answers[4..], answer => Assert.Equal(("rejected", "invalid-request"), (answer.Kind, answer.Code)));
        Assert.Equal(["minimize", "size 800x600", "title Report", "close"], control.Calls);
        Assert.Equal(("rejected", "invalid-request"), (noWindow.Kind, noWindow.Code));
    }

    private static IAppWindow Opened(Result<IAppWindow, WindowOpenError> result) => result switch
    {
        IAppWindow window => window,
        WindowOpenError error => throw new InvalidOperationException($"Not opened: {error}"),
    };

    private static (SparWindows Service, AppWindowSet Set, FakeHost Host) Connected()
    {
        var builder = new SparBuilder([]);
        builder.Window("note", w => w.Route("/note"));
        builder.Window("panel", w => w.Route("/panel").SingleInstance());
        builder.Window("confirm", w => w.Route("/confirm").Modal());
        var host = new FakeHost();
        var set = new AppWindowSet(builder, host);
        var service = new SparWindows();
        service.Use(set);
        return (service, set, host);
    }

    private sealed class FakeHost : IAppWindowHost
    {
        public List<OpenWindow> Windows { get; } = [];
        public List<string> Focused { get; } = [];

        public Task OpenAsync(OpenWindow window, CancellationToken cancellationToken)
        {
            window.Handle.Attach(new FakeControl());
            Windows.Add(window);
            return Task.CompletedTask;
        }

        public void Focus(OpenWindow window) => Focused.Add(window.Id);

        public void Close(OpenWindow window)
        {
        }
    }

    private sealed class FakeControl : IWindowControl
    {
        public List<string> Calls { get; } = [];

        public void Focus() => Calls.Add("focus");

        public void Minimize() => Calls.Add("minimize");

        public void Maximize() => Calls.Add("maximize");

        public void Restore() => Calls.Add("restore");

        public void SetSize(int width, int height) => Calls.Add($"size {width}x{height}");

        public void SetTitle(string title) => Calls.Add($"title {title}");

        public void SetZoom(double factor) => Calls.Add($"zoom {factor}");

        public Task<bool> CloseAsync()
        {
            Calls.Add("close");
            return Task.FromResult(true);
        }
    }
}
