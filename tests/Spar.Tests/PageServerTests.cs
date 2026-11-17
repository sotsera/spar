using System.Runtime.Versioning;
using Spar.Windows;

namespace Spar.Tests;

/// <summary>
/// The Windows host's view of addresses (plan section 3, "Assets and the bridge"): only the app's own origin is served
/// and may talk to the bridge, and a path is decoded once, without its query and fragment. Pure string work, so it runs
/// on every OS.
/// </summary>
[SupportedOSPlatform("windows10.0.17763")]
public sealed class PageServerTests
{
    [Theory]
    [InlineData("https://app.localhost/", true)]
    [InlineData("https://app.localhost/assets/index.js?v=1", true)]
    [InlineData("https://app.localhost", false)]
    [InlineData("https://app.localhost:8443/", false)]
    [InlineData("https://app.localhost.example/", false)]
    [InlineData("https://app.localhostx/", false)]
    [InlineData("http://app.localhost/", false)]
    [InlineData("https://example.com/https://app.localhost/", false)]
    [InlineData("about:blank", false)]
    public void Only_the_apps_own_origin_counts(string address, bool own)
    {
        Assert.Equal(own, PageServer.IsAppOrigin(address));
    }

    [Theory]
    [InlineData("https://app.localhost/", "/")]
    [InlineData("https://app.localhost/assets/index-1.js?x=1#top", "/assets/index-1.js")]
    [InlineData("https://app.localhost/a%20b/caf%C3%A9.css", "/a b/café.css")]
    [InlineData("https://app.localhost/bad%zz", "/bad%zz")]
    [InlineData("https://app.localhost/%2e%2e/secret", "/../secret")]
    public void A_path_is_decoded_without_its_query_and_fragment(string address, string path)
    {
        Assert.Equal(path, PageServer.PathOf(address));
    }
}
