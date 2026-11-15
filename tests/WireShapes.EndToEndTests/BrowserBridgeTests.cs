using System.Text.Json;
using Microsoft.Playwright;
using Spar.Testing;

namespace WireShapes.EndToEndTests;

/// <summary>The page in Chromium calling the app's backend in this process, over the loopback bridge.</summary>
public sealed class BrowserBridgeTests(SharedBrowser shared)
{
    [Fact]
    public async Task Values_declared_errors_and_their_payloads_reach_the_page()
    {
        await using var wire = await WirePage.OpenAsync(shared);

        Assert.Equal("{\"value\":\"hello\"}", await wire.CallAsync("name => window.wire.check(name)", "hello"));
        Assert.Equal("{\"refused\":\"said no\"}", await wire.CallAsync("name => window.wire.check(name)", "no"));
        Assert.Equal("{\"empty\":true}", await wire.CallAsync("name => window.wire.check(name)", ""));
        await wire.AssertCleanAsync();
    }

    [Fact]
    public async Task Text_survives_the_line_framing_both_ways()
    {
        await using var wire = await WirePage.OpenAsync(shared);
        const string Text = "line\u2028separator\u2029paragraph \ud83c\udf89 tab\tnewline\nend";
        const string Shapes = """
            {"note":null,"flag":true,"count":-7,"big":"-9223372036854775808","ratio":0.1,
            "key":"0f8fad5b-d9cb-469f-a165-70867728950e","at":"2026-11-15T10:20:30+01:00","maybe":null,"tags":["a","b"],
            "cells":["x",null],"items":[{"name":"i"}],"parent":null}
            """;

        // The text goes in as a Playwright argument, so the page's own JSON.stringify encodes it.
        var json = await wire.CallAsync("([shapes, text]) => window.wire.echoShapes({ text, ...JSON.parse(shapes) })", new[] { Shapes, Text });
        var echoed = JsonDocument.Parse(json).RootElement.GetProperty("value");

        Assert.Equal(Text, echoed.GetProperty("text").GetString());
        Assert.Equal("-9223372036854775808", echoed.GetProperty("big").GetString());
        Assert.Equal(DateTimeOffset.Parse("2026-11-15T10:20:30+01:00", System.Globalization.CultureInfo.InvariantCulture), echoed.GetProperty("at").GetDateTimeOffset());
        Assert.Equal("[\"x\",null]", echoed.GetProperty("cells").GetRawText());
        await wire.AssertCleanAsync();
    }

    [Fact]
    public async Task A_long_arrives_as_a_bigint_with_every_digit()
    {
        await using var wire = await WirePage.OpenAsync(shared);

        Assert.Equal("{\"value\":\"bigint 9007199254740993\"}", await wire.CallAsync("digits => window.wire.big(digits)", "9007199254740993"));
        await wire.AssertCleanAsync();
    }

    [Fact]
    public async Task An_aborted_call_is_reported_cancelled_once_the_backend_confirms()
    {
        await using var wire = await WirePage.OpenAsync(shared);

        var outcome = await wire.CallAsync("ms => window.wire.abortWait(ms)", 200);

        Assert.Equal("{\"failure\":{\"kind\":\"cancelled\",\"code\":\"cancelled\",\"execution\":\"stopped\",\"command\":\"wait\"}}", outcome);
        await wire.AssertCleanAsync();
    }

    [Fact]
    public async Task Many_calls_at_once_reach_the_host_in_order_and_each_gets_its_answer()
    {
        await using var wire = await WirePage.OpenAsync(shared);

        var json = await wire.CallAsync("count => window.wire.many(count)", 200);
        var names = JsonDocument.Parse(json).RootElement.EnumerateArray().Select(name => name.GetString());

        Assert.Equal(Enumerable.Range(0, 200).Select(i => $"n{i}"), names);
        await wire.AssertCleanAsync();
    }

    [Fact]
    public async Task A_command_that_throws_reaches_the_page_as_a_correlation_id_and_the_apps_log_has_the_details()
    {
        await using var wire = await WirePage.OpenAsync(shared);

        var outcome = JsonDocument.Parse(await wire.CallAsync("text => window.wire.ratio(text)", "not a number")).RootElement.GetProperty("failure");

        Assert.Equal(("failed", "failed"), (outcome.GetProperty("kind").GetString(), outcome.GetProperty("code").GetString()));
        var correlation = outcome.GetProperty("correlation").GetString()!;
        Assert.Matches("^[0-9a-f]{32}$", correlation);
        var entry = Assert.Single(wire.App.Log, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Error);
        Assert.Contains(correlation, entry.Message, StringComparison.Ordinal);
        Assert.Contains("echo.ratio", entry.Message, StringComparison.Ordinal);
        Assert.IsType<FormatException>(entry.Exception);
        await wire.AssertCleanAsync();
    }

    [Fact]
    public async Task A_page_built_for_other_contracts_is_refused_before_anything_runs()
    {
        await using var wire = await WirePage.OpenAsync(shared, contracts: "ffffffffffffffff");

        var outcome = await wire.CallAsync("name => window.wire.check(name)", "hello");

        Assert.Equal("{\"failure\":{\"kind\":\"rejected\",\"code\":\"contract-mismatch\",\"execution\":\"notStarted\",\"command\":\"check\"}}", outcome);
        await wire.AssertCleanAsync();
    }

    [Fact]
    public async Task The_token_leaves_the_address_and_a_reload_opens_a_new_document()
    {
        await using var wire = await WirePage.OpenAsync(shared);
        var first = await wire.CallAsync("() => window.wire.document()", null);

        await wire.Page.ReloadAsync();
        await wire.WaitForReadyAsync();

        Assert.DoesNotContain("token", wire.Page.Url, StringComparison.Ordinal);
        Assert.NotEqual(first, await wire.CallAsync("() => window.wire.document()", null));
        Assert.Equal("{\"value\":\"again\"}", await wire.CallAsync("name => window.wire.check(name)", "again"));
        await wire.AssertCleanAsync();
    }

    [Fact]
    public async Task The_generated_fingerprint_is_the_backends()
    {
        await using var wire = await WirePage.OpenAsync(shared);

        var generated = await wire.CallAsync("() => import('/spar/app/spar.js').then(module => module.fingerprint)", null);

        Assert.Equal($"\"{wire.App.Fingerprint}\"", generated);
        await wire.AssertCleanAsync();
    }

    [Fact]
    public async Task A_request_the_encoder_refuses_never_reaches_the_host()
    {
        await using var wire = await WirePage.OpenAsync(shared);
        const string Shapes = """
            {"text":"t","note":null,"flag":true,"count":1,"big":"1","ratio":0,"key":"0f8fad5b-d9cb-469f-a165-70867728950e",
            "at":"2026-11-15T10:20:30+01:00","maybe":null,"tags":[],"cells":[],"items":[],"parent":null}
            """;

        var outcome = await wire.CallAsync("json => window.wire.echoShapes({ ...JSON.parse(json), ratio: NaN })", Shapes);

        Assert.Equal("{\"failure\":{\"kind\":\"rejected\",\"code\":\"invalid-request\",\"execution\":\"notStarted\",\"command\":\"shapes\"}}", outcome);
        Assert.Empty(wire.App.Problems);
        Assert.Equal(["error: Spar: echo.shapes was not sent: Expected a finite number, got number NaN."], wire.ConsoleErrors);
    }

    [Fact]
    public async Task Another_origin_cannot_reach_the_bridge()
    {
        await using var wire = await WirePage.OpenAsync(shared);
        await using var other = await shared.Browser.NewContextAsync();
        var page = await other.NewPageAsync();
        await page.GotoAsync("data:text/html,<p>elsewhere</p>");

        // A page elsewhere that somehow learnt the token still sends its own origin.
        var status = await page.EvaluateAsync<int>(
            "async ([url, token]) => (await fetch(url, { method: 'POST', headers: { Authorization: 'Bearer ' + token }, body: 'x' }).catch(() => ({ status: 0 }))).status",
            new[] { new Uri(wire.App.Origin, "/_spar/channel").ToString(), wire.Token });

        // 0: the browser itself blocked the request (CORS preflight refused); 403: the host refused it.
        Assert.Contains(status, new[] { 0, 403 });
        await wire.AssertCleanAsync();
    }

    /// <summary>One app with its page open in a fresh browser context, and what the page reported.</summary>
    private sealed class WirePage : IAsyncDisposable
    {
        private readonly List<string> _consoleErrors = [];

        private WirePage(SparTestApp app, IBrowserContext context, IPage page, string token)
        {
            App = app;
            Context = context;
            Page = page;
            Token = token;
            page.Console += (_, message) =>
            {
                if (message.Type is "error" or "warning")
                {
                    lock (_consoleErrors)
                    {
                        _consoleErrors.Add($"{message.Type}: {message.Text}");
                    }
                }
            };
            page.PageError += (_, error) =>
            {
                lock (_consoleErrors)
                {
                    _consoleErrors.Add($"page error: {error}");
                }
            };
        }

        public SparTestApp App { get; }

        public IBrowserContext Context { get; }

        public IPage Page { get; }

        public string Token { get; }

        /// <summary>The console's errors and warnings so far, and uncaught page errors.</summary>
        public IReadOnlyList<string> ConsoleErrors
        {
            get
            {
                lock (_consoleErrors)
                {
                    return [.. _consoleErrors];
                }
            }
        }

        public static async Task<WirePage> OpenAsync(SharedBrowser shared, string? contracts = null)
        {
            var app = await SparTestApp.StartAsync<Program>(o =>
            {
                o.Surface = SparTestSurface.Browser;
                o.Page(Repository.Page).Page(Repository.Client, "spar/app/");
            });

            var context = await shared.Browser.NewContextAsync();
            // Without an override the page uses the fingerprint its generated package was built with.
            var url = app.PageUrl(contracts is null ? "/" : $"/?contracts={contracts}");
            var wire = new WirePage(app, context, await context.NewPageAsync(), url.Fragment["#token=".Length..]);
            await wire.Page.GotoAsync(url.ToString());
            await wire.WaitForReadyAsync();
            return wire;
        }

        public Task WaitForReadyAsync() => Assertions.Expect(Page.Locator("#status")).ToHaveTextAsync("ready");

        /// <summary>
        /// Runs one of the page's functions and returns its plain JSON result. Evaluation has no deadline of its own, and
        /// a call the host dropped is never answered, so the test sets one.
        /// </summary>
        public async Task<string> CallAsync(string function, object? argument)
        {
            var evaluation = Page.EvaluateAsync<JsonElement>($"async arg => JSON.stringify(await ({function})(arg))", argument);
            var result = await evaluation.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            return result.GetString()!;
        }

        /// <summary>Nothing dropped or refused by the bridge, no CSP violation, and nothing on the page's console.</summary>
        public Task AssertCleanAsync()
        {
            Assert.Empty(App.Problems);
            lock (_consoleErrors)
            {
                Assert.Empty(_consoleErrors);
            }

            return Task.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await App.DisposeAsync();
        }
    }
}
