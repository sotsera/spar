using Microsoft.CodeAnalysis;

namespace Spar.Generator.Tests;

/// <summary>
/// Contracts from a referenced library, served only when the app lists the library in <c>SparContracts</c> (decision
/// "Contracts from referenced libraries: the app opts in"), and the clash matrix between the app and its libraries, each
/// with the library as a project reference and as a built assembly.
/// </summary>
public sealed class ReferencedContractTests
{
    private const string Program = """
        using System.Threading.Tasks;
        using Spar;

        var app = SparApplication.Create(args, _ => { });
        return app.Run();

        """;

    private const string Jobs = """
        using System.Threading.Tasks;
        using Spar;

        namespace Lib;

        public sealed record Job(string Name);

        [SparContract]
        public sealed class JobsService
        {
            [SparCommand]
            public Task<Job> GetAsync(string id) => Task.FromResult(new Job(id));
        }
        """;

    [Theory]
    [InlineData(LibraryKind.Project)]
    [InlineData(LibraryKind.Metadata)]
    public void A_listed_librarys_contract_is_served_and_compiles(LibraryKind kind)
    {
        var library = GeneratorHarness.Library("Lib", Jobs, kind);

        var run = GeneratorHarness.Generate(Program, libraries: [library], listed: "Lib");

        Assert.Empty(run.Errors);
        var contracts = run.Files["Spar.Contracts.g.cs"];
        Assert.Contains("\"jobs\"", contracts, StringComparison.Ordinal);
        Assert.Contains("AddScoped<global::Lib.JobsService>", contracts, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(LibraryKind.Project)]
    [InlineData(LibraryKind.Metadata)]
    public async Task A_library_that_is_not_listed_is_ignored(LibraryKind kind)
    {
        var library = GeneratorHarness.Library("Lib", Jobs, kind);

        var run = GeneratorHarness.Generate(Program, libraries: [library]);

        Assert.DoesNotContain("\"jobs\"", run.Files["Spar.Contracts.g.cs"], StringComparison.Ordinal);
        Assert.Empty(await GeneratorHarness.MessagesAsync(Program, [library]));
    }

    [Theory]
    [InlineData(LibraryKind.Project)]
    [InlineData(LibraryKind.Metadata)]
    public async Task A_local_contract_named_like_a_listed_one_is_SPAR005_at_the_local_one_only(LibraryKind kind)
    {
        var library = GeneratorHarness.Library("Lib", Jobs, kind);
        const string Local = "[SparContract] public sealed class Jobs { [SparCommand] public Task PingAsync() => Task.CompletedTask; }";

        var diagnostics = await GeneratorHarness.AnalyzeAsync(Program + Local, [library], "Lib");
        var messages = await GeneratorHarness.MessagesAsync(Program + Local, [library], "Lib");
        var run = GeneratorHarness.Generate(Program + Local, libraries: [library], listed: "Lib");

        Assert.Equal(["SPAR005: Jobs"], diagnostics);
        Assert.Equal(
            ["SPAR005: Contracts 'Jobs' and 'Lib.JobsService' both resolve to the name 'jobs' (contract names are compared ignoring case). Give one of them another name with [SparContract(\"…\")]"],
            messages);
        Assert.DoesNotContain("\"jobs\"", run.Files["Spar.Contracts.g.cs"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(LibraryKind.Project)]
    [InlineData(LibraryKind.Metadata)]
    public async Task An_explicit_name_fixes_the_clash(LibraryKind kind)
    {
        var library = GeneratorHarness.Library("Lib", Jobs, kind);
        const string Local = "[SparContract(\"localJobs\")] public sealed class Jobs { [SparCommand] public Task PingAsync() => Task.CompletedTask; }";

        Assert.Empty(await GeneratorHarness.MessagesAsync(Program + Local, [library], "Lib"));
        var contracts = GeneratorHarness.Generate(Program + Local, libraries: [library], listed: "Lib").Files["Spar.Contracts.g.cs"];
        Assert.Contains("\"jobs\"", contracts, StringComparison.Ordinal);
        Assert.Contains("\"localJobs\"", contracts, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(LibraryKind.Project)]
    [InlineData(LibraryKind.Metadata)]
    public async Task Two_listed_libraries_with_one_contract_name_are_one_SPAR005_with_no_location(LibraryKind kind)
    {
        var first = GeneratorHarness.Library("LibA", Jobs.Replace("namespace Lib;", "namespace LibA;", StringComparison.Ordinal), kind);
        var second = GeneratorHarness.Library("LibB", Jobs.Replace("namespace Lib;", "namespace LibB;", StringComparison.Ordinal), kind);

        var messages = await GeneratorHarness.MessagesAsync(Program, [first, second], "LibA;LibB");

        Assert.Equal(
            ["SPAR005: In referenced assembly 'LibA': Contracts 'LibA.JobsService' and 'LibB.JobsService' both resolve to the name 'jobs' (contract names are compared ignoring case). Give one of them another name with [SparContract(\"…\")]"],
            messages);
    }

    [Theory]
    [InlineData(LibraryKind.Project)]
    [InlineData(LibraryKind.Metadata)]
    public async Task A_local_type_named_like_a_library_type_in_one_contract_is_SPAR040_at_the_local_type(LibraryKind kind)
    {
        var library = GeneratorHarness.Library("Lib", Jobs, kind);
        const string Local = """
            public sealed record Job(int Id);
            [SparContract] public sealed class Queue { [SparCommand] public Task<Lib.Job> NextAsync(Job request) => null!; }
            """;

        Assert.Equal(["SPAR040: Job"], await GeneratorHarness.AnalyzeAsync(Program + Local, [library], "Lib"));

        var renamed = Local.Replace("public sealed record Job(int Id);", "[SparName(\"QueuedJob\")] public sealed record Job(int Id);", StringComparison.Ordinal);
        Assert.Empty(await GeneratorHarness.MessagesAsync(Program + renamed, [library], "Lib"));
    }

    [Theory]
    [InlineData(LibraryKind.Project)]
    [InlineData(LibraryKind.Metadata)]
    public async Task A_library_type_two_contracts_use_is_fine(LibraryKind kind)
    {
        var library = GeneratorHarness.Library("Lib", Jobs, kind);
        const string Local = "[SparContract] public sealed class Queue { [SparCommand] public Task<Lib.Job> NextAsync() => null!; }";

        Assert.Empty(await GeneratorHarness.MessagesAsync(Program + Local, [library], "Lib"));
        Assert.Empty(GeneratorHarness.Generate(Program + Local, libraries: [library], listed: "Lib").Errors);
    }

    [Theory]
    [InlineData(LibraryKind.Project)]
    [InlineData(LibraryKind.Metadata)]
    public async Task A_problem_inside_a_listed_library_names_the_library_and_has_no_location(LibraryKind kind)
    {
        var broken = Jobs.Replace("public Task<Job> GetAsync(string id) => Task.FromResult(new Job(id));", "public Job Get(string id) => new(id);", StringComparison.Ordinal);
        var library = GeneratorHarness.Library("Lib", broken, kind);

        var messages = await GeneratorHarness.MessagesAsync(Program, [library], "Lib");

        var message = Assert.Single(messages);
        Assert.StartsWith("SPAR010: In referenced assembly 'Lib': Command 'Get' returns 'Lib.Job'.", message, StringComparison.Ordinal);
        Assert.Empty(GeneratorHarness.Generate(Program, libraries: [library], listed: "Lib").Errors);
    }

    [Theory]
    [InlineData("Missing", "SPAR006: SparContracts lists 'Missing', which the project does not reference. Reference it, or remove it from SparContracts")]
    [InlineData("Plain", "SPAR006: SparContracts lists 'Plain', which declares no contract. Remove it from SparContracts")]
    public async Task A_listed_name_that_serves_nothing_is_SPAR006(string listed, string expected)
    {
        var plain = GeneratorHarness.Library("Plain", "namespace Plain; public sealed record Note(string Text);", LibraryKind.Metadata);

        Assert.Equal([expected], await GeneratorHarness.MessagesAsync(Program, [plain], listed));
    }

    [Fact]
    public async Task An_internal_contract_in_a_listed_library_is_SPAR006()
    {
        var library = GeneratorHarness.Library("Lib", Jobs.Replace("public sealed class JobsService", "internal sealed class JobsService", StringComparison.Ordinal), LibraryKind.Metadata);

        Assert.Equal(
            ["SPAR006: In referenced assembly 'Lib': contract 'JobsService' is not public, so the app cannot serve it. Make it public, or move it out of the library"],
            await GeneratorHarness.MessagesAsync(Program, [library], "Lib"));
    }

    [Fact]
    public async Task The_list_ignores_case_and_spaces()
    {
        var library = GeneratorHarness.Library("Lib", Jobs, LibraryKind.Metadata);

        Assert.Empty(await GeneratorHarness.MessagesAsync(Program, [library], " lib ; "));
        Assert.Contains("\"jobs\"", GeneratorHarness.Generate(Program, libraries: [library], listed: " lib ; ").Files["Spar.Contracts.g.cs"], StringComparison.Ordinal);
    }

}
