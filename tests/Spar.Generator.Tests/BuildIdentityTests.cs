namespace Spar.Generator.Tests;

/// <summary>
/// The build's identity (decision "M1d's decide-first questions"): <c>Spar.BuildIdentity.g.cs</c> in every variant, one
/// UTF-8 block with the variant, the revision and the tree's state from the build's properties and the contracts'
/// fingerprint; the catalog hands it to the host by name, so an identity change never changes the contracts' code or
/// Hot Reload's shape. The block's format is read by <c>Spar.Runtime.BuildIdentity</c> (tests/Spar.Tool.Tests).
/// </summary>
public sealed class BuildIdentityTests
{
    private const string Program = """
        using System.Threading.Tasks;
        using Spar;

        var app = SparApplication.Create(args, _ => { });
        return app.Run();

        [SparContract]
        public sealed class NotesService
        {
            [SparCommand]
            public Task<string> TitleAsync(string id) => Task.FromResult(id);
        }

        """;

    private const string Revision = "b59a518974b858fbd2c08edc4697000ffab8f996";

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    [InlineData("Test")]
    public void Every_variant_carries_the_block_with_the_catalogs_fingerprint(string variant)
    {
        var run = GeneratorHarness.Generate(Program, variant, revision: Revision, dirty: "false");

        Assert.Empty(run.Errors);
        var fingerprint = System.Text.RegularExpressions.Regex.Match(run.Files["Spar.Contracts.g.cs"], "Catalog \\{ get; \\} = new\\(\\s*\"(?<value>[0-9a-f]{16})\"").Groups["value"].Value;
        Assert.Matches("^[0-9a-f]{16}$", fingerprint);
        Assert.Contains($"Block => \"[spar-build-identity/1 variant={variant} revision={Revision} dirty=false fingerprint={fingerprint}]\"u8;", run.Files["Spar.BuildIdentity.g.cs"], StringComparison.Ordinal);
        Assert.Contains("    BuildIdentity = SparBuildIdentity.Block.ToArray(),", run.Files["Spar.Contracts.g.cs"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "", "revision= dirty=")]
    [InlineData(Revision, "TRUE", $"revision={Revision} dirty=true")]
    [InlineData("abc def", "maybe", "revision= dirty=")]
    [InlineData("abc]\"; evil", "false", "revision= dirty=false")]
    public void Values_outside_the_format_are_left_empty(string revision, string dirty, string expected)
    {
        var run = GeneratorHarness.Generate(Program, "Production", revision: revision, dirty: dirty);

        Assert.Empty(run.Errors);
        Assert.Contains($"variant=Production {expected} fingerprint=", run.Files["Spar.BuildIdentity.g.cs"], StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_revision_or_tree_state_changes_neither_the_contracts_code_nor_the_shape()
    {
        var clean = GeneratorHarness.Generate(Program, revision: Revision, dirty: "false");
        var dirty = GeneratorHarness.Generate(Program, revision: "0000000000000000000000000000000000000000", dirty: "true");

        Assert.NotEqual(clean.Files["Spar.BuildIdentity.g.cs"], dirty.Files["Spar.BuildIdentity.g.cs"]);
        Assert.Equal(clean.Files["Spar.Contracts.g.cs"], dirty.Files["Spar.Contracts.g.cs"]);
        Assert.Equal(clean.Files["Spar.ContractShape.g.cs"], dirty.Files["Spar.ContractShape.g.cs"]);
    }
}
