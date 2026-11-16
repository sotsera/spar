using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;

namespace Spar.Sdk.Tasks.Tests;

/// <summary>
/// The Spar.Sdk 0.0.0-source package that lets this repository's projects write <c>&lt;Project Sdk="Spar.Sdk"&gt;</c>
/// (eng/source-sdk). NuGet keeps one extracted copy per version, so the committed package must hold the current
/// sources, and a change needs a new version in the nuspec, the package's file name and global.json alike.
/// </summary>
public sealed class SourceSdkTests
{
    private static string Folder => Path.Combine(Repository.Root, "eng", "source-sdk");

    [Fact]
    public void The_committed_package_holds_the_current_sources()
    {
        using var package = ZipFile.OpenRead(Package());
        foreach (var name in (string[])["Spar.Sdk.nuspec", "Sdk/Sdk.props", "Sdk/Sdk.targets"])
        {
            var entry = package.GetEntry(name);
            Assert.True(entry is not null, $"The package has no {name}. Run: dotnet run eng/source-sdk/pack.cs");

            using var reader = new StreamReader(entry.Open());
            var packed = reader.ReadToEnd();
            var current = File.ReadAllText(Path.Combine(Folder, name)).ReplaceLineEndings("\n");
            Assert.True(packed == current, $"The package's {name} is out of date. Give the package a new version and run: dotnet run eng/source-sdk/pack.cs");
        }
    }

    [Fact]
    public void The_nuspec_the_package_and_global_json_name_one_version()
    {
        var nuspec = XDocument.Load(Path.Combine(Folder, "Spar.Sdk.nuspec"));
        var version = nuspec.Descendants().Single(e => e.Name.LocalName == "version").Value;

        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repository.Root, "global.json")));
        var pinned = json.RootElement.GetProperty("msbuild-sdks").GetProperty("Spar.Sdk").GetString();

        Assert.Equal(version, pinned);
        Assert.Equal($"spar.sdk.{version}.nupkg", Path.GetFileName(Package()));
    }

    private static string Package() =>
        Assert.Single(Directory.GetFiles(Path.Combine(Folder, "feed"), "*.nupkg"));
}
