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
    [InlineData("public closed record Err { public record Open : Err; } [SparContract] public sealed class Items { [SparCommand] public Task<Result<Err>> GoAsync() => null!; }", "SPAR032: Open")]
    [InlineData("public closed record Err { public sealed record Bad(string Message) : Err; } [SparContract] public sealed class Items { [SparCommand] public Task<Result<Err>> GoAsync() => null!; }", "SPAR033: Bad")]
    [InlineData("public closed record Err; [SparContract] public sealed class Items { [SparCommand] public Task<Result<Err>> GoAsync() => null!; }", "SPAR035: GoAsync")]
    [InlineData("[SparContract(\"Copy-Jobs\")] public sealed class Items { }", "SPAR003: SparContract(\"Copy-Jobs\")")]
    [InlineData("[SparContract] public sealed class Copy_JobsService { }", "SPAR003: Copy_JobsService")]
    [InlineData("[SparContract] public sealed class Items { [SparCommand(\"Get\")] public Task GetAsync() => null!; }", "SPAR003: SparCommand(\"Get\")")]
    [InlineData("[SparName(\"Bad-Name\")] public sealed record Note(string Text); [SparContract] public sealed class Items { [SparCommand] public Task<Note> GetAsync() => null!; }", "SPAR003: SparName(\"Bad-Name\")")]
    [InlineData("public closed record Err { [SparName(\"Not Found\")] public sealed record Missing : Err; } [SparContract] public sealed class Items { [SparCommand] public Task<Result<Err>> GoAsync() => null!; }", "SPAR003: SparName(\"Not Found\")")]
    [InlineData("[SparContract(\"sdk\")] public sealed class Items { }", "SPAR004: SparContract(\"sdk\")")]
    [InlineData("[SparContract(\"Testing\")] public sealed class Items { }", "SPAR003: SparContract(\"Testing\")")]
    [InlineData("[SparContract] public sealed class TestingService { }", "SPAR004: TestingService")]
    [InlineData("[SparContract] public sealed class DeleteService { }", "SPAR004: DeleteService")]
    [InlineData("[SparContract(\"wire\")] public sealed class Items { }", "SPAR004: SparContract(\"wire\")")]
    [InlineData("[SparContract(\"readItem\")] public sealed class Items { [SparCommand] public Task<Item> GetAsync() => null!; }", "SPAR004: SparContract(\"readItem\")")]
    [InlineData("[SparContract(\"decodeGet\")] public sealed class Items { [SparCommand] public Task GetAsync() => null!; }", "SPAR004: SparContract(\"decodeGet\")")]
    [InlineData("public sealed record any(string Name); [SparContract] public sealed class Items { [SparCommand] public Task<any> GetAsync() => null!; }", "SPAR004: any")]
    [InlineData("public sealed record @string(string Name); [SparContract] public sealed class Items { [SparCommand] public Task<@string> GetAsync() => null!; }", "SPAR004: @string")]
    [InlineData("public sealed record Call(string Name); [SparContract] public sealed class Items { [SparCommand] public Task<Call> GetAsync() => null!; }", "SPAR004: Call")]
    [InlineData("[SparContract] public sealed class Items { [SparCommand] public Task<Item> GetAsync(string[] request) => null!; }", "SPAR015: request")]
    [InlineData("public sealed record Draft(string Title) { public string Body { get; init; } = \"\"; } [SparContract] public sealed class Items { [SparCommand] public Task<Draft> GetAsync() => null!; }", "SPAR021: Body")]
    [InlineData("public sealed record Draft { public Draft(string title) { } public string Title { get; } = \"\"; } [SparContract] public sealed class Items { [SparCommand] public Task<Draft> GetAsync() => null!; }", "SPAR022: title")]
    [InlineData("public sealed record Link(string URL, string Url); [SparContract] public sealed class Items { [SparCommand] public Task<Link> GetAsync() => null!; }", "SPAR023: Url")]
    [InlineData("public closed record Err { public sealed record Inside : Err; } public sealed record Outside : Err; [SparContract] public sealed class Items { [SparCommand] public Task<Result<Err>> GoAsync() => null!; }", "SPAR031: Outside")]
    [InlineData("public closed record Err { public sealed record NotFound : Err; [SparName(\"notFound\")] public sealed record Missing : Err; } [SparContract] public sealed class Items { [SparCommand] public Task<Result<Err>> GoAsync() => null!; }", "SPAR034: Missing")]
    [InlineData("[System.Flags] public enum Rights { Read = 1, Write = 2 } [SparContract] public sealed class Items { [SparCommand] public Task<Rights> GetAsync() => null!; }", "SPAR025: Rights")]
    [InlineData("public enum Twice { One = 1, Uno = 1 } [SparContract] public sealed class Items { [SparCommand] public Task<Twice> GetAsync() => null!; }", "SPAR025: Twice")]
    [InlineData("public enum Nothing { } [SparContract] public sealed class Items { [SparCommand] public Task GoAsync(Nothing request) => null!; }", "SPAR025: Nothing")]
    [InlineData("public enum any { A } [SparContract] public sealed class Items { [SparCommand] public Task<any> GetAsync() => null!; }", "SPAR004: any")]
    public async Task A_contract_problem_is_reported_once_at_its_location(string contract, string expected)
    {
        var diagnostics = await GeneratorHarness.AnalyzeAsync(Program + contract);

        Assert.Equal([expected], diagnostics);
    }

    [Theory]
    [InlineData("[SparContract] public sealed class Items { [SparCommand] public Task Get() => null!; [SparCommand] public Task GetAsync() => null!; }", "SPAR016: Get", "SPAR016: GetAsync")]
    [InlineData("[SparContract] public sealed class Items { [SparCommand] public Task GetAsync() => null!; [SparCommand] public Task GetAsync(string id) => null!; }", "SPAR016: GetAsync", "SPAR016: GetAsync")]
    [InlineData("[SparContract] public sealed class Items { } [SparContract] public interface IItemsService { }", "SPAR005: Items", "SPAR005: IItemsService")]
    [InlineData("[SparContract] public sealed class Notes { } [SparContract(\"NOTES\")] public sealed class Other { }", "SPAR003: SparContract(\"NOTES\")")]
    [InlineData("namespace A { public sealed record Note(string Text); } namespace B { public sealed record Note(int Id); } [SparContract] public sealed class Items { [SparCommand] public Task<A.Note> GetAsync(B.Note request) => null!; }", "SPAR040: Note", "SPAR040: Note")]
    [InlineData("namespace A { public enum Note { X } } namespace B { public sealed record Note(int Id); } [SparContract] public sealed class Items { [SparCommand] public Task<A.Note> GetAsync(B.Note request) => null!; }", "SPAR040: Note", "SPAR040: Note")]
    public async Task A_clash_is_reported_at_each_declaration(string contract, params string[] expected)
    {
        var diagnostics = await GeneratorHarness.AnalyzeAsync(Program + contract);

        Assert.Equal(expected, diagnostics);
    }

    [Fact]
    public async Task DateTime_is_a_warning_at_each_place_that_uses_it_and_the_command_stays()
    {
        const string Contracts = """
            public sealed record Visit(System.DateTime At, System.Collections.Generic.IReadOnlyList<System.DateTime?> Earlier, System.DateTime Left);
            [SparContract] public sealed class Visits
            {
                [SparCommand] public Task<Visit> GetAsync(System.DateTime since) => null!;
                [SparCommand] public Task<System.DateTime> LastAsync() => null!;
            }
            """;

        var diagnostics = await GeneratorHarness.AnalyzeAsync(Program + Contracts);
        var run = GeneratorHarness.Generate(Program + Contracts);

        Assert.Equal(["SPAR024: At", "SPAR024: Earlier", "SPAR024: Left", "SPAR024: since", "SPAR024: LastAsync"], diagnostics);
        var messages = await GeneratorHarness.MessagesAsync(Program + Contracts);
        Assert.StartsWith("SPAR024: 'System.Collections.Generic.IReadOnlyList<System.DateTime?>' carries a DateTime,", messages[1], StringComparison.Ordinal);
        var contracts = run.Files["Spar.Contracts.g.cs"];
        Assert.Contains("new(\"visits\", \"get\", 0, HandleVisitsGet),", contracts, StringComparison.Ordinal);
        Assert.Contains("new(\"visits\", \"last\", 0, HandleVisitsLast),", contracts, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_kind_of_the_wire_rules_table_is_accepted_and_byte_arrays_are_scalars()
    {
        const string Contracts = """
            public sealed record Kinds(
                byte B, sbyte Sb, short S, ushort Us, uint Ui, ulong Ul, float F, decimal M, char C,
                System.DateOnly Day, System.TimeOnly Time, System.TimeSpan Span, byte[] Data, byte[]? Maybe,
                System.Collections.Generic.IReadOnlyList<ulong?> Ids, decimal? Price);
            [SparContract] public sealed class KindsService
            {
                [SparCommand] public Task<Kinds> EchoAsync(Kinds request) => Task.FromResult(request);
                [SparCommand] public Task<int> LengthAsync(byte[] data) => Task.FromResult(data.Length);
            }
            """;

        var diagnostics = await GeneratorHarness.AnalyzeAsync(Program + Contracts);
        var run = GeneratorHarness.Generate(Program + Contracts);

        Assert.Empty(diagnostics);
        Assert.Contains("new(\"kinds\", \"length\", 0, HandleKindsLength),", run.Files["Spar.Contracts.g.cs"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_enum_problem_says_why_and_its_commands_are_left_out()
    {
        const string Contracts = """
            [System.Flags] public enum Rights { Read = 1, Write = 2 }
            public enum Twice { One = 1, Uno = 1, Two = 2 }
            public enum Level { Low, High, @class }
            public sealed record Chore(string Title, Level Level, Level? Was, System.Collections.Generic.IReadOnlyList<Level> History);
            [SparContract] public sealed class Items
            {
                [SparCommand] public Task<Rights> RightsAsync() => null!;
                [SparCommand] public Task<Twice> TwiceAsync() => null!;
                [SparCommand] public Task<Chore> GetAsync(Level request) => null!;
            }
            """;

        var messages = await GeneratorHarness.MessagesAsync(Program + Contracts);
        var run = GeneratorHarness.Generate(Program + Contracts);

        Assert.Equal(
        [
            "SPAR025: Enum 'Rights' cannot travel on the wire as one member name: it is [Flags], and one member name cannot carry a combination of them",
            "SPAR025: Enum 'Twice' cannot travel on the wire as one member name: its members One and Uno have one value, which would have two names",
        ], messages);
        var contracts = run.Files["Spar.Contracts.g.cs"];
        Assert.Contains("new(\"items\", \"get\", 0, HandleItemsGet),", contracts, StringComparison.Ordinal);
        Assert.DoesNotContain("HandleItemsRights", contracts, StringComparison.Ordinal);
        Assert.Contains("\"class\" => global::Level.@class,", contracts, StringComparison.Ordinal);
        Assert.Contains("global::Level.@class => \"class\",", contracts, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_shared_types_with_one_name_are_reported_at_each_type()
    {
        const string Contracts = """
            namespace A { public sealed record Note(string Text); }
            namespace B { public sealed record Note(int Id); }
            [SparContract] public sealed class First { [SparCommand] public Task<A.Note> GetAsync() => null!; }
            [SparContract] public sealed class Second { [SparCommand] public Task<A.Note> GetAsync() => null!; }
            [SparContract] public sealed class Third { [SparCommand] public Task<B.Note> GetAsync() => null!; }
            [SparContract] public sealed class Fourth { [SparCommand] public Task<B.Note> GetAsync() => null!; }
            """;

        var messages = await GeneratorHarness.MessagesAsync(Program + Contracts);

        const string Message = "SPAR040: The shared module declares two types named 'Note' in TypeScript: A.Note and B.Note. Give one of them another name with [SparName(\"…\")]";
        Assert.Equal([Message, Message], messages);
    }

    [Fact]
    public async Task A_type_two_contracts_use_is_reported_once()
    {
        const string Contracts = """
            public sealed record any(string Name);
            [SparContract] public sealed class First { [SparCommand] public Task<any> GetAsync() => null!; }
            [SparContract] public sealed class Second { [SparCommand] public Task<any> GetAsync() => null!; }
            """;

        Assert.Equal(["SPAR004: any"], await GeneratorHarness.AnalyzeAsync(Program + Contracts));
    }

    [Fact]
    public async Task The_messages_name_the_problem_and_the_fix()
    {
        const string Contracts = """
            [SparContract] public sealed class Items { }
            [SparContract] public interface IItemsService { }
            [SparContract("sdk")] public sealed class Kit { }
            public sealed record Call(string Number);
            [SparContract] public sealed class Phone { [SparCommand] public Task<Call> DialAsync() => null!; }
            """;

        var messages = await GeneratorHarness.MessagesAsync(Program + Contracts);

        Assert.Equal(
            [
                "SPAR005: Contracts 'Items' and 'IItemsService' both resolve to the name 'items' (contract names are compared ignoring case). Give one of them another name with [SparContract(\"…\")]",
                "SPAR005: Contracts 'IItemsService' and 'Items' both resolve to the name 'items' (contract names are compared ignoring case). Give one of them another name with [SparContract(\"…\")]",
                "SPAR004: Contract 'Kit' resolves to the name 'sdk', which @spar/app uses for one of its own subpaths. Use another name in [SparContract(\"…\")]",
                "SPAR004: Record 'Call' resolves to the name 'Call', the name of a Spar SDK type the generated modules import. Give it a TypeScript name with [SparName(\"…\")]",
            ],
            messages);
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
