using System.Text;
using System.Text.Json;
using Spar.AppWindows;
using Spar.Bridge;
using Spar.Files;
using Spar.Hosting;

namespace Spar.Tests.AppWindows;

/// <summary>
/// Modals and the close order in the runtime (decision "M3d's decide-first questions", items 3 and 4): a modal kind
/// is shown, never opened, and its opener's call answers once it closes, with <c>closeWith</c>'s value or
/// <c>dismissed</c>; a result nobody waits for any more is dropped and logged; closing the main window asks every other
/// first, a window its own modals, the latest first, and one that stays open keeps the window open.
/// </summary>
public sealed class ModalTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_modal_answers_its_opener_with_the_closeWith_value_and_dismissed_when_it_closes_otherwise()
    {
        var (windows, host) = Windows();
        await using var main = await TestBridge.OpenAsync(window: new HostWindow(TimeProvider.System, null) { Windows = windows });

        main.Session.Receive(TestBridge.Call(1, "spar", "showModal", """{"kind":"confirm","params":{"q":1}}"""));
        var modal = await host.NextOpenedAsync();
        await using var page = await TestBridge.OpenAsync(window: new HostWindow(TimeProvider.System, null) { Windows = windows, Id = modal.Id, Params = modal.Params });
        page.Session.Receive(TestBridge.Call(1, "spar", "closeWindow", """{"value":{"ok":true,"n":[1,2]}}"""));
        var closed = await page.Sent.NextAsync();
        var value = await main.Sent.NextAsync();

        main.Session.Receive(TestBridge.Call(2, "spar", "showModal", """{"kind":"confirm","params":null}"""));
        var second = await host.NextOpenedAsync();
        windows.Closed(second.Id);
        var dismissed = await main.Sent.NextAsync();

        Assert.Equal(("confirm-1", "main", """{"q":1}"""), (modal.Id, modal.OpenerId, WindowCommands.Text(modal.Params)));
        Assert.Equal((BridgeMessageType.Result, true), (closed.Type, closed.Ok!.Value));
        Assert.Equal(["confirm-1"], host.Closes.Select(w => w.Id));
        Assert.Equal((true, """{"ok":true,"n":[1,2]}"""), (value.Ok!.Value, Encoding.UTF8.GetString(value.Payload.Span)));
        Assert.Equal((false, "dismissed"), (dismissed.Ok!.Value, JsonDocument.Parse(dismissed.Payload).RootElement.GetProperty("code").GetString()));
        Assert.Empty(windows.Open);
    }

    [Fact]
    public async Task A_modal_kind_is_shown_never_opened_and_closeWith_is_for_the_app_kinds_windows_only()
    {
        var (windows, _) = Windows();
        await using var main = await TestBridge.OpenAsync(window: new HostWindow(TimeProvider.System, null) { Windows = windows });
        await using var browser = await TestBridge.OpenAsync(window: new HostWindow(TimeProvider.System, null));

        main.Session.Receive(TestBridge.Call(1, "spar", "openWindow", """{"kind":"confirm"}"""));
        var opened = await main.Sent.NextAsync();
        main.Session.Receive(TestBridge.Call(2, "spar", "showModal", """{"kind":"panel"}"""));
        var shown = await main.Sent.NextAsync();
        main.Session.Receive(TestBridge.Call(3, "spar", "closeWindow", """{"value":1}"""));
        var mainClose = await main.Sent.NextAsync();
        var big = new string('x', WindowCommands.MaxParamsBytes);
        main.Session.Receive(TestBridge.Call(4, "spar", "closeWindow", $$"""{"value":"{{big}}"}"""));
        var tooBig = await main.Sent.NextAsync();
        browser.Session.Receive(TestBridge.Call(1, "spar", "showModal", """{"kind":"confirm"}"""));
        var notOpened = await browser.Sent.NextAsync();
        browser.Session.Receive(TestBridge.Call(2, "spar", "closeWindow", """{"value":null}"""));
        var browserClose = await browser.Sent.NextAsync();

        Assert.All([opened, shown, mainClose, tooBig, browserClose], sent => Assert.Equal(("rejected", "invalid-request"), (sent.Kind, sent.Code)));
        Assert.Equal("notOpened", JsonDocument.Parse(notOpened.Payload).RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_result_nobody_waits_for_is_dropped_and_logged_and_an_open_single_instance_modal_is_focused()
    {
        var warnings = new List<string>();
        var (windows, host) = Windows(warnings.Add);

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var waiting = windows.ShowModalAsync("settings", ReadOnlyMemory<byte>.Empty, "main", stop.Token);
        var modal = await host.NextOpenedAsync();
        var again = await windows.ShowModalAsync("settings", ReadOnlyMemory<byte>.Empty, "panel-1", Token);
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        var closed = windows.CloseWith(modal.Id, "42"u8.ToArray());

        Assert.True(closed);
        Assert.Equal((OpenOutcome.NotOpened, null), (again.Opened.Outcome, again.Result));
        Assert.Contains("settings-1", again.Opened.Reason, StringComparison.Ordinal);
        Assert.Equal(["settings-1"], host.Focused.Select(w => w.Id));
        Assert.Contains("closeWith value", Assert.Single(warnings), StringComparison.Ordinal);
        Assert.Contains("dropped", warnings[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Closing_the_main_window_asks_the_others_latest_first_and_a_window_closes_its_own_modals_first()
    {
        var asked = new List<string>();
        var family = new WindowFamily<FakeWindow>();
        var main = new FakeWindow("main", null, asked);
        var panel = new FakeWindow("panel-1", Opened("panel", "main", modal: false), asked);
        var mainModal = new FakeWindow("confirm-1", Opened("confirm", "main", modal: true), asked);
        var panelModal = new FakeWindow("confirm-2", Opened("confirm", "panel-1", modal: true), asked);
        foreach (var window in new[] { main, panel, mainModal, panelModal })
        {
            family.Add(window);
        }

        var forPanel = family.ClosesFirst(panel).Select(w => w.Id).ToList();
        var forMain = family.ClosesFirst(main).Select(w => w.Id).ToList();
        panel.Keeps = true;
        var kept = await family.CloseFirstAsync(main, CloseReason.User, automated: true, questions: null);
        var askedWhenKept = asked.ToList();
        panel.Keeps = false;
        var none = await family.CloseFirstAsync(main, CloseReason.User, automated: true, questions: null);

        Assert.Equal(["confirm-2"], forPanel);
        Assert.Equal(["confirm-2", "confirm-1", "panel-1"], forMain);
        Assert.Equal(("panel-1", "keep"), (kept?.Window.Id, kept?.Report.Question));
        Assert.Equal(["confirm-2", "confirm-1", "panel-1"], askedWhenKept);
        Assert.Null(none);

        // The modals closed the first time; only the panel, which stayed open, is asked again.
        Assert.Equal(["confirm-2", "confirm-1", "panel-1", "panel-1"], asked);
    }

    private static OpenWindow Opened(string kind, string opener, bool modal)
    {
        var builder = new SparBuilder([]);
        builder.Window(kind, w =>
        {
            w.Route("/" + kind);
            if (modal)
            {
                w.Modal();
            }
        });
        return new OpenWindow { Id = kind, Options = builder.WindowKinds[kind], OpenerId = opener };
    }

    private static (AppWindowSet Windows, FakeHost Host) Windows(Action<string>? warn = null)
    {
        var builder = new SparBuilder([]);
        builder.Window("panel", w => w.Route("/panel"));
        builder.Window("confirm", w => w.Route("/confirm").Modal());
        builder.Window("settings", w => w.Route("/settings").Modal().SingleInstance());
        var host = new FakeHost();
        var windows = new AppWindowSet(builder, host, warn);
        host.Set = windows;
        return (windows, host);
    }

    private sealed class FakeHost : IAppWindowHost
    {
        private readonly System.Threading.Channels.Channel<OpenWindow> _opened = System.Threading.Channels.Channel.CreateUnbounded<OpenWindow>();

        public AppWindowSet? Set { get; set; }
        public List<OpenWindow> Focused { get; } = [];
        public List<OpenWindow> Closes { get; } = [];

        public Task OpenAsync(OpenWindow window, CancellationToken cancellationToken)
        {
            _opened.Writer.TryWrite(window);
            return Task.CompletedTask;
        }

        public void Focus(OpenWindow window) => Focused.Add(window);

        public void Close(OpenWindow window)
        {
            Closes.Add(window);
            Set?.Closed(window.Id);
        }

        public async Task<OpenWindow> NextOpenedAsync() => await _opened.Reader.ReadAsync(Token).AsTask().WaitAsync(TimeSpan.FromSeconds(10), Token);
    }

    private sealed class FakeWindow(string id, OpenWindow? opened, List<string> asked) : IFamilyWindow
    {
        public string Id { get; } = id;
        public OpenWindow? Opened { get; } = opened;
        public bool IsClosing { get; private set; }
        public bool Keeps { get; set; }

        public Task<CloseReport> RequestCloseAsync(CloseReason reason, bool automated, ICloseQuestions? questions)
        {
            asked.Add(Id);
            IsClosing = !Keeps;
            return Task.FromResult(new CloseReport(closed: !Keeps, Keeps ? "keep" : null, unanswered: false));
        }

        public Task CloseNowAsync()
        {
            IsClosing = true;
            return Task.CompletedTask;
        }
    }
}
