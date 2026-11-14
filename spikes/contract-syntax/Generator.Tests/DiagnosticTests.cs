namespace Generator.Tests;

/// <summary>Every invalid shape gets its diagnostic at the declaration to change; valid commands still dispatch.</summary>
public sealed class DiagnosticTests
{
    [Theory]
    [InlineData("[SparContract] public class Open { public Task<int> A() => Task.FromResult(1); }", "Open")]
    [InlineData("[SparContract] public abstract class Base { public Task<int> A() => Task.FromResult(1); }", "Base")]
    [InlineData("[SparContract] public static class Helpers { public static Task<int> A() => Task.FromResult(1); }", "Helpers")]
    [InlineData("[SparContract] public sealed record Rec(int X) { public Task<int> A() => Task.FromResult(1); }", "Rec")]
    public void ContractMustBeSealedClassOrInterface(string source, string located) =>
        Harness.Run(source).AssertDiagnostics(("SPAR001", located));

    [Fact]
    public void GenericContract() =>
        Harness.Run("[SparContract] public interface IStore<T> { Task<int> CountAsync(); }").AssertDiagnostics(("SPAR002", "IStore"));

    [Fact]
    public void InvalidExplicitNames() =>
        Harness.Run("""
            [SparContract("Copy-Jobs")] public sealed class CopyJobsService { public Task<int> A() => Task.FromResult(1); }
            [SparContract] public sealed class OtherService { [SparCommand("Get_Plan")] public Task<int> A() => Task.FromResult(1); }
            """)
            .AssertDiagnostics(("SPAR003", "SparContract(\"Copy-Jobs\")"), ("SPAR003", "SparCommand(\"Get_Plan\")"));

    [Fact]
    public void ReservedContractNames() =>
        Harness.Run("""
            [SparContract] public sealed class DeleteService { public Task<int> A() => Task.FromResult(1); }
            [SparContract("sdk")] public interface ITools { Task<int> A(); }
            """)
            .AssertDiagnostics(("SPAR004", "DeleteService"), ("SPAR004", "SparContract(\"sdk\")"));

    [Fact]
    public void UnsupportedReturnTypes()
    {
        var run = Harness.Run("""
            public closed record E { public sealed record A : E; }
            [SparContract]
            public sealed class JobsService
            {
                public ValueTask<int> A() => default;
                public Task B() => Task.CompletedTask;
                public void C() { }
                public int D() => 1;
                public async IAsyncEnumerable<Result<int, E>> F() { await Task.Yield(); yield break; }
                public Task<int> Valid() => Task.FromResult(1);
            }
            """);

        run.AssertDiagnostics(
            ("SPAR010", "ValueTask<int>"), ("SPAR010", "Task"), ("SPAR010", "void"), ("SPAR010", "int"),
            ("SPAR010", "IAsyncEnumerable<Result<int, E>>"));

        // Only the invalid commands are skipped.
        Assert.Contains("\"valid\" => Command_valid", run.Binding("jobs"));
    }

    [Fact]
    public void ParameterShapes() =>
        Harness.Run("""
            public sealed record R(string Id);
            [SparContract]
            public sealed class JobsService
            {
                public Task<int> TwoRequests(string id, int version, CancellationToken cancellationToken) => Task.FromResult(1);
                public Task<int> TokenFirst(CancellationToken cancellationToken, string id) => Task.FromResult(1);
                public Task<int> ByRef(ref int value) => Task.FromResult(1);
                public Task<int> Many(params int[] values) => Task.FromResult(1);
                public Task<int> Generic<T>(string id) => Task.FromResult(1);
                public Task<int> ArrayRequest(string[] ids) => Task.FromResult(1);
                public Task<int> ListRequest(IReadOnlyList<R> items) => Task.FromResult(1);
                public Task<int> NoToken(R request) => Task.FromResult(1);
            }
            """)
            .AssertDiagnostics(
                ("SPAR011", "version"),
                ("SPAR012", "cancellationToken"),
                ("SPAR013", "value"),
                ("SPAR013", "values"),
                ("SPAR014", "Generic"),
                ("SPAR015", "ids"),
                ("SPAR015", "items"));

    [Fact]
    public void DuplicateCommands() =>
        Harness.Run("""
            [SparContract]
            public sealed class JobsService
            {
                public Task<int> Get() => Task.FromResult(1);
                public Task<int> GetAsync() => Task.FromResult(1);
                public Task<int> Find(string id) => Task.FromResult(1);
                public Task<int> Find(int id) => Task.FromResult(1);
                [SparCommand("count")] public Task<int> Total() => Task.FromResult(1);
                public Task<int> CountAsync() => Task.FromResult(1);
            }
            """)
            .AssertDiagnostics(
                ("SPAR016", "Get"), ("SPAR016", "GetAsync"),
                ("SPAR016", "Find"), ("SPAR016", "Find"),
                ("SPAR016", "Total"), ("SPAR016", "CountAsync"));

    [Fact]
    public void UnsupportedWireTypes() =>
        Harness.Run("""
            public sealed record Bag(Dictionary<string, int> Values);
            public sealed record WithResult(Result<int, E> Outcome);
            public closed record E { public sealed record A : E; }
            [SparContract]
            public sealed class JobsService
            {
                public Task<Bag> A() => Task.FromResult(new Bag([]));
                public Task<int> B(DateTime when) => Task.FromResult(1);
                public Task<WithResult> C() => Task.FromResult(new WithResult(1));
                public Task<object> D() => Task.FromResult(new object());
            }
            """)
            .AssertDiagnostics(("SPAR020", "Values"), ("SPAR020", "when"), ("SPAR020", "Outcome"), ("SPAR020", "Task<object>"));

    [Fact]
    public void RecordShapes() =>
        Harness.Run("""
            public sealed record InitOnly { public string Name { get; init; } = ""; }
            public abstract record Shape(int Sides);
            public sealed record Box<T>(T Value);
            public closed record E { public sealed record A : E; }
            public sealed record Explicit { public Explicit(string name) { Label = name; } public string Label { get; } }
            public sealed record Twice(string Url, string URL);
            [SparContract]
            public sealed class JobsService
            {
                public Task<InitOnly> A() => Task.FromResult(new InitOnly());
                public Task<Shape> B() => Task.FromResult<Shape>(null!);
                public Task<Box<int>> C() => Task.FromResult(new Box<int>(1));
                public Task<E> D() => Task.FromResult<E>(new E.A());
                public Task<Explicit> F() => Task.FromResult(new Explicit("x"));
                public Task<Twice> G() => Task.FromResult(new Twice("a", "b"));
            }
            """)
            .AssertDiagnostics(
                ("SPAR021", "Name"),
                ("SPAR021", "Shape"),
                ("SPAR021", "Box"),
                ("SPAR021", "E"),
                ("SPAR021", "Label"),
                ("SPAR022", "name"),
                ("SPAR023", "URL"));

    [Fact]
    public void ErrorTypeMustBeClosedRecord() =>
        Harness.Run("""
            public abstract record OpenError { public sealed record A : OpenError; }
            public closed class ClassError { }
            public sealed class ClassCase : ClassError { }
            public sealed record Plain(string X);
            [SparContract]
            public sealed class JobsService
            {
                public Task<Result<int, OpenError>> A() => Task.FromResult<Result<int, OpenError>>(1);
                public Task<Result<int, ClassError>> B() => Task.FromResult<Result<int, ClassError>>(1);
                public Task<Result<int, Plain>> C() => Task.FromResult<Result<int, Plain>>(1);
            }
            """)
            .AssertDiagnostics(("SPAR030", "OpenError"), ("SPAR030", "ClassError"), ("SPAR030", "Plain"));

    [Fact]
    public void ErrorCases() =>
        Harness.Run("""
            public closed record PlanError
            {
                public sealed record NotFound(string Id) : PlanError;
                public record Open(string Id) : PlanError;
                public sealed record Bad(string Message, string Code, int Kind, string Execution) : PlanError;
                [SparName("notFound")] public sealed record Missing(string Id) : PlanError;
                [SparName("not-found")] public sealed record Odd : PlanError;
            }
            public sealed record Outside(string Id) : PlanError;
            public closed record Empty;
            [SparContract]
            public sealed class JobsService
            {
                public Task<Result<int, PlanError>> A() => Task.FromResult<Result<int, PlanError>>(1);
                public Task<Result<int, Empty>> B() => Task.FromResult<Result<int, Empty>>(1);
            }
            """)
            .AssertDiagnostics(
                ("SPAR031", "Outside"),
                ("SPAR032", "Open"),
                ("SPAR033", "Message"), ("SPAR033", "Code"), ("SPAR033", "Kind"), ("SPAR033", "Execution"),
                ("SPAR034", "Missing"),
                ("SPAR003", "Odd"),
                ("SPAR035", "Empty"));

    [Fact]
    public void InvalidSparName() =>
        Harness.Run("""
            [SparName("Plan Item")] public sealed record Item(string Id);
            [SparContract] public sealed class JobsService { public Task<Item> A() => Task.FromResult(new Item("x")); }
            """)
            .AssertDiagnostics(("SPAR003", "Item"));

    [Fact]
    public void OneInvalidCommandKeepsTheRest()
    {
        var run = Harness.Run("""
            [SparContract]
            public interface IJobs
            {
                Task<int> CountAsync(CancellationToken cancellationToken);
                Task<int> Broken(string a, string b);
                IAsyncEnumerable<string> NamesAsync(CancellationToken cancellationToken);
            }
            """);

        run.AssertDiagnostics(("SPAR011", "b"));
        Assert.Contains("\"count\" => Command_count", run.Binding("jobs"));
        Assert.Contains("\"names\" => Stream_names", run.Binding("jobs"));
        Assert.DoesNotContain("broken", run.Binding("jobs"));
        Assert.DoesNotContain("broken", run.Client["packages/@spar/jobs/index.ts"]);
    }
}
