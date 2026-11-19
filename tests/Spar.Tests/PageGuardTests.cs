using System.Runtime.Versioning;
using Spar.Windows;

namespace Spar.Tests;

/// <summary>The Windows host's navigation lock and permission gate, in what they decide without a WebView (plan section 3b).</summary>
[SupportedOSPlatform("windows10.0.17763")]
public sealed class PageGuardTests
{
    [Theory]
    [InlineData("https://example.com/docs?a=1#b", null)]
    [InlineData("http://example.com", null)]
    [InlineData("mailto:someone@example.com", null)]
    [InlineData("tel:+390123456", null)]
    [InlineData("https://user:secret@example.com/", "credentials")]
    [InlineData("https://example.com@evil.example/", "credentials")]
    [InlineData("https://app.localhost/settings", "own pages")]
    [InlineData("file:///C:/Windows/notepad.exe", "only http, https, mailto and tel")]
    [InlineData("ms-settings:privacy", "only http, https, mailto and tel")]
    [InlineData("javascript:alert(1)", "only http, https, mailto and tel")]
    [InlineData("https:example.com", "no host")]
    [InlineData("https://", "no host")]
    [InlineData("https://example.com/a b", "spaces")]
    [InlineData("mailto:", "empty")]
    public void Only_plain_external_addresses_open_outside_the_app(string address, string? refusal)
    {
        var reason = ExternalLinks.Refusal(address);

        if (refusal is null)
        {
            Assert.Null(reason);
        }
        else
        {
            Assert.Contains(refusal, reason, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void An_address_longer_than_the_limit_is_refused_not_cut()
    {
        Assert.NotNull(ExternalLinks.Refusal("https://example.com/" + new string('a', ExternalLinks.MaxLength)));
    }

    [Fact]
    public void Each_permission_maps_from_its_WebView2_kind_and_the_others_map_to_none()
    {
        var mapped = Enumerable.Range(0, 14).Select(PageGuard.PermissionOf).Where(p => p is not null).Select(p => p!.Value).ToList();

        Assert.Equal(Enum.GetValues<Permission>().Order(), mapped.Order());
        Assert.Null(PageGuard.PermissionOf(4));
        Assert.Null(PageGuard.PermissionOf(13));
    }
}
