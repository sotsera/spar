using System.Text;
using Spar.Runtime;
using Spar.Tool.Identity;

namespace Spar.Tool.Tests;

/// <summary>
/// The build identity's block (<c>src/Spar/Runtime/BuildIdentity.cs</c>, linked into the tool), what the host's first
/// log line says from it, and the reader on files: anywhere in a file, across its chunks, and in the DLL beside an
/// apphost. Real built executables are read in Spar.BuildTests and Spar.PackageTests.
/// </summary>
public sealed class BuildIdentityTests
{
    private const string Revision = "b59a518974b858fbd2c08edc4697000ffab8f996";

    [Fact]
    public void A_block_reads_back_and_says_what_the_host_logs()
    {
        var identity = new BuildIdentity("Production", Revision, false, "59cb559befd9b76b");

        var parsed = BuildIdentity.Parse(Encoding.ASCII.GetBytes(identity.ToBlock()));

        Assert.Equal(identity, parsed);
        Assert.Equal($"[spar-build-identity/1 variant=Production revision={Revision} dirty=false fingerprint=59cb559befd9b76b]", identity.ToBlock());
        Assert.Equal($"build Production, revision {Revision} (no uncommitted changes), contract fingerprint 59cb559befd9b76b", identity.Describe());
    }

    [Fact]
    public void Unknown_values_are_empty_and_said_as_unknown()
    {
        var outside = BuildIdentity.Parse("[spar-build-identity/1 variant=Development revision= dirty= fingerprint=0123456789abcdef]"u8);
        var noGit = BuildIdentity.Parse(Encoding.ASCII.GetBytes($"[spar-build-identity/1 variant=Test revision={Revision} dirty= fingerprint=0123456789abcdef extra=later]"));

        Assert.Equal(new BuildIdentity("Development", null, null, "0123456789abcdef"), outside);
        Assert.Equal("build Development, no revision (not built in a git work tree), contract fingerprint 0123456789abcdef", outside!.Describe());
        Assert.Contains("(tree state unknown)", noGit!.Describe(), StringComparison.Ordinal);
        Assert.Equal("no build identity (built without Spar's generator)", BuildIdentity.Describe(ReadOnlySpan<byte>.Empty));
    }

    [Theory]
    [InlineData("[spar-build-identity/1 variant=Production revision=x dirty=maybe fingerprint=1]")]
    [InlineData("[spar-build-identity/1 variant=Production revision=x fingerprint=1]")]
    [InlineData("[spar-build-identity/1 variant=Produ\"ction revision=x dirty=true fingerprint=1]")]
    [InlineData("[spar-build-identity/2 variant=Production revision=x dirty=true fingerprint=1]")]
    [InlineData("[spar-build-identity/1 ")]
    public void Anything_else_is_not_a_block(string text)
    {
        Assert.Null(BuildIdentity.Parse(Encoding.ASCII.GetBytes(text)));
    }

    [Fact]
    public void The_reader_finds_the_block_after_a_false_marker_and_across_a_chunk_boundary()
    {
        var block = Encoding.ASCII.GetBytes(new BuildIdentity("Production", Revision, true, "59cb559befd9b76b").ToBlock());
        var falseMarker = Encoding.ASCII.GetBytes(BuildIdentity.Marker + "\0\0 not a block");
        var file = new byte[(1 << 20) + 4096];
        Random.Shared.NextBytes(file);
        falseMarker.CopyTo(file, 100);
        block.CopyTo(file, (1 << 20) - 40);

        var found = BuildIdentityReader.Find(new MemoryStream(file));

        Assert.Equal(new BuildIdentity("Production", Revision, true, "59cb559befd9b76b"), found);
        Assert.Null(BuildIdentityReader.Find(new MemoryStream(new byte[3 << 20])));
    }

    [Fact]
    public void An_apphost_without_a_block_is_read_through_the_DLL_beside_it()
    {
        using var folder = Repository.NewFolder("identity");
        var block = new BuildIdentity("Development", null, null, "0123456789abcdef").ToBlock();
        File.WriteAllBytes(Path.Combine(folder.Path, "Notes.exe"), new byte[4096]);
        File.WriteAllBytes(Path.Combine(folder.Path, "Notes.dll"), [.. new byte[77], .. Encoding.ASCII.GetBytes(block), .. new byte[9]]);

        var (identity, file, problem) = BuildIdentityReader.Read(Path.Combine(folder.Path, "Notes.exe"));
        var (none, _, missing) = BuildIdentityReader.Read(Path.Combine(folder.Path, "Other.exe"));

        Assert.Null(problem);
        Assert.Equal("Development", identity?.Variant);
        Assert.EndsWith("Notes.dll", file, StringComparison.Ordinal);
        Assert.Null(none);
        Assert.Contains("does not exist", missing, StringComparison.Ordinal);
    }
}
