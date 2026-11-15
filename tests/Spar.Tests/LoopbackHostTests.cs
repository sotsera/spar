using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Spar.Bridge;
using Spar.Loopback;

namespace Spar.Tests;

/// <summary>The loopback host over real HTTP: what it serves, whom it answers, and one document's channel.</summary>
public sealed class LoopbackHostTests : IAsyncLifetime
{
    private const string Hello = "{\"type\":\"hello\",\"protocol\":1,\"types\":[\"open\",\"result\",\"failure\",\"event\",\"closing\"],\"contracts\":\"0123456789abcdef\"}\t";

    private readonly DirectoryInfo _page = Directory.CreateTempSubdirectory("spar-loopback-page-");
    private readonly List<string> _log = [];
    private TestBackend _backend = null!;
    private LoopbackHost _host = null!;
    private HttpClient _http = null!;

    public async ValueTask InitializeAsync()
    {
        File.WriteAllText(Path.Combine(_page.FullName, "index.html"), "<!doctype html><title>Notebook</title>");
        File.WriteAllText(Path.Combine(_page.FullName, "notes.js.map"), "{}");
        File.WriteAllText(Path.Combine(_page.FullName, ".hidden.js"), "export {};");

        _backend = TestBackend.Create();
        _host = await LoopbackHost.StartAsync(
            new LoopbackOptions
            {
                Dispatcher = _backend.Dispatcher,
                Fingerprint = TestBackend.Fingerprint,
                Page = new LoopbackAssets().Mount("", _page.FullName),
                Log = message => { lock (_log) { _log.Add(message); } },
            },
            TestContext.Current.CancellationToken);
        _http = new HttpClient { BaseAddress = _host.Origin };
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _host.DisposeAsync();
        await _backend.DisposeAsync();
        _page.Delete(recursive: true);
    }

    [Fact]
    public async Task The_page_is_served_with_the_page_headers()
    {
        using var response = await _http.GetAsync("/", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html; charset=utf-8", response.Content.Headers.ContentType!.ToString());
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("no-store", Header(response, "Cache-Control"));
        Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
        Assert.Contains($"connect-src {_host.Origin.GetLeftPart(UriPartial.Authority)};", Header(response, "Content-Security-Policy"), StringComparison.Ordinal);
        Assert.Contains("require-trusted-types-for 'script'", Header(response, "Content-Security-Policy"), StringComparison.Ordinal);
        Assert.StartsWith("camera=()", Header(response, "Permissions-Policy"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/missing.js")]
    [InlineData("/notes.js.map")]
    [InlineData("/.hidden.js")]
    [InlineData("/index.html/")]
    public async Task Anything_else_is_404(string path)
    {
        using var response = await _http.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
    }

    [Theory]
    [InlineData("assets/index-B2x.js", true)]
    [InlineData("spar/sdk.js", true)]
    [InlineData("../secret.txt", false)]
    [InlineData("a/../b.js", false)]
    [InlineData("a//b.js", false)]
    [InlineData(".git/config", false)]
    [InlineData("a\\b.js", false)]
    [InlineData("C:/x.js", false)]
    [InlineData("a%2Fb.js", false)]
    public void Asset_paths_are_plain_segments(string path, bool safe)
    {
        Assert.Equal(safe, LoopbackAssets.IsSafePath(path));
    }

    [Fact]
    public async Task Another_host_name_gets_nothing()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = $"rebound.example:{_host.Origin.Port}";

        using var response = await _http.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("forbidden-host", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Localhost_with_the_port_is_this_host()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = $"localhost:{_host.Origin.Port}";

        using var response = await _http.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(null, null, HttpStatusCode.Unauthorized, "unauthorized")]
    [InlineData("wrong", null, HttpStatusCode.Unauthorized, "unauthorized")]
    [InlineData("right", "http://evil.example", HttpStatusCode.Forbidden, "forbidden-origin")]
    [InlineData("right", "null", HttpStatusCode.Forbidden, "forbidden-origin")]
    public async Task Bridge_requests_need_the_token_and_this_origin(string? token, string? origin, HttpStatusCode status, string code)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/_spar/channel") { Content = new StringContent(Hello) };
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token == "right" ? _host.Token : token);
        }

        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        using var response = await _http.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal((status, code), (response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Bridge_endpoints_take_only_posts()
    {
        using var response = await _http.GetAsync("/_spar/channel", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task A_channel_opens_with_hello_and_carries_the_replies_of_sent_calls()
    {
        await using var channel = await OpenChannelAsync();

        var open = Decode(await channel.NextAsync());
        await SendAsync(open.Doc!, "{\"type\":\"call\",\"id\":1,\"contract\":\"pages\",\"command\":\"get\"}\t{\"id\":\"ideas\"}\n"
            + "{\"type\":\"call\",\"id\":2,\"contract\":\"pages\",\"command\":\"get\"}\t{\"id\":\"missing\"}");

        var replies = new[] { Decode(await channel.NextAsync()), Decode(await channel.NextAsync()) }.OrderBy(r => r.Id).ToArray();
        Assert.Equal((BridgeMessageType.Open, TestBackend.Fingerprint), (open.Type, open.Contracts));
        Assert.Equal([(1L, true), (2L, false)], replies.Select(r => (r.Id!.Value, r.Ok!.Value)));
    }

    [Fact]
    public async Task A_channel_must_open_with_hello()
    {
        using var response = await PostAsync("/_spar/channel", "{\"type\":\"ready\"}\t", doc: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_closed_channel_closes_its_document()
    {
        string doc;
        await using (var channel = await OpenChannelAsync())
        {
            doc = Decode(await channel.NextAsync()).Doc!;
        }

        // The host notices the closed connection on its own schedule.
        var status = HttpStatusCode.NoContent;
        for (var attempt = 0; attempt < 50 && status == HttpStatusCode.NoContent; attempt++)
        {
            using var response = await PostAsync("/_spar/send", "{\"type\":\"ready\"}\t", doc);
            status = response.StatusCode;
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task A_send_names_a_document_the_host_opened()
    {
        using var response = await PostAsync("/_spar/send", "{\"type\":\"ready\"}\t", doc: "0000000000000000");

        Assert.Equal((HttpStatusCode.NotFound, "unknown-document"), (response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Ready_reaches_the_host()
    {
        var ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _host.DocumentReady += doc => ready.TrySetResult(doc);
        await using var channel = await OpenChannelAsync();
        var doc = Decode(await channel.NextAsync()).Doc!;

        using var response = await PostAsync("/_spar/send", "{\"type\":\"ready\"}\t", doc);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(doc, await ready.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Csp_reports_are_logged_without_a_token()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/_spar/csp-report") { Content = new StringContent("{\"csp-report\":{}}") };

        using var response = await _http.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        lock (_log)
        {
            Assert.Contains("csp violation: {\"csp-report\":{}}", _log);
        }
    }

    [Fact]
    public async Task Stopping_the_host_ends_open_channels()
    {
        var channel = await OpenChannelAsync();
        await channel.NextAsync();

        await _host.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Null(await channel.NextOrEndAsync());
        await channel.DisposeAsync();
        _host = await LoopbackHost.StartAsync(new LoopbackOptions { Dispatcher = _backend.Dispatcher, Fingerprint = "", Page = new LoopbackAssets() }, TestContext.Current.CancellationToken);
    }

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values)
            ? string.Join(",", values)
            : "";

    private static BridgeMessage Decode(string line)
    {
        Assert.True(BridgeCodec.TryDecode(line, out var message, out var error), error?.Reason);
        return message!;
    }

    private async Task<HttpResponseMessage> PostAsync(string path, string body, string? doc)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(body, Encoding.UTF8) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _host.Token);
        if (doc is not null)
        {
            request.Headers.Add("X-Spar-Doc", doc);
        }

        return await _http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task SendAsync(string doc, string body)
    {
        using var response = await PostAsync("/_spar/send", body, doc);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task<Channel> OpenChannelAsync()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/_spar/channel") { Content = new StringContent(Hello, Encoding.UTF8) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _host.Token);
        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        return new Channel(request, response, new StreamReader(stream, Encoding.UTF8));
    }

    /// <summary>One open channel response, read line by line.</summary>
    private sealed class Channel(HttpRequestMessage request, HttpResponseMessage response, StreamReader reader) : IAsyncDisposable
    {
        public async Task<string> NextAsync() =>
            await NextOrEndAsync() ?? throw new InvalidOperationException("The channel ended.");

        public async Task<string?> NextOrEndAsync()
        {
            try
            {
                return await reader.ReadLineAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception exception) when (exception is IOException or HttpRequestException)
            {
                return null;
            }
        }

        /// <summary>
        /// Aborts the connection, as a browser does when a page goes away. Only disposing the response would let
        /// HttpClient drain the endless body for up to 2 s, hoping to reuse the connection.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            using var abort = new CancellationTokenSource();
            var pending = reader.ReadLineAsync(abort.Token);
            abort.Cancel();
            try
            {
                await pending;
            }
            catch (Exception exception) when (exception is OperationCanceledException or IOException or HttpRequestException)
            {
                // Expected: the read was cancelled, or the channel had already ended.
            }

            reader.Dispose();
            response.Dispose();
            request.Dispose();
        }
    }
}
