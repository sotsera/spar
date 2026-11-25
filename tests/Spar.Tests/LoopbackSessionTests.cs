using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Spar.Loopback;

namespace Spar.Tests;

/// <summary>
/// Loopback in the development loop (M1d step 2): the choice by <c>SPAR_HOST</c>, the dev session's record and its
/// rule, the orchestrator's variables, the browser opener's command, and a dev session's host on a fixed address.
/// </summary>
public sealed class LoopbackSessionTests : IDisposable
{
    private const string Hello = "{\"type\":\"hello\",\"protocol\":1,\"types\":[\"open\"],\"contracts\":\"0123456789abcdef\"}\t";

    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    private readonly DirectoryInfo _obj = Directory.CreateTempSubdirectory("spar-loopback-obj-");

    private string File => Path.Combine(_obj.FullName, LoopbackSession.FileName);

    public void Dispose() => _obj.Delete(recursive: true);

    [Theory]
    [InlineData(null, "desktop")]
    [InlineData("", "desktop")]
    [InlineData("Desktop", "desktop")]
    [InlineData(" desktop ", "desktop")]
    [InlineData("Loopback", "loopback")]
    [InlineData("loopback", "loopback")]
    [InlineData("Browser", "refused")]
    public void SPAR_HOST_chooses_the_host(string? value, string expected)
    {
        var chosen = SparLoopbackHost.Choose(value);

        var kind = chosen is null ? "desktop" : chosen.Refusal is null ? "loopback" : "refused";
        Assert.Equal(expected, kind);
        if (kind == "refused")
        {
            Assert.Contains("use Desktop (the window, the default) or Loopback", chosen!.Refusal, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_first_run_is_a_new_session_on_a_free_port()
    {
        var address = Resolve();

        Assert.Equal((0, true, false), (address.Port, address.NewSession, address.Orchestrated));
        Assert.NotNull(LoopbackSession.TokenBytes(address.Token));
    }

    [Fact]
    public void A_run_within_the_gap_continues_the_session_with_its_port_and_token()
    {
        var token = LoopbackSession.NewToken();
        Assert.True(LoopbackSession.Write(File, 52311, token));
        LoopbackSession.Touch(File, Now - TimeSpan.FromHours(3));

        var address = Resolve();

        Assert.Equal((52311, token, false), (address.Port, address.Token, address.NewSession));
    }

    [Fact]
    public void A_run_after_the_gap_starts_a_new_session_on_the_same_port_with_a_new_token()
    {
        var token = LoopbackSession.NewToken();
        LoopbackSession.Write(File, 52311, token);
        LoopbackSession.Touch(File, Now - LoopbackSession.SessionGap - TimeSpan.FromMinutes(1));

        var address = Resolve();

        Assert.Equal((52311, true), (address.Port, address.NewSession));
        Assert.NotEqual(token, address.Token);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"version\":2,\"port\":52311,\"token\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"}")]
    [InlineData("{\"version\":1,\"port\":0,\"token\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"}")]
    [InlineData("{\"version\":1,\"port\":52311,\"token\":\"short\"}")]
    public void A_record_this_host_cannot_read_is_a_new_session(string text)
    {
        System.IO.File.WriteAllText(File, text);
        LoopbackSession.Touch(File, Now);

        var address = Resolve();

        Assert.Equal((0, true), (address.Port, address.NewSession));
    }

    [Fact]
    public void The_orchestrators_variables_win_and_the_record_is_not_read()
    {
        LoopbackSession.Write(File, 52311, LoopbackSession.NewToken());
        LoopbackSession.Touch(File, Now);
        var token = LoopbackSession.NewToken();

        var both = Resolve(new() { [LoopbackSession.PortVariable] = "41000", [LoopbackSession.TokenVariable] = token });
        var portOnly = Resolve(new() { [LoopbackSession.PortVariable] = "41000" });

        Assert.Equal((41000, token, false, true), (both.Port, both.Token, both.NewSession, both.Orchestrated));
        Assert.Equal((41000, false, true), (portOnly.Port, portOnly.NewSession, portOnly.Orchestrated));
        Assert.NotNull(LoopbackSession.TokenBytes(portOnly.Token));
    }

    [Theory]
    [InlineData("SPAR_LOOPBACK_PORT", "0", "not a port")]
    [InlineData("SPAR_LOOPBACK_PORT", "65536", "not a port")]
    [InlineData("SPAR_LOOPBACK_PORT", "-1", "not a port")]
    [InlineData("SPAR_LOOPBACK_TOKEN", "short", "not base64url")]
    [InlineData("SPAR_LOOPBACK_TOKEN", "has spaces and is long enough to be a token", "not base64url")]
    public void A_variable_that_cannot_be_used_stops_the_start(string name, string value, string message)
    {
        var address = LoopbackSession.Resolve(File, variable => variable == name ? value : null, Now, out var problem);

        Assert.Null(address);
        Assert.Contains(message, problem, StringComparison.Ordinal);
    }

    [Fact]
    public void The_record_is_readable_by_its_owner_only()
    {
        LoopbackSession.Write(File, 52311, LoopbackSession.NewToken());
        LoopbackSession.Write(File, 52312, LoopbackSession.NewToken());

        if (OperatingSystem.IsWindows())
        {
            AssertOwnerOnly(File);
        }
        else
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, System.IO.File.GetUnixFileMode(File));
        }

        Assert.Equal(52312, LoopbackSession.Read(File)!.Value.Port);
        Assert.Single(_obj.GetFiles());
    }

    [Theory]
    [InlineData(null, true, null)]
    [InlineData("none", false, null)]
    [InlineData("NONE", false, null)]
    public void The_opener_is_the_default_browser_or_none(string? setting, bool opens, string? program)
    {
        var info = BrowserOpener.For(new Uri("http://127.0.0.1:5000/#token=abc"), setting);

        Assert.Equal(opens, info is not null);
        Assert.Equal(program, info is { UseShellExecute: false } ? info.FileName : null);
        if (info is not null)
        {
            Assert.Equal(("http://127.0.0.1:5000/#token=abc", true), (info.FileName, info.UseShellExecute));
        }
    }

    [Fact]
    public void The_opener_runs_a_command_with_the_address_in_place_of_url_or_last()
    {
        var url = new Uri("http://127.0.0.1:5000/#token=abc");

        var placed = BrowserOpener.For(url, "\"C:\\Program Files\\Browser\\browser.exe\" --profile \"a b\" --open={url} --new");
        var appended = BrowserOpener.For(url, "recorder  --file out.txt");

        Assert.Equal(("C:\\Program Files\\Browser\\browser.exe", false), (placed!.FileName, placed.UseShellExecute));
        Assert.Equal(["--profile", "a b", "--open=http://127.0.0.1:5000/#token=abc", "--new"], placed.ArgumentList);
        Assert.Equal(["--file", "out.txt", "http://127.0.0.1:5000/#token=abc"], appended!.ArgumentList);
        Assert.Throws<ArgumentException>(() => BrowserOpener.Split("\"unclosed"));
    }

    [Fact]
    public async Task A_dev_sessions_host_listens_at_its_port_with_its_token_and_names_its_run()
    {
        await using var backend = TestBackend.Create();
        var port = FreePort();
        var token = LoopbackSession.NewToken();
        var host = await LoopbackHost.StartAsync(Options(backend, port, token), TestContext.Current.CancellationToken);
        using var http = new HttpClient { BaseAddress = host.Origin };

        try
        {
            Assert.Equal((port, token), (host.Origin.Port, host.Token));

            using var probe = await PostAsync(http, "/_spar/session", token);
            Assert.Equal((HttpStatusCode.OK, "run-1"), (probe.StatusCode, await probe.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)));

            using var wrong = await PostAsync(http, "/_spar/session", LoopbackSession.NewToken());
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

            using var channel = await PostAsync(http, "/_spar/channel", token, Hello, HttpCompletionOption.ResponseHeadersRead);
            Assert.Equal(HttpStatusCode.OK, channel.StatusCode);
            Assert.Equal("run-1", string.Join(",", channel.Headers.GetValues(LoopbackHost.RunHeader)));
        }
        finally
        {
            await host.DisposeAsync();
        }

        await Assert.ThrowsAnyAsync<HttpRequestException>(() => PostAsync(http, "/_spar/session", token));
    }

    [Fact]
    public async Task A_port_another_program_holds_is_an_address_in_use()
    {
        await using var backend = TestBackend.Create();
        using var holder = new TcpListener(IPAddress.Loopback, 0);
        holder.Start();
        var port = ((IPEndPoint)holder.LocalEndpoint).Port;

        var failure = await Assert.ThrowsAsync<IOException>(() => LoopbackHost.StartAsync(Options(backend, port, null), TestContext.Current.CancellationToken));

        Assert.IsType<Microsoft.AspNetCore.Connections.AddressInUseException>(failure.InnerException);
    }

    [Fact]
    public async Task The_test_hosts_loopback_offers_no_session_probe_and_names_no_run()
    {
        await using var backend = TestBackend.Create();
        var host = await LoopbackHost.StartAsync(Options(backend, 0, null, runId: null), TestContext.Current.CancellationToken);
        using var http = new HttpClient { BaseAddress = host.Origin };
        try
        {
            using var probe = await PostAsync(http, "/_spar/session", host.Token);
            using var channel = await PostAsync(http, "/_spar/channel", host.Token, Hello, HttpCompletionOption.ResponseHeadersRead);

            Assert.Equal(HttpStatusCode.NotFound, probe.StatusCode);
            Assert.False(channel.Headers.Contains(LoopbackHost.RunHeader));
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    private LoopbackAddress Resolve(Dictionary<string, string>? variables = null) =>
        LoopbackSession.Resolve(File, name => variables?.GetValueOrDefault(name), Now, out _)!;

    private static LoopbackOptions Options(TestBackend backend, int port, string? token, string? runId = "run-1") => new()
    {
        Dispatcher = backend.Dispatcher,
        Fingerprint = TestBackend.Fingerprint,
        Page = new LoopbackAssets(),
        Port = port,
        Token = token,
        RunId = runId,
    };

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient http, string path, string token, string body = "", HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(body, Encoding.UTF8) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await http.SendAsync(request, completion, TestContext.Current.CancellationToken);
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    [SupportedOSPlatform("windows")]
    private static void AssertOwnerOnly(string path)
    {
        var security = new FileInfo(path).GetAccessControl();
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();
        using var identity = WindowsIdentity.GetCurrent();

        Assert.True(security.AreAccessRulesProtected);
        Assert.Equal([identity.User!], rules.Select(rule => (SecurityIdentifier)rule.IdentityReference));
    }
}
