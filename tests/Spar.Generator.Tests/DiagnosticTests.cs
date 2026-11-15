namespace Spar.Generator.Tests;

/// <summary>Each diagnostic once, at its location, with the text it underlines (plan section 2, "Diagnostics").</summary>
public sealed class DiagnosticTests
{
    private const string Program = """
        using System.Threading;
        using System.Threading.Tasks;
        using Spar;

        var app = SparApplication.Create(args, _ => { });
        return app.Run();

        public sealed record Item(string Name);

        """;

    [Theory]
    [InlineData("[SparContract] public class Items { }", "SPAR001: Items")]
    [InlineData("[SparContract] public sealed class Items<T> { }", "SPAR002: Items")]
    [InlineData("[SparContract] public sealed class Items { [SparCommand] public Item Get(Item request) => request; }", "SPAR010: Get")]
    [InlineData("[SparContract] public sealed class Items { [SparCommand] public Task<Item> GetAsync(Item first, Item second) => Task.FromResult(first); }", "SPAR011: second")]
    [InlineData("[SparContract] public sealed class Items { [SparCommand] public Task<Item> GetAsync(CancellationToken token, Item request) => Task.FromResult(request); }", "SPAR012: token")]
    [InlineData("[SparContract] public sealed class Items { [SparCommand] public Task<Item> GetAsync(ref Item request) => Task.FromResult(request); }", "SPAR013: request")]
    [InlineData("[SparContract] public sealed class Items { [SparCommand] public Task<T> GetAsync<T>(T request) => Task.FromResult(request); }", "SPAR014: GetAsync")]
    [InlineData("[SparContract] public sealed class Items { [SparCommand] public Task<System.Collections.Generic.Dictionary<string, int>> MapAsync() => null!; }", "SPAR020: MapAsync")]
    [InlineData("[SparContract] public sealed class Items { [SparCommand] public Task<Item> GetAsync(System.Uri request) => null!; }", "SPAR020: request")]
    [InlineData("public abstract record Shape(string Name); [SparContract] public sealed class Items { [SparCommand] public Task<Shape> GetAsync() => null!; }", "SPAR021: Shape")]
    [InlineData("public sealed class NotRecord; [SparContract] public sealed class Items { [SparCommand] public Task<Result<NotRecord>> GoAsync() => null!; }", "SPAR030: GoAsync")]
    [InlineData("public closed record Err { public record Open : Err; } [SparContract] public sealed class Items { [SparCommand] public Task<Result<Err>> GoAsync() => null!; }", "SPAR030: Open")]
    [InlineData("public closed record Err { public sealed record Bad(string Message) : Err; } [SparContract] public sealed class Items { [SparCommand] public Task<Result<Err>> GoAsync() => null!; }", "SPAR033: Bad")]
    [InlineData("public closed record Err; [SparContract] public sealed class Items { [SparCommand] public Task<Result<Err>> GoAsync() => null!; }", "SPAR035: GoAsync")]
    public async Task A_contract_problem_is_reported_once_at_its_location(string contract, string expected)
    {
        var diagnostics = await GeneratorHarness.AnalyzeAsync(Program + contract);

        Assert.Equal([expected], diagnostics);
    }

    [Fact]
    public async Task A_valid_contract_has_no_diagnostics()
    {
        var diagnostics = await GeneratorHarness.AnalyzeAsync(Program + Fixtures.Catalog);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task A_classic_Main_is_reported()
    {
        const string source = """
            using Spar;

            public static class Program
            {
                public static int Main(string[] args) => SparApplication.Create(args, _ => { }).Run();
            }
            """;

        Assert.Equal(["SPAR060: Main"], await GeneratorHarness.AnalyzeAsync(source));
    }

    [Fact]
    public async Task Top_level_statements_that_do_not_return_Run_are_reported()
    {
        const string source = """
            using Spar;

            SparApplication.Create(args, _ => { }).Run();
            """;

        var diagnostics = await GeneratorHarness.AnalyzeAsync(source);

        Assert.Equal("SPAR061", Assert.Single(diagnostics).Split(':')[0]);
    }
}
