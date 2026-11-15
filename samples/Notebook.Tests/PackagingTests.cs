using System.IO.Compression;
using System.Text;
using Notebook.Pages;

namespace Notebook.Tests;

/// <summary>What the Notebook's executable carries besides its code: its page, and the licences of what the page bundles.</summary>
public sealed class PackagingTests
{
    private static readonly System.Reflection.Assembly App = typeof(Page).Assembly;

    [Fact]
    public void The_executable_carries_its_page_and_the_third_party_notices()
    {
        var resources = App.GetManifestResourceNames();

        Assert.Contains("Spar.Frontend", resources);
        Assert.Contains("Spar.ThirdPartyNotices", resources);
    }

    [Fact]
    public void The_notices_name_what_the_page_bundles()
    {
        using var stream = App.GetManifestResourceStream("Spar.ThirdPartyNotices")!;
        using var decoded = new BrotliStream(stream, CompressionMode.Decompress);
        var notices = new StreamReader(decoded, Encoding.UTF8).ReadToEnd();

        Assert.StartsWith("# Licenses", notices, StringComparison.Ordinal);
        Assert.Contains("## react - 19.3.1 (MIT)", notices, StringComparison.Ordinal);
        Assert.Contains("## @radix-ui/react-dropdown-menu - 2.2.1 (MIT)", notices, StringComparison.Ordinal);
    }
}
