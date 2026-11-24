using System.Net;
using System.Text;
using Spar.DevServer;
using Spar.Hosting;
using Spar.Runtime;

namespace Spar.Tests;

/// <summary>
/// A Debug build's page through the app's own origin (plan section 4; decision "M1d step 1's shape"): the development
/// server's address rules, the record the plugin writes, the settings the build gives, the Debug policy, and the proxy's
/// rules against a fake server, with the error page it shows when the server is not there.
/// </summary>
public sealed class DevServerTests
{
    private const string Origin = "https://app.localhost";

    [Theory]
    [InlineData("http://127.0.0.1:5173", "http://127.0.0.1:5173", "ws://127.0.0.1:5173")]
    [InlineData("http://127.0.0.1:5173/", "http://127.0.0.1:5173", "ws://127.0.0.1:5173")]
    [InlineData(" http://localhost:4000 ", "http://localhost:4000", "ws://localhost:4000")]
    [InlineData("http://[::1]:5173", "http://[::1]:5173", "ws://[::1]:5173")]
    [InlineData("http://127.4.5.6:80", "http://127.4.5.6:80", "ws://127.4.5.6:80")]
    public void A_loopback_http_origin_is_an_address(string text, string origin, string socket)
    {
        var address = DevServerAddress.Parse(text, out var problem);

        Assert.Null(problem);
        Assert.Equal(new DevServerAddress(origin, socket), address);
    }

    [Theory]
    [InlineData("https://127.0.0.1:5173", "not an http://")]
    [InlineData("ws://127.0.0.1:5173", "not an http://")]
    [InlineData("http://example.com:5173", "not on this machine")]
    [InlineData("http://192.168.1.10:5173", "not on this machine")]
    [InlineData("http://localhost.example.com:5173", "not on this machine")]
    [InlineData("http://127.0.0.1:5173/app/", "origin alone")]
    [InlineData("http://127.0.0.1:5173/?x=1", "origin alone")]
    [InlineData("http://user:secret@127.0.0.1:5173", "origin alone")]
    [InlineData("127.0.0.1:5173", "not an http://")]
    public void Anything_else_is_refused_with_why(string text, string reason)
    {
        Assert.Null(DevServerAddress.Parse(text, out var problem));
        Assert.Contains(reason, problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/src/main.tsx?t=1", "http://127.0.0.1:5173/src/main.tsx?t=1")]
    [InlineData("//other.example/steal", "http://127.0.0.1:5173//other.example/steal")]
    [InlineData("/@fs/E:/x.js", "http://127.0.0.1:5173/@fs/E:/x.js")]
    public void A_request_s_path_stays_on_the_server(string pathAndQuery, string expected)
    {
        var address = DevServerAddress.Parse("http://127.0.0.1:5173", out _)!;

        var target = address.Target(pathAndQuery);

        Assert.Equal("127.0.0.1", target.Host);
        Assert.Equal(5173, target.Port);
        Assert.Equal(expected, target.OriginalString);
    }

    [Fact]
    public void A_path_without_its_slash_is_never_fetched()
    {
        var address = DevServerAddress.Parse("http://127.0.0.1:5173", out _)!;

        Assert.Throws<ArgumentException>(() => address.Target("@other.example/x"));
    }

    [Fact]
    public void The_plugin_s_record_is_read()
    {
        var json = """{"version":1,"url":"http://127.0.0.1:61234","instance":"ab12","pid":4242,"started":1795282406595,"later":{"x":[1]}}"""u8;

        var state = DevServerState.Parse(json);

        Assert.Equal(new DevServerState("http://127.0.0.1:61234", "ab12", 4242, 1795282406595), state);
    }

    [Theory]
    [InlineData("""{"version":2,"url":"http://127.0.0.1:1","instance":"a","pid":1,"started":1}""")]
    [InlineData("""{"version":1,"instance":"a","pid":1,"started":1}""")]
    [InlineData("""{"version":1,"url":"http://127.0.0.1:1","instance":"","pid":1,"started":1}""")]
    [InlineData("""{"version":1,"url":"http://127.0.0.1:1","instance":"a","pid":0,"started":1}""")]
    [InlineData("""{"version":1,"url":"http://127.0.0.1:1","instance":"a","pid":1}""")]
    [InlineData("""[1]""")]
    [InlineData("""{"version":1,""")]
    [InlineData("")]
    public void A_record_this_host_does_not_read_is_none(string json)
    {
        Assert.Null(DevServerState.Parse(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void The_settings_come_from_the_build_and_the_environment_beats_the_property()
    {
        var data = new Dictionary<string, object?>
        {
            ["Spar.DevServer.Frontend"] = "/app/frontend/",
            ["Spar.DevServer.Intermediate"] = "/app/obj/",
            ["Spar.DevServer.Url"] = "http://127.0.0.1:5173",
        };

        var built = DevServerSettings.Read(name => data.GetValueOrDefault(name), environmentUrl: null);
        var environment = DevServerSettings.Read(name => data.GetValueOrDefault(name), "http://127.0.0.1:6000");
        data.Remove("Spar.DevServer.Url");
        var started = DevServerSettings.Read(name => data.GetValueOrDefault(name), "");

        Assert.Equal(("http://127.0.0.1:5173", "SparDevServerUrl"), (built!.Url, built.UrlSource));
        Assert.Equal(("http://127.0.0.1:6000", DevServerSettings.UrlVariable), (environment!.Url, environment.UrlSource));
        Assert.Equal((null, null), (started!.Url, started.UrlSource));
        Assert.Equal(Path.Combine("/app/obj/", "spar-dev-server.json"), started.StateFile);
        Assert.Equal(Path.Combine("/app/obj/", "spar-dev-server.log"), started.OutputFile);
        Assert.Null(DevServerSettings.Read(_ => null, "http://127.0.0.1:5173"));
    }

    [Fact]
    public void On_Windows_the_folders_get_an_upper_case_drive_letter()
    {
        var data = new Dictionary<string, object?>
        {
            ["Spar.DevServer.Frontend"] = @"c:\app\frontend\",
            ["Spar.DevServer.Intermediate"] = @"c:\app\obj\",
        };

        var settings = DevServerSettings.Read(name => data.GetValueOrDefault(name), environmentUrl: null)!;

        var drive = OperatingSystem.IsWindows() ? "C" : "c";
        Assert.Equal($@"{drive}:\app\frontend\", settings.FrontendDirectory);
        Assert.StartsWith($@"{drive}:\app\obj\", settings.StateFile, StringComparison.Ordinal);
        Assert.Equal("/app/frontend", DevServerSettings.UpperCaseDrive("/app/frontend"));
        Assert.Equal(@"\\server\share", DevServerSettings.UpperCaseDrive(@"\\server\share"));
    }

    [Fact]
    public void A_missing_state_file_is_none()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"spar-{Guid.NewGuid():N}", "spar-dev-server.json");

        Assert.Null(DevServerState.Read(missing));
    }

    [Fact]
    public void Npm_and_Node_are_found_in_path_order()
    {
        var separator = Path.PathSeparator;
        var existing = new HashSet<string> { Path.Combine("b", "npm"), Path.Combine("c", "npm") };

        var found = DevServerLauncher.FindOnPath("npm", $"a{separator}\"b\"{separator}{separator}c", existing.Contains);

        Assert.Equal(Path.Combine("b", "npm"), found);
        Assert.Null(DevServerLauncher.FindOnPath("node", $"a{separator}b", existing.Contains));
        Assert.Null(DevServerLauncher.FindOnPath("npm", null, existing.Contains));
    }

    [Fact]
    public void The_Debug_policy_adds_the_socket_and_the_document_nonce_once_each()
    {
        var policy = new PagePolicy(SparPageSecurity.Default, Origin, development: true, devServerSocket: "ws://127.0.0.1:5173");

        var document = policy.ContentSecurityPolicyWithNonce("abc");

        Assert.True(policy.DevServer);
        Assert.Contains("script-src 'self' 'nonce-abc';", document, StringComparison.Ordinal);
        Assert.Contains("style-src 'self' 'nonce-abc';", document, StringComparison.Ordinal);
        Assert.Contains("connect-src https://app.localhost ws://127.0.0.1:5173;", document, StringComparison.Ordinal);
        Assert.Contains("require-trusted-types-for 'script'", document, StringComparison.Ordinal);
        Assert.DoesNotContain("nonce", policy.ContentSecurityPolicy, StringComparison.Ordinal);
        Assert.Contains("connect-src https://app.localhost ws://127.0.0.1:5173;", policy.ContentSecurityPolicy, StringComparison.Ordinal);
        Assert.All(Directives(document).GroupBy(name => name), group => Assert.Single(group));
    }

    [Fact]
    public void With_inline_styles_allowed_the_Debug_nonce_goes_only_in_script_src()
    {
        var security = new SparPageSecurity { AllowInlineStyles = true, ConnectSrc = ["ws://127.0.0.1:5173"] };
        var policy = new PagePolicy(security, Origin, development: true, devServerSocket: "ws://127.0.0.1:5173");

        var document = policy.ContentSecurityPolicyWithNonce("abc");

        Assert.Contains("script-src 'self' 'nonce-abc';", document, StringComparison.Ordinal);
        Assert.Contains("style-src 'self' 'unsafe-inline';", document, StringComparison.Ordinal);
        Assert.Contains("connect-src https://app.localhost ws://127.0.0.1:5173;", document, StringComparison.Ordinal);
    }

    [Fact]
    public void A_policy_without_the_server_is_unchanged()
    {
        var plain = new PagePolicy(SparPageSecurity.Default, Origin, development: true);

        Assert.False(plain.DevServer);
        Assert.Equal(plain.ContentSecurityPolicy, plain.ContentSecurityPolicyWithNonce("abc"));
    }

    [Fact]
    public async Task A_document_gets_a_fresh_nonce_in_its_body_and_its_policy_and_only_the_forwarded_headers_reach_the_server()
    {
        var server = new FakeServer(_ => Html("<html><head><meta property=\"csp-nonce\" nonce=\"spar-dev-nonce\"><script type=\"module\" nonce=\"spar-dev-nonce\">x</script></head></html>"));
        using var session = External(server);
        var proxy = Proxy(session);

        var first = await proxy.AnswerAsync("/", "GET", Headers(("Accept", "text/html"), ("Sec-Fetch-Dest", "document"), ("Cookie", "a=b"), ("If-None-Match", "\"x\"")));
        var second = await proxy.AnswerAsync("/", "GET", Headers(("Sec-Fetch-Dest", "document")));

        var body = Encoding.UTF8.GetString(first.Body.Span);
        var nonce = body.Split("nonce=\"")[1].Split('"')[0];
        Assert.Equal(200, first.Status);
        Assert.DoesNotContain(DevServerProxy.NoncePlaceholder, body, StringComparison.Ordinal);
        Assert.Contains($"script-src 'self' 'nonce-{nonce}';", first.Csp, StringComparison.Ordinal);
        Assert.Contains($"style-src 'self' 'nonce-{nonce}';", first.Csp, StringComparison.Ordinal);
        Assert.Contains("connect-src https://app.localhost ws://127.0.0.1:5173;", first.Csp, StringComparison.Ordinal);
        Assert.DoesNotContain(nonce, second.Csp, StringComparison.Ordinal);

        var request = server.Requests[0];
        Assert.Equal("http://127.0.0.1:5173/", request.Uri);
        Assert.Equal(["Accept: text/html", "Sec-Fetch-Dest: document"], request.Headers);
    }

    [Fact]
    public async Task Every_other_response_carries_the_policy_without_a_nonce_and_keeps_its_type_and_status()
    {
        var server = new FakeServer(request => request.RequestUri!.AbsolutePath == "/missing.js"
            ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new ByteArrayContent([]) }
            : Typed("self.onmessage = () => {};", "text/javascript"));
        using var session = External(server);
        var proxy = Proxy(session);

        var worker = await proxy.AnswerAsync("/src/worker.ts?worker_file&type=module", "GET", Headers(("Sec-Fetch-Dest", "worker")));
        var missing = await proxy.AnswerAsync("/missing.js", "GET", []);

        Assert.Equal((200, "text/javascript; charset=utf-8"), (worker.Status, worker.ContentType));
        Assert.Contains("worker-src 'self'", worker.Csp, StringComparison.Ordinal);
        Assert.DoesNotContain("nonce", worker.Csp, StringComparison.Ordinal);
        Assert.Equal(404, missing.Status);
        Assert.NotNull(missing.Csp);
    }

    [Fact]
    public async Task Redirects_encoded_bodies_and_other_methods_are_refused()
    {
        var server = new FakeServer(request => request.RequestUri!.AbsolutePath switch
        {
            "/moved" => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("http://other.example/") } },
            _ => Encoded(),
        });
        using var session = External(server);
        var proxy = Proxy(session);

        var moved = await proxy.AnswerAsync("/moved", "GET", []);
        var encoded = await proxy.AnswerAsync("/encoded.js", "GET", []);
        var posted = await proxy.AnswerAsync("/api", "POST", []);

        Assert.Equal((502, "redirect-refused"), (moved.Status, Encoding.UTF8.GetString(moved.Body.Span)));
        Assert.Equal((502, "encoded-response"), (encoded.Status, Encoding.UTF8.GetString(encoded.Body.Span)));
        Assert.Equal((405, "GET, HEAD"), (posted.Status, posted.Allow));
        Assert.Equal(2, server.Requests.Count);
    }

    [Fact]
    public async Task The_host_s_own_paths_never_reach_the_server()
    {
        var server = new FakeServer(_ => Typed("x", "text/plain"));
        using var session = External(server);
        var proxy = Proxy(session);

        var report = await proxy.AnswerAsync("/_spar/csp-report", "GET", []);
        var other = await proxy.AnswerAsync("/_spar/send?x=1", "GET", []);
        var script = await proxy.AnswerAsync("/_spar/dev-server/page.js", "GET", []);
        var style = await proxy.AnswerAsync("/_spar/dev-server/page.css", "GET", []);
        var retry = await proxy.AnswerAsync("/_spar/dev-server/retry", "POST", []);

        Assert.Equal(404, report.Status);
        Assert.Equal(404, other.Status);
        Assert.Equal((200, "text/javascript; charset=utf-8"), (script.Status, script.ContentType));
        Assert.Equal((200, "text/css; charset=utf-8"), (style.Status, style.ContentType));
        Assert.Equal(204, retry.Status);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task A_page_that_did_not_come_through_the_plugin_is_the_error_page()
    {
        var server = new FakeServer(_ => Html("<html><head><script type=\"module\" src=\"/@vite/client\"></script></head></html>"));
        using var session = External(server);
        var proxy = Proxy(session);

        var page = await proxy.AnswerAsync("/", "GET", Headers(("Sec-Fetch-Dest", "document")));

        Assert.Equal(503, page.Status);
        Assert.Contains("spar() from &quot;@spar/app/vite&quot;", Encoding.UTF8.GetString(page.Body.Span), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_server_that_does_not_answer_gives_documents_the_error_page_and_the_rest_502_until_retry()
    {
        var down = true;
        var server = new FakeServer(_ => down
            ? throw new HttpRequestException("No connection could be made")
            : Html("<html><head><meta nonce=\"spar-dev-nonce\"></head></html>"));
        using var session = External(server);
        var proxy = Proxy(session);

        var page = await proxy.AnswerAsync("/", "GET", Headers(("Sec-Fetch-Dest", "document")));
        var module = await proxy.AnswerAsync("/src/main.tsx", "GET", Headers(("Sec-Fetch-Dest", "script")));
        var failed = await session.Current;
        down = false;
        var retry = await proxy.AnswerAsync("/_spar/dev-server/retry", "POST", []);
        var again = await proxy.AnswerAsync("/", "GET", Headers(("Sec-Fetch-Dest", "document")));

        var html = Encoding.UTF8.GetString(page.Body.Span);
        Assert.Equal((503, "text/html; charset=utf-8"), (page.Status, page.ContentType));
        Assert.Contains("Nothing answers at http://127.0.0.1:5173", html, StringComparison.Ordinal);
        Assert.Contains("<button id=\"retry\"", html, StringComparison.Ordinal);
        Assert.Contains("src=\"/_spar/dev-server/page.js\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.Equal(502, module.Status);
        Assert.False(failed.IsReady);
        Assert.Equal(204, retry.Status);
        Assert.Equal(200, again.Status);
    }

    [Fact]
    public void The_error_page_escapes_the_server_s_output()
    {
        var settings = new DevServerSettings("/app/frontend/", "/app/obj/s.json", "/app/obj/s.log", null, null);
        var outcome = new DevServerOutcome(null, "It <broke> & stopped.", External: false, ["<script>alert(1)</script>", "\"quoted\" 'single'"]);

        var html = DevErrorPage.Render(outcome, settings);

        Assert.Contains("It &lt;broke&gt; &amp; stopped.", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("&quot;quoted&quot; &#39;single&#39;", html, StringComparison.Ordinal);
        Assert.Contains("npm run dev", html, StringComparison.Ordinal);
        Assert.Equal(1, html.Split("<script").Length - 1);
    }

    [Fact]
    public void The_plugin_and_the_host_agree_on_the_placeholder_the_identity_path_and_the_variables()
    {
        var plugin = File.ReadAllText(BridgeVectorTests.RepositoryFile("src/Spar.Client/src/vite/devServer.ts"));

        Assert.Contains($"export const devNoncePlaceholder = \"{DevServerProxy.NoncePlaceholder}\";", plugin, StringComparison.Ordinal);
        Assert.Contains($"export const identityPath = \"{DevServerSession.IdentityPath}\";", plugin, StringComparison.Ordinal);
        foreach (var variable in (string[])[DevServerLauncher.StateFileVariable, DevServerLauncher.PortVariable, DevServerLauncher.InstanceVariable, DevServerLauncher.IdleVariable])
        {
            Assert.Contains($"environment[\"{variable}\"]", plugin, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_document_is_a_window_or_frame_navigation()
    {
        Assert.True(DevServerProxy.IsDocument(Headers(("Sec-Fetch-Dest", "document"))));
        Assert.True(DevServerProxy.IsDocument(Headers(("Sec-Fetch-Dest", "iframe"))));
        Assert.False(DevServerProxy.IsDocument(Headers(("Sec-Fetch-Dest", "script"), ("Accept", "text/html"))));
        Assert.True(DevServerProxy.IsDocument(Headers(("Accept", "text/html,application/xhtml+xml"))));
        Assert.False(DevServerProxy.IsDocument([]));
    }

    private static DevServerSession External(FakeServer server) =>
        DevServerSession.Start(
            new DevServerSettings("/app/frontend/", "/app/obj/spar-dev-server.json", "/app/obj/spar-dev-server.log", "http://127.0.0.1:5173", DevServerSettings.UrlVariable),
            new HostLog(standardError: false),
            server);

    private static DevServerProxy Proxy(DevServerSession session) =>
        new(session, SparPageSecurity.Default, Origin, new HostLog(standardError: false));

    private static List<KeyValuePair<string, string>> Headers(params (string Name, string Value)[] headers) =>
        [.. headers.Select(header => new KeyValuePair<string, string>(header.Name, header.Value))];

    private static HttpResponseMessage Html(string html) => Typed(html, "text/html");

    private static HttpResponseMessage Typed(string text, string type) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(text, Encoding.UTF8, type),
        Headers = { ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"v1\"") },
    };

    private static HttpResponseMessage Encoded()
    {
        var response = Typed("x", "text/javascript");
        response.Content.Headers.ContentEncoding.Add("gzip");
        return response;
    }

    private static IEnumerable<string> Directives(string policy) =>
        policy.Split("; ").Select(directive => directive.Split(' ')[0]);

    /// <summary>A development server in memory: it answers with <c>answer</c> and keeps every request it got.</summary>
    private sealed class FakeServer(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<(string Uri, string[] Headers)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = request.Headers.Select(header => $"{header.Key}: {string.Join(",", header.Value)}").ToArray();
            lock (Requests)
            {
                Requests.Add((request.RequestUri!.OriginalString, headers));
            }

            return Task.FromResult(answer(request));
        }
    }
}
