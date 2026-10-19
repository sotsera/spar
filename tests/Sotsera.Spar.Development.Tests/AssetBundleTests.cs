using System.IO.Compression;
using System.Text;
using AwesomeAssertions;
using Spar.NativeSample;
using Sotsera.Spar.Bridge;
using Sotsera.Spar.Windows;

namespace Sotsera.Spar.Development.Tests;

public sealed class AssetBundleTests
{
    [Fact]
    public void BundleOwnsItsBytesAndLeavesTheInputStreamOpen()
    {
        using var stream = Archive(("index.html", "<h1>Local</h1>"), ("spar-contract.txt", new string('a', 64)));
        var bundle = SparAssetBundle.Read(stream);
        bundle.ContractFingerprint.Should().Be(new string('a', 64));
        stream.CanRead.Should().BeTrue();
    }

    [Theory]
    [InlineData("../escape.js")]
    [InlineData("/absolute.js")]
    [InlineData("assets/../escape.js")]
    [InlineData("assets\\escape.js")]
    [InlineData("assets/%2e%2e.js")]
    [InlineData("assets/a.js?other")]
    [InlineData("assets//a.js")]
    [InlineData("assets/a.exe")]
    public void RejectsNoncanonicalOrUnsupportedResources(string path)
    {
        using var stream = Archive(("index.html", "html"), ("spar-contract.txt", new string('a', 64)), (path, "bad"));
        var act = () => SparAssetBundle.Read(stream);
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void RejectsDuplicateAssets()
    {
        using var stream = Archive(("index.html", "one"), ("index.html", "two"), ("spar-contract.txt", new string('a', 64)));
        var act = () => SparAssetBundle.Read(stream);
        act.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("wrong")]
    [InlineData("")]
    public void RejectsInvalidContractMetadata(string fingerprint)
    {
        using var stream = Archive(("index.html", "html"), ("spar-contract.txt", fingerprint));
        var act = () => SparAssetBundle.Read(stream);
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void RejectsOversizedDecompressedAssetBeforeAllocation()
    {
        using var stream = Archive(("index.html", new string('a', 16 * 1024 * 1024 + 1)),
            ("spar-contract.txt", new string('a', 64)));
        var act = () => SparAssetBundle.Read(stream);
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void MismatchedAssetsFailBeforeNativeCreation()
    {
        using var stream = Archive(("index.html", "html"), ("spar-contract.txt", new string('a', 64)));
        var options = new SparWindowOptions
        {
            Title = "Mismatch",
            UserDataFolder = "unused",
            Bridge = new SparBridge(new SampleBackendBridge(null!), []),
            Assets = SparAssetBundle.Read(stream)
        };
        var act = () => SparApplication.Run(options);
        act.Should().Throw<ArgumentException>().WithMessage("*matching bridge*");
    }

    private static MemoryStream Archive(params (string Name, string Content)[] files)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                using var entry = archive.CreateEntry(file.Name).Open();
                entry.Write(Encoding.UTF8.GetBytes(file.Content));
            }
        }

        stream.Position = 0;
        return stream;
    }
}
