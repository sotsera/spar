using System.Text;
using System.Text.Json;
using Spar.AppWindows;
using Spar.Bridge;
using Spar.Files;
using Spar.Hosting;
using Spar.Runtime;

namespace Spar.Tests.AppWindows;

/// <summary>
/// Window kinds in the configuration and the runtime (decision "M3d's decide-first questions", items 3 and 4, and
/// decision "Per-window command allow-lists"): names and routes, the start's checks, the allow-list's refusal, the
/// page's window commands, the single-instance rule and the route fallback.
/// </summary>
public sealed class WindowKindTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("settings")]
    [InlineData("queryEditor")]
    [InlineData("page2")]
    public void A_kind_is_camelCase_ASCII(string kind) => Assert.Equal(kind, WindowRoutes.CheckKind(kind));

    [Theory]
    [InlineData("Settings")]
    [InlineData("query-editor")]
    [InlineData("2nd")]
    [InlineData("éditeur")]
    [InlineData("main")]
    public void Other_names_and_main_are_refused(string kind) => Assert.Throws<ArgumentException>(() => WindowRoutes.CheckKind(kind));

    [Theory]
    [InlineData("/settings", "/settings")]
    [InlineData("/settings/", "/settings")]
    [InlineData("/reports/monthly", "/reports/monthly")]
    [InlineData("/a-b_c.d", "/a-b_c.d")]
    public void A_route_is_an_absolute_path_of_the_origin(string route, string expected) => Assert.Equal(expected, WindowRoutes.Check(route, "k"));

    [Theory]
    [InlineData("settings")]
    [InlineData("/a//b")]
    [InlineData("/a/../b")]
    [InlineData("/a?x=1")]
    [InlineData("/a#b")]
    [InlineData("/_spar/blob")]
    [InlineData("https://example.com/")]
    public void Other_routes_are_refused(string route) => Assert.Throws<ArgumentException>(() => WindowRoutes.Check(route, "k"));

    [Fact]
    public void A_path_is_under_a_route_at_a_segment_boundary()
    {
        Assert.True(WindowRoutes.IsUnder("/settings", "/settings"));
        Assert.True(WindowRoutes.IsUnder("/settings/connections", "/settings"));
        Assert.False(WindowRoutes.IsUnder("/settingsx", "/settings"));
        Assert.False(WindowRoutes.IsUnder("/other", "/settings"));
    }

    [Fact]
    public void A_duplicate_kind_a_kind_without_a_route_and_a_kinds_colours_fail_the_start()
    {
        var builder = new SparBuilder([]);
        builder.Window("settings", w => w.Route("/settings"));

        var twice = Assert.Throws<InvalidOperationException>(() => builder.Window("settings", w => w.Route("/other")));
        var colours = Assert.Throws<InvalidOperationException>(() => builder.Window("report", w => w.Background("#fff", "#000")));
        var routeless = new SparBuilder([]);
        routeless.Window("report", w => w.Size(400, 300));
        var noRoute = Assert.Throws<InvalidOperationException>(() => routeless.ResolveWindows(Catalog()));
        var mainRoute = Assert.Throws<InvalidOperationException>(() => new SparBuilder([]).Window(w => w.Route("/x")));

        Assert.Contains("declared twice", twice.Message, StringComparison.Ordinal);
        Assert.Contains("main window's", colours.Message, StringComparison.Ordinal);
        Assert.Contains("has no route", noRoute.Message, StringComparison.Ordinal);
        Assert.Contains("main window shows", mainRoute.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_allow_list_resolves_to_contract_names_and_fails_on_a_type_or_command_the_app_lacks()
    {
        var builder = new SparBuilder([]);
        builder.Window("settings", w => w.Route("/settings").Commands(c => c.Allow<EchoContract>().Allow<PagesContract>("get")));
        builder.ResolveWindows(Catalog());
        var access = builder.WindowKinds["settings"].Access!;

        var notContract = new SparBuilder([]);
        notContract.Window("a", w => w.Route("/a").Commands(c => c.Allow<string>()));
        var missing = new SparBuilder([]);
        missing.Window("a", w => w.Route("/a").Commands(c => c.Allow<PagesContract>("remove")));

        Assert.True(access.Allows("echo", "back"));
        Assert.True(access.Allows("pages", "get"));
        Assert.False(access.Allows("pages", "delete"));
        Assert.False(access.Allows("slow", "wait"));
        Assert.Contains("not one of the app's contracts", Assert.Throws<InvalidOperationException>(() => notContract.ResolveWindows(Catalog())).Message, StringComparison.Ordinal);
        Assert.Contains("pages.remove", Assert.Throws<InvalidOperationException>(() => missing.ResolveWindows(Catalog())).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_call_outside_the_windows_allow_list_is_refused_before_it_runs_and_Spars_own_commands_stay_allowed()
    {
        var builder = new SparBuilder([]);
        builder.Window("settings", w => w.Route("/settings").Commands(c => c.Allow<EchoContract>()));
        builder.ResolveWindows(Catalog());
        var window = new HostWindow(TimeProvider.System, null) { Name = "settings", Id = "settings-1", Access = builder.WindowKinds["settings"].Access };
        await using var bridge = await TestBridge.OpenAsync(window: window);

        bridge.Session.Receive(TestBridge.Call(1, "echo", "back", "\"hi\""));
        var allowed = await bridge.Sent.NextAsync();
        bridge.Session.Receive(TestBridge.Call(2, "pages", "get", "{}"));
        var refused = await bridge.Sent.NextAsync();
        bridge.Session.Receive(TestBridge.Call(3, "spar", "windowParams", ""));
        var own = await bridge.Sent.NextAsync();

        Assert.Equal(BridgeMessageType.Result, allowed.Type);
        Assert.Equal(("rejected", "forbidden-command", "notStarted"), (refused.Kind, refused.Code, refused.Execution));
        Assert.Equal(BridgeMessageType.Result, own.Type);
    }

    [Fact]
    public async Task A_window_reads_the_params_its_opener_gave_and_null_without_any()
    {
        var with = new HostWindow(TimeProvider.System, null) { Params = """{"tab":"connections","n":3}"""u8.ToArray() };
        await using var bridge = await TestBridge.OpenAsync(window: with);
        await using var plain = await TestBridge.OpenAsync(window: new HostWindow(TimeProvider.System, null));

        bridge.Session.Receive(TestBridge.Call(1, "spar", "windowParams", ""));
        plain.Session.Receive(TestBridge.Call(1, "spar", "windowParams", ""));

        Assert.Equal("""{"tab":"connections","n":3}""", Encoding.UTF8.GetString((await bridge.Sent.NextAsync()).Payload.Span));
        Assert.Equal("null", Encoding.UTF8.GetString((await plain.Sent.NextAsync()).Payload.Span));
    }

    [Fact]
    public async Task A_page_without_windows_of_its_own_gets_notOpened_and_a_malformed_request_is_refused()
    {
        await using var bridge = await TestBridge.OpenAsync(window: new HostWindow(TimeProvider.System, null));

        bridge.Session.Receive(TestBridge.Call(1, "spar", "openWindow", """{"kind":"settings","params":{"a":1}}"""));
        var notOpened = await bridge.Sent.NextAsync();
        bridge.Session.Receive(TestBridge.Call(2, "spar", "openWindow", """{"params":{}}"""));
        var noKind = await bridge.Sent.NextAsync();
        var big = new string('x', WindowCommands.MaxParamsBytes);
        bridge.Session.Receive(TestBridge.Call(3, "spar", "openWindow", $$"""{"kind":"settings","params":"{{big}}"}"""));
        var tooBig = await bridge.Sent.NextAsync();

        var error = JsonDocument.Parse(notOpened.Payload).RootElement;
        Assert.Equal((BridgeMessageType.Result, false), (notOpened.Type, notOpened.Ok!.Value));
        Assert.Equal("notOpened", error.GetProperty("code").GetString());
        Assert.Contains("not in a browser", error.GetProperty("reason").GetString(), StringComparison.Ordinal);
        Assert.Equal(("rejected", "invalid-request"), (noKind.Kind, noKind.Code));
        Assert.Equal(("rejected", "invalid-request"), (tooBig.Kind, tooBig.Code));
    }

    [Fact]
    public void The_request_keeps_the_params_own_bytes_and_null_means_none()
    {
        Assert.True(WindowCommands.TryRead("""{ "params" : [1, {"b":true}], "kind":"a" }"""u8.ToArray(), out var kind, out var parameters, out _));
        Assert.True(WindowCommands.TryRead("""{"kind":"b","params":null}"""u8.ToArray(), out _, out var none, out _));
        Assert.False(WindowCommands.TryRead("""{"kind":"a"} x"""u8.ToArray(), out _, out _, out _));

        Assert.Equal(("a", """[1, {"b":true}]"""), (kind, WindowCommands.Text(parameters)));
        Assert.True(none.IsEmpty);
    }

    [Fact]
    public async Task Windows_get_numbered_ids_a_single_instance_kind_is_focused_and_nothing_opens_once_the_app_closes()
    {
        var builder = new SparBuilder([]);
        builder.Window("editor", w => w.Route("/editor"));
        builder.Window("settings", w => w.Route("/settings").SingleInstance());
        var host = new FakeHost();
        var windows = new AppWindowSet(builder, host);

        var first = await windows.OpenAsync("editor", ReadOnlyMemory<byte>.Empty, "main", Token);
        var second = await windows.OpenAsync("editor", """{"id":2}"""u8.ToArray(), "main", Token);
        var settings = await windows.OpenAsync("settings", ReadOnlyMemory<byte>.Empty, "editor-1", Token);
        var again = await windows.OpenAsync("settings", ReadOnlyMemory<byte>.Empty, "main", Token);
        var unknown = await windows.OpenAsync("nothing", ReadOnlyMemory<byte>.Empty, "main", Token);
        host.Fail = true;
        var failed = await windows.OpenAsync("editor", ReadOnlyMemory<byte>.Empty, "main", Token);
        windows.Closed("settings-1");
        host.Fail = false;
        var reopened = await windows.OpenAsync("settings", ReadOnlyMemory<byte>.Empty, "main", Token);
        windows.Closing();
        var closing = await windows.OpenAsync("editor", ReadOnlyMemory<byte>.Empty, "main", Token);

        Assert.Equal([OpenOutcome.Opened, OpenOutcome.Opened, OpenOutcome.Opened, OpenOutcome.Focused, OpenOutcome.UnknownKind], new[] { first, second, settings, again, unknown }.Select(r => r.Outcome));
        Assert.Equal(["editor-1", "editor-2", "settings-1", "settings-2"], host.Opened.Select(w => w.Id));
        Assert.Equal("settings-1", Assert.Single(host.Focused).Id);
        Assert.Equal("editor-1", host.Opened[2].OpenerId);
        Assert.Equal("""{"id":2}""", WindowCommands.Text(host.Opened[1].Params));
        Assert.Equal((OpenOutcome.NotOpened, "no window here"), (failed.Outcome, failed.Reason));
        Assert.Equal(OpenOutcome.Opened, reopened.Outcome);
        Assert.Equal((OpenOutcome.NotOpened, "The app is closing."), (closing.Outcome, closing.Reason));
        Assert.Equal(["editor-1", "editor-2", "settings-2"], windows.Open.Select(w => w.Id).Order());
    }

    [Fact]
    public void A_route_and_its_views_get_the_page_and_a_file_below_it_does_not()
    {
        var responder = new PageResponder(null, SparPageSecurity.Default, "https://app.localhost", development: false) { Routes = ["/settings"] };
        Assert.True(responder.IsRoute("/settings"));
        Assert.True(responder.IsRoute("/settings/connections"));
        Assert.False(responder.IsRoute("/settings/logo.png"));
        Assert.False(responder.IsRoute("/other"));
    }

    private static SparCatalog Catalog() =>
        new(
            "0000000000000000",
            [
                new("echo", "back", 0, call => ValueTask.FromResult(SparOutcome.FromValue(call.Request.ToArray()))),
                new("pages", "get", 0, _ => ValueTask.FromResult(SparOutcome.FromValue("{}"u8.ToArray()))),
                new("pages", "delete", 0, _ => ValueTask.FromResult(SparOutcome.FromValue(ReadOnlyMemory<byte>.Empty))),
            ],
            _ => { })
        {
            ContractTypes = [new(typeof(EchoContract), "echo"), new(typeof(PagesContract), "pages")],
        };

    private sealed class EchoContract;

    private sealed class PagesContract;

    private sealed class FakeHost : IAppWindowHost
    {
        public List<OpenWindow> Opened { get; } = [];
        public List<OpenWindow> Focused { get; } = [];
        public bool Fail { get; set; }

        public Task OpenAsync(OpenWindow window, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new InvalidOperationException("no window here");
            }

            Opened.Add(window);
            return Task.CompletedTask;
        }

        public void Focus(OpenWindow window) => Focused.Add(window);
    }
}
