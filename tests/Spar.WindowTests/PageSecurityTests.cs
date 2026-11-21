using Spar.Testing;
using static Spar.Testing.SparAssertions;

namespace Spar.WindowTests;

/// <summary>
/// Page security that only a real window shows, on both hosts (plan section 3c; on Linux, the in-process counterparts
/// of M1c step 4's host tests, which also keep running from outside on the Production executable in
/// <c>tests/Spar.Tests</c>): a script's navigation away from the app is refused and the page stays, every response of
/// the origin carries the policy (a missing file's 404 too), a permission the app did not declare is refused without a
/// prompt, and neither the bridge's relay nor its handler is in the page's world.
/// </summary>
[Trait("Surface", "Window")]
public sealed class PageSecurityTests
{
    [Fact]
    public async Task A_script_navigation_away_from_the_app_is_refused_and_the_page_stays()
    {
        await using var host = await HostPage.OpenAsync();
        var documents = host.Page.Navigations();

        await host.Page.EvaluateAsync("() => { location.href = 'https://example.com/'; return true; }");
        await host.Page.EvaluateAsync("() => { window.open('https://example.com/other'); return true; }");
        await Task.Delay(500, TestContext.Current.CancellationToken);

        Assert.Equal(host.Address("/"), host.Page.Url);
        Assert.Equal(documents, host.Page.Navigations());
        await host.ClickAsync("Echo");
        await Expect(host.Status).ToHaveTextAsync("Echo: hello");
        Assert.Empty(host.Page.PageErrors);
    }

    [Fact]
    public async Task Every_response_of_the_origin_carries_the_policy_a_missing_files_404_too()
    {
        await using var host = await HostPage.OpenAsync();

        var page = await host.Page.EvaluateAsync<string>(
            "fetch(location.href).then(r => r.status + ' ' + r.headers.get('content-security-policy'))");
        var missing = await host.Page.EvaluateAsync<string>(
            "fetch('/no-such-file.txt').then(r => r.status + ' ' + r.headers.get('content-security-policy'))");

        Assert.StartsWith("200 ", page, StringComparison.Ordinal);
        Assert.Contains("default-src 'none'", page, StringComparison.Ordinal);
        Assert.StartsWith("404 ", missing, StringComparison.Ordinal);
        Assert.Contains("default-src 'none'", missing, StringComparison.Ordinal);
        host.AssertClean();
    }

    [Fact]
    public async Task A_permission_the_app_did_not_declare_is_refused_without_a_prompt()
    {
        await using var host = await HostPage.OpenAsync();

        // Geolocation: never declared by WindowHost, so the page's request ends at once, refused, and nothing asks.
        var answer = await host.Page.EvaluateAsync<string>(
            "new Promise(done => { const t = setTimeout(() => done('no answer'), 3000); " +
            "navigator.geolocation.getCurrentPosition(() => { clearTimeout(t); done('granted'); }, e => { clearTimeout(t); done('refused ' + e.code); }); })");

        Assert.Equal("refused 1", answer);
    }

    [Fact]
    public async Task The_bridge_relay_and_its_handler_are_out_of_the_pages_world()
    {
        await using var host = await HostPage.OpenAsync();

        Assert.Equal("undefined", await host.Page.EvaluateAsync<string>("typeof globalThis.sparRelay"));
        Assert.Equal("undefined", await host.Page.EvaluateAsync<string>("typeof globalThis.webkit?.messageHandlers?.spar"));
        Assert.Equal("undefined", await host.Page.EvaluateAsync<string>("typeof globalThis.webkit?.messageHandlers?.sparAutomation"));
        await host.ClickAsync("Echo");
        await Expect(host.Status).ToHaveTextAsync("Echo: hello");
        host.AssertClean();
    }
}

/// <summary>Reads of the page driver's state for Spar's own window tests.</summary>
internal static class PageState
{
    /// <summary>How many documents the main frame has loaded since the window opened.</summary>
    public static int Navigations(this SparPage page) => page.Session.Navigations;
}
