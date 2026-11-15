namespace Spar.Generator.Tests;

/// <summary>What the generator writes, and that it compiles, for every supported shape and both variant kinds.</summary>
public sealed class GeneratorTests
{
    private const string Program = """
        using System.Threading;
        using System.Threading.Tasks;
        using Spar;

        var app = SparApplication.Create(args, _ => { });
        return app.Run();

        public sealed record Item(string Name);

        """;

    [Fact]
    public void Every_supported_shape_generates_code_that_compiles()
    {
        var run = GeneratorHarness.Generate(Program + Fixtures.Catalog);

        Assert.Empty(run.Errors);
        Assert.Equal(["Spar.Contracts.g.cs", "Spar.Create.g.cs"], run.Files.Keys.Order());
    }

    [Fact]
    public void Every_command_is_in_the_catalog_with_its_wire_name_and_time_limit()
    {
        var contracts = GeneratorHarness.Generate(Program + Fixtures.Catalog).Files["Spar.Contracts.g.cs"];

        string[] expected =
        [
            "new(\"catalog\", \"find\", 0, HandleCatalogFind),",
            "new(\"catalog\", \"remove\", 30, HandleCatalogRemove),",
            "new(\"catalog\", \"list\", 0, HandleCatalogList),",
            "new(\"catalog\", \"count\", 0, HandleCatalogCount),",
            "new(\"catalog\", \"clear\", 0, HandleCatalogClear),",
            "new(\"catalog\", \"rename\", 0, HandleCatalogRename),",
            "new(\"ioStats\", \"read\", 0, HandleIoStatsRead),",
        ];
        Assert.All(expected, entry => Assert.Contains(entry, contracts, StringComparison.Ordinal));
        Assert.DoesNotContain("notACommand", contracts, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_class_contracts_are_registered_by_the_generated_code()
    {
        var contracts = GeneratorHarness.Generate(Program + Fixtures.Catalog).Files["Spar.Contracts.g.cs"];

        Assert.Contains("AddScoped<global::CatalogService>(services);", contracts, StringComparison.Ordinal);
        Assert.DoesNotContain("AddScoped<global::IIOStatsService>", contracts, StringComparison.Ordinal);
    }

    [Fact]
    public void Error_codes_are_camel_case_or_their_SparName()
    {
        var contracts = GeneratorHarness.Generate(Program + Fixtures.Catalog).Files["Spar.Contracts.g.cs"];

        Assert.Contains("writer.WriteString(\"code\"u8, \"missing\"u8);", contracts, StringComparison.Ordinal);
        Assert.Contains("writer.WriteString(\"code\"u8, \"locked\"u8);", contracts, StringComparison.Ordinal);
        Assert.Contains("writer.WriteString(\"code\"u8, \"tooBig\"u8);", contracts, StringComparison.Ordinal);
    }

    [Fact]
    public void The_test_entry_point_and_clients_exist_only_in_the_Test_variant()
    {
        var development = GeneratorHarness.Generate(Program + Fixtures.Catalog, "Development");
        var production = GeneratorHarness.Generate(Program + Fixtures.Catalog, "Production");
        var test = GeneratorHarness.Generate(Program + Fixtures.Catalog, "Test");

        Assert.DoesNotContain("Spar.TestEntryPoint.g.cs", development.Files.Keys);
        Assert.DoesNotContain("Spar.TestEntryPoint.g.cs", production.Files.Keys);
        Assert.Empty(test.Errors);
        Assert.Contains("public partial class Program : global::Spar.ISparEntryPoint", test.Files["Spar.TestEntryPoint.g.cs"], StringComparison.Ordinal);
        Assert.Contains("public sealed class CatalogClient(", test.Files["Spar.TestClients.g.cs"], StringComparison.Ordinal);
        Assert.Contains("public sealed class IoStatsClient(", test.Files["Spar.TestClients.g.cs"], StringComparison.Ordinal);
    }

    [Fact]
    public void An_invalid_command_is_left_out_and_its_contract_still_works()
    {
        const string contract = """
            [SparContract]
            public sealed class Mixed
            {
                [SparCommand]
                public Task<Item> GoodAsync(Item request) => Task.FromResult(request);

                [SparCommand]
                public Item Bad(Item request) => request;
            }
            """;

        var contracts = GeneratorHarness.Generate(Program + contract).Files["Spar.Contracts.g.cs"];

        Assert.Contains("new(\"mixed\", \"good\", 0, HandleMixedGood),", contracts, StringComparison.Ordinal);
        Assert.DoesNotContain("\"bad\"", contracts, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_files_use_only_LF_line_endings()
    {
        var run = GeneratorHarness.Generate(Program + Fixtures.Catalog, "Test");

        Assert.All(run.Files.Values, text => Assert.DoesNotContain('\r', text));
    }

    [Fact]
    public void The_fingerprint_changes_with_the_wire_shape_and_only_with_it()
    {
        var original = Fingerprint(Program + Fixtures.Catalog);
        var reformatted = Fingerprint(Program + Fixtures.Catalog.Replace("public Task ClearAsync() => Task.CompletedTask;", "public Task ClearAsync()\n{\n    return Task.CompletedTask;\n}", StringComparison.Ordinal));
        var changed = Fingerprint(Program + Fixtures.Catalog.Replace("int Count,", "long Count,", StringComparison.Ordinal));

        Assert.Equal(original, reformatted);
        Assert.NotEqual(original, changed);
    }

    private static string Fingerprint(string source)
    {
        var contracts = GeneratorHarness.Generate(source).Files["Spar.Contracts.g.cs"];
        var start = contracts.IndexOf("Catalog { get; } = new(\n            \"", StringComparison.Ordinal);
        Assert.True(start >= 0, "no catalog");
        return contracts.Substring(start + "Catalog { get; } = new(\n            \"".Length, 16);
    }
}
