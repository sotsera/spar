using Spar.Testing;
using static Spar.Testing.SparAssertions;

namespace Spar.WindowTests;

/// <summary>The app's real window opens, its page reaches the backend, and it closes cleanly.</summary>
[Trait("Surface", "Window")]
public sealed class SmokeTests
{
    [Fact]
    public async Task The_window_opens_calls_the_backend_and_closes()
    {
        await using var host = await HostPage.OpenAsync();

        await host.ClickAsync("Echo");

        await Expect(host.Status).ToHaveTextAsync("Echo: hello");
        Assert.Equal(host.Address("/"), host.Page.Url);
        host.AssertClean();
    }

    [Fact]
    public async Task The_page_is_the_apps_own_and_the_engine_is_out_of_its_reach()
    {
        await using var host = await HostPage.OpenAsync();

        Assert.Equal("Window host", await host.Page.TitleAsync());
        Assert.Equal(host.Origin, await host.Page.EvaluateAsync<string>("location.origin"));

        // The engine runs in a world of its own: the page's scripts can neither see it nor change what it uses.
        Assert.Equal("undefined", await host.Page.EvaluateAsync<string>("typeof globalThis.__sparAutomation"));
        host.AssertClean();
    }
}
