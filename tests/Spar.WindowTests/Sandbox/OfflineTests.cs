using static Spar.Testing.SparAssertions;

namespace Spar.WindowTests;

/// <summary>
/// The page on a machine with no network (plan M1b step 9): the guest's configuration turns networking off, so the
/// page starts offline. It says so (<c>navigator.onLine</c> false) and its calls to the backend still work, since the
/// bridge never uses the network. A Window-surface test, in the guest's user session (WebView2 cannot open a window as
/// SYSTEM). Switching the network on and off inside a guest that has none was not possible (decision record of 8c).
/// </summary>
[Trait(SandboxLane.Trait, "UserSession")]
public sealed class OfflineTests
{
    [Fact(Explicit = true)]
    public async Task The_page_starts_offline_and_its_calls_still_work()
    {
        SandboxLane.SkipUnlessUserSession();
        await using var host = await HostPage.OpenAsync();

        Assert.False(await host.Page.EvaluateAsync<bool>("navigator.onLine"));
        await host.ClickAsync("Echo");
        await Expect(host.Status).ToHaveTextAsync("Echo: hello");
        SandboxLane.Note("window surface in the guest: navigator.onLine false; Echo answered");
        host.AssertClean();
    }
}
