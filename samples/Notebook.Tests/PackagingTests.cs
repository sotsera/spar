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
        // The resource as shipped: one byte for its encoding (1 Brotli, 0 stored), then the text or its Brotli stream.
        using var stream = App.GetManifestResourceStream("Spar.ThirdPartyNotices")!;
        var compressed = stream.ReadByte() == 1;
        using var text = compressed ? new BrotliStream(stream, CompressionMode.Decompress) : stream;
        var notices = new StreamReader(text, Encoding.UTF8).ReadToEnd();

        Assert.StartsWith("# Licenses", notices, StringComparison.Ordinal);
        Assert.Contains("## react - 19.3.1 (MIT)", notices, StringComparison.Ordinal);
        Assert.Contains("## @radix-ui/react-dropdown-menu - 2.2.1 (MIT)", notices, StringComparison.Ordinal);
    }
}
