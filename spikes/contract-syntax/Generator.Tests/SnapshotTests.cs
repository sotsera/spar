using System.Text;

namespace Generator.Tests;

/// <summary>
/// The complete generated output (C# and TypeScript) for a representative app with a referenced library. Set
/// SPAR_UPDATE_SNAPSHOTS=1 to rewrite the snapshot after an intended change, then review the diff.
/// </summary>
public sealed class SnapshotTests
{
    private const string Library = """
        namespace Shop;
        public sealed record Money(double Amount, string Currency);
        public sealed record Item(string Sku, int Quantity, Money? Price);
        public closed record LookupError
        {
            public sealed record NotFound(string Sku) : LookupError;
            public sealed record Discontinued(string Sku, string? Replacement) : LookupError;
        }
        [SparContract] public sealed class InventoryService
        {
            public Task<Item> GetAsync(string sku, CancellationToken cancellationToken) => Task.FromResult(new Item(sku, 1, null));
        }
        """;

    private const string App = """
        namespace App;
        public sealed record BuildPlanRequest(string Connection, string Table, bool Force);
        public sealed record Plan(Guid Id, string Name, IReadOnlyList<PlanStep> Steps, Shop.Money? Budget, long? Version);
        public sealed record PlanStep(int Order, string Title);
        [SparName("PlanItem")] public sealed record Item(string Label, double Weight);
        public closed record PlanError
        {
            public sealed record ConnectionNotFound(string ConnectionId) : PlanError;
            public sealed record TableMissing(string Connection, string Table) : PlanError;
            public sealed record Forbidden : PlanError;
        }

        [SparContract]
        public sealed class PlanningService
        {
            public Task<Plan> GetAsync(string id, CancellationToken cancellationToken) => throw new NotImplementedException();
            public Task<int> CountAsync() => Task.FromResult(0);
            public Task<Result<Plan, PlanError>> BuildAsync(BuildPlanRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
            public IAsyncEnumerable<PlanStep> StepsAsync(int count, CancellationToken cancellationToken) => throw new NotImplementedException();
            [SparCommand("total")] public Task<Shop.Money> GetTotalAsync(Guid planId, CancellationToken cancellationToken) => throw new NotImplementedException();
            public Task<Result<Shop.Item, Shop.LookupError>> LookupAsync(string sku) => throw new NotImplementedException();
            public Task<Item[]> ItemsAsync() => throw new NotImplementedException();
        }

        [SparContract("billing")]
        public interface IBillingApi
        {
            Task<Shop.Money> ChargeAsync(Shop.Money amount, CancellationToken cancellationToken);
        }
        """;

    [Fact]
    public void RepresentativeApp()
    {
        var run = Harness.Run(App, Harness.Library(Library, "Shop"));
        run.AssertClean();

        var actual = new StringBuilder();
        foreach (var (name, text) in run.Sources.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            actual.Append("==== C# ").Append(name).Append('\n').Append(text).Append('\n');
        }

        foreach (var (name, text) in run.Client)
        {
            actual.Append("==== TS ").Append(name).Append('\n').Append(text).Append('\n');
        }

        var path = Path.Combine(Harness.SnapshotDirectory(), "RepresentativeApp.txt");
        if (Environment.GetEnvironmentVariable("SPAR_UPDATE_SNAPSHOTS") == "1" || !File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual.ToString(), new UTF8Encoding(false));
            Assert.Fail($"Snapshot written to {path}. Review it, then run the tests again.");
        }

        Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), actual.ToString());
    }
}
