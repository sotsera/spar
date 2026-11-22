namespace Spar.Generator.Tests;

/// <summary>
/// The restart rule of the development loop (plan section 4): in a Development build the generator adds
/// <c>SparContractShape</c>, an enum whose one member is named from the generated contract code, so an edit that changes
/// that code renames the member, which Hot Reload cannot apply, and <c>dotnet watch</c> restarts the app. These tests
/// classify edits: every change to the wire, a wire name, a command's limit or the page security renames it; an edit
/// that leaves the generated code alone (a command's body, a helper, a comment) keeps it, so Hot Reload still applies it.
/// That a renamed member is a rude edit is checked by running <c>dotnet watch</c> (docs/evidence/m1d-hot-reload.md).
/// </summary>
public sealed class ContractShapeTests
{
    private const string Program = """
        using System.Threading;
        using System.Threading.Tasks;
        using Spar;

        var app = SparApplication.Create(args, _ => { });
        return app.Run();

        """;

    private const string Contract = """
        public sealed record Item(string Name, int Count);

        public closed record ItemError
        {
            public sealed record Missing(string Name) : ItemError;
        }

        [SparContract]
        public sealed class ItemsService
        {
            [SparCommand]
            public Task<Result<Item, ItemError>> FindAsync(Item request, CancellationToken cancellationToken)
            {
                return Task.FromResult<Result<Item, ItemError>>(request);
            }

            private static int Helper() => 1;
        }

        """;

    [Theory]
    [InlineData("Production")]
    [InlineData("Test")]
    [InlineData("")]
    public void Only_a_Development_build_has_the_shape(string variant)
    {
        var run = GeneratorHarness.Generate(Program + Contract, variant);

        Assert.Empty(run.Errors);
        Assert.DoesNotContain("Spar.ContractShape.g.cs", run.Files.Keys);
        Assert.Contains("Spar.Contracts.g.cs", run.Files.Keys);
    }

    [Fact]
    public void The_shape_is_one_enum_member_named_from_the_generated_contract_code()
    {
        var run = GeneratorHarness.Generate(Program + Contract);

        Assert.Empty(run.Errors);
        var shape = run.Files["Spar.ContractShape.g.cs"];
        var contracts = System.Text.Encoding.UTF8.GetBytes(run.Files["Spar.Contracts.g.cs"]);
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(contracts).AsSpan(0, 16));
        Assert.Contains("internal enum SparContractShape", shape, StringComparison.Ordinal);
        Assert.Contains($"Shape_{hash},", shape, StringComparison.Ordinal);
        Assert.Matches("^[0-9a-f]{32}$", hash);
    }

    [Theory]
    [InlineData("a command's body", "return Task.FromResult<Result<Item, ItemError>>(request);", "return Task.FromResult<Result<Item, ItemError>>(new ItemError.Missing(request.Name));")]
    [InlineData("a private helper", "private static int Helper() => 1;", "private static int Helper() => 2;")]
    [InlineData("a new private method", "private static int Helper() => 1;", "private static int Helper() => 1;\n\n    private static int Other() => 3;")]
    [InlineData("a comment", "public sealed class ItemsService", "/// <summary>Items.</summary>\npublic sealed class ItemsService")]
    public void An_edit_that_keeps_the_generated_code_keeps_the_shape(string edit, string before, string after)
    {
        Assert.Equal(Shape(Contract), Shape(Edited(Contract, before, after)));
        Assert.NotEmpty(edit);
    }

    [Theory]
    [InlineData("a DTO property added", "Item(string Name, int Count)", "Item(string Name, int Count, string? Note)")]
    [InlineData("a DTO property's type", "Item(string Name, int Count)", "Item(string Name, long Count)")]
    [InlineData("a DTO property made nullable", "Item(string Name, int Count)", "Item(string? Name, int Count)")]
    [InlineData("a DTO property renamed", "Item(string Name, int Count)", "Item(string Title, int Count)")]
    [InlineData("an error case added", "public sealed record Missing(string Name) : ItemError;", "public sealed record Missing(string Name) : ItemError;\n    public sealed record Locked : ItemError;")]
    [InlineData("an error case's payload", "Missing(string Name)", "Missing(string Name, int Tries)")]
    [InlineData("an error case's code", "public sealed record Missing(string Name)", "[SparName(\"gone\")]\n    public sealed record Missing(string Name)")]
    [InlineData("a command's name", "[SparCommand]\n    public Task<Result<Item, ItemError>> FindAsync", "[SparCommand(\"lookUp\")]\n    public Task<Result<Item, ItemError>> FindAsync")]
    [InlineData("a command's limit", "[SparCommand]\n    public Task<Result<Item, ItemError>> FindAsync", "[SparCommand(TimeoutSeconds = 5)]\n    public Task<Result<Item, ItemError>> FindAsync")]
    [InlineData("a command's request", "FindAsync(Item request, CancellationToken cancellationToken)\n    {\n        return Task.FromResult<Result<Item, ItemError>>(request);", "FindAsync(string request, CancellationToken cancellationToken)\n    {\n        return Task.FromResult<Result<Item, ItemError>>(new Item(request, 0));")]
    [InlineData("a command's cancellation", "FindAsync(Item request, CancellationToken cancellationToken)", "FindAsync(Item request)")]
    [InlineData("a command added", "private static int Helper() => 1;", "private static int Helper() => 1;\n\n    [SparCommand]\n    public Task<int> CountAsync() => Task.FromResult(1);")]
    [InlineData("the contract's name", "[SparContract]", "[SparContract(\"stock\")]")]
    public void A_contract_edit_changes_the_shape(string edit, string before, string after)
    {
        Assert.NotEqual(Shape(Contract), Shape(Edited(Contract, before, after)));
        Assert.NotEmpty(edit);
    }

    [Fact]
    public void A_page_security_edit_in_Program_cs_changes_the_shape()
    {
        const string Strict = "var app = SparApplication.Create(args, _ => { });";
        const string Framed = "var app = SparApplication.Create(args, spar => spar.ContentSecurity(csp => csp.ImgSrc(\"https://images.example.com\")));";

        var before = GeneratorHarness.Generate(Program + Contract);
        var after = GeneratorHarness.Generate(Program.Replace(Strict, Framed, StringComparison.Ordinal) + Contract);

        Assert.Empty(after.Errors);
        Assert.NotEqual(before.Files["Spar.ContractShape.g.cs"], after.Files["Spar.ContractShape.g.cs"]);
    }

    private static string Shape(string contract)
    {
        var run = GeneratorHarness.Generate(Program + contract);
        Assert.Empty(run.Errors);
        return run.Files["Spar.ContractShape.g.cs"];
    }

    private static string Edited(string source, string before, string after)
    {
        var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal);
        var replaced = normalized.Replace(before, after, StringComparison.Ordinal);
        Assert.NotEqual(normalized, replaced);
        return replaced;
    }
}
