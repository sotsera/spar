namespace Generator.Tests;

/// <summary>
/// Name clashes across contracts, DTOs and errors, with one side in a referenced assembly. Each case runs with the
/// library as metadata (command-line build) and as a project reference (IDE).
/// </summary>
public sealed class ClashMatrixTests
{
    private const string Lib = """
        namespace Lib;
        public sealed record Item(string Sku);
        public sealed record Money(double Amount);
        public closed record LookupError { public sealed record NotFound(string Sku) : LookupError; }
        public closed record Conflict { public sealed record Taken(string Id) : Conflict; }
        public abstract record OpenError { public sealed record A : OpenError; }
        [SparContract] public sealed class PlanningService { public Task<int> CountAsync() => Task.FromResult(1); }
        [SparContract("stock")] public interface IInventory { Task<Item> GetAsync(string sku); }
        """;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ContractNameClashWithReferencedContract(bool metadata)
    {
        var run = Harness.Run("""
            namespace App;
            [SparContract] public sealed class PlanningService { public Task<int> TotalAsync() => Task.FromResult(1); }
            """, Harness.Library(Lib, "Lib", metadata));

        run.AssertDiagnostics(("SPAR005", "PlanningService"));
        Assert.Contains("'Lib.PlanningService' (in 'Lib')", run.Diagnostics[0]);
        Assert.Contains("[SparContract(\"…\")]", run.Diagnostics[0]);

        // Neither is exported; the referenced 'stock' contract still is.
        Assert.Equal(["stock"], run.Set.Contracts.Select(c => c.WireName));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExplicitNameResolvesContractClash(bool metadata)
    {
        var run = Harness.Run("""
            namespace App;
            [SparContract("appPlanning")] public sealed class PlanningService { public Task<int> TotalAsync() => Task.FromResult(1); }
            """, Harness.Library(Lib, "Lib", metadata));

        run.AssertClean();
        Assert.Equal(["appPlanning", "planning", "stock"], run.Set.Contracts.Select(c => c.WireName));
        Assert.Contains("\"planning\" => SparBinding_planning", run.Sources["SparCatalog.g.cs"]);
        Assert.Contains("TryAddScoped<global::Lib.PlanningService>", run.Sources["SparCatalog.g.cs"]);
    }

    [Fact]
    public void ContractNameClashBetweenTwoReferencedAssemblies()
    {
        var one = Harness.Library("namespace One; [SparContract] public sealed class ReportsService { public Task<int> A() => Task.FromResult(1); }", "One");
        var two = Harness.Library("namespace Two; [SparContract] public interface IReports { Task<int> B(); }", "Two");

        var run = Harness.Run("namespace App; public static class Nothing { }", one, two);

        // No source location exists in the application, so the generator reports it without one.
        run.AssertDiagnostics(("SPAR005", null));
        Assert.Contains("'One.ReportsService' (in 'One') and 'Two.IReports' (in 'Two')", run.Diagnostics[0]);
        Assert.Contains("in its assembly", run.Diagnostics[0]);
        Assert.Empty(run.Set.Contracts);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DtoNameClashInOneContract(bool metadata)
    {
        var run = Harness.Run("""
            namespace App;
            public sealed record Item(string Label);
            [SparContract("appPlanning")]
            public sealed class PlanningService
            {
                public Task<Item> LocalAsync() => Task.FromResult(new Item("x"));
                public Task<Lib.Item> ReferencedAsync() => Task.FromResult(new Lib.Item("y"));
            }
            """, Harness.Library(Lib, "Lib", metadata));

        run.AssertDiagnostics(("SPAR040", "Item"));
        Assert.Contains("two different types named 'Item': 'App.Item' and 'Lib.Item'", run.Diagnostics[0]);
        Assert.Contains("[SparName(", run.Diagnostics[0]);
        Assert.DoesNotContain("packages/@spar/appPlanning/index.ts", run.Client.Keys);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SparNameResolvesDtoClash(bool metadata)
    {
        var run = Harness.Run("""
            namespace App;
            [SparName("PlanItem")] public sealed record Item(string Label);
            [SparContract("appPlanning")]
            public sealed class PlanningService
            {
                public Task<Item> LocalAsync() => Task.FromResult(new Item("x"));
                public Task<Lib.Item> ReferencedAsync() => Task.FromResult(new Lib.Item("y"));
            }
            """, Harness.Library(Lib, "Lib", metadata));

        run.AssertClean();
        var module = run.Client["packages/@spar/appPlanning/index.ts"];
        Assert.Contains("export interface PlanItem {", module);
        Assert.Contains("import type { Item } from \"../shared/Lib.Item.js\";", module);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SameNameInDifferentContractsIsFine(bool metadata)
    {
        var run = Harness.Run("""
            namespace App;
            public sealed record Item(string Label);
            [SparContract] public sealed class CatalogService { public Task<Item> GetAsync() => Task.FromResult(new Item("x")); }
            [SparContract] public interface IOrders { Task<Lib.Item> GetAsync(); }
            """, Harness.Library(Lib, "Lib", metadata));

        run.AssertClean();
        Assert.Contains("export interface Item {\n  readonly label: string;\n}", run.Client["packages/@spar/catalog/index.ts"]);
        // Lib.Item is also used by the referenced 'stock' contract, so it is shared; App.Item is not.
        Assert.Contains("import type { Item } from \"../shared/Lib.Item.js\";", run.Client["packages/@spar/orders/index.ts"]);
        Assert.Contains("export interface Item {\n  readonly sku: string;\n}", run.Client["packages/@spar/shared/Lib.Item.ts"]);
        Assert.DoesNotContain("packages/@spar/shared/App.Item.ts", run.Client.Keys);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SharedTypeIsEmittedOnceAndReExported(bool metadata)
    {
        var run = Harness.Run("""
            namespace App;
            public sealed record Invoice(Lib.Money Total);
            [SparContract] public sealed class BillingService { public Task<Invoice> GetAsync() => Task.FromResult(new Invoice(new Lib.Money(1))); }
            [SparContract] public interface IQuotes { Task<Lib.Money> QuoteAsync(string sku); }
            """, Harness.Library(Lib, "Lib", metadata));

        run.AssertClean();
        var declarations = run.Client.Where(f => f.Value.Contains("export interface Money", StringComparison.Ordinal)).Select(f => f.Key);
        Assert.Equal(["packages/@spar/shared/Lib.Money.ts"], declarations);
        foreach (var contract in new[] { "billing", "quotes" })
        {
            var module = run.Client[$"packages/@spar/{contract}/index.ts"];
            Assert.Contains("import type { Money } from \"../shared/Lib.Money.js\";", module);
            Assert.Contains("export type { Money };", module);
        }

        // The referenced 'stock' contract also uses Lib.Item, but only it does: declared in its own package.
        Assert.Contains("export interface Item", run.Client["packages/@spar/stock/index.ts"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ErrorNameClashInOneContract(bool metadata)
    {
        var run = Harness.Run("""
            namespace App;
            public closed record LookupError { public sealed record Gone : LookupError; }
            [SparContract("appPlanning")]
            public sealed class PlanningService
            {
                public Task<Result<int, LookupError>> A() => Task.FromResult<Result<int, LookupError>>(1);
                public Task<Result<int, Lib.LookupError>> B() => Task.FromResult<Result<int, Lib.LookupError>>(1);
            }
            """, Harness.Library(Lib, "Lib", metadata));

        run.AssertDiagnostics(("SPAR040", "LookupError"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DtoAndErrorNameClash(bool metadata)
    {
        var run = Harness.Run("""
            namespace App;
            public sealed record Conflict(string Reason);
            [SparContract("appPlanning")]
            public sealed class PlanningService
            {
                public Task<Conflict> A() => Task.FromResult(new Conflict("x"));
                public Task<Result<int, Lib.Conflict>> B() => Task.FromResult<Result<int, Lib.Conflict>>(1);
            }
            """, Harness.Library(Lib, "Lib", metadata));

        run.AssertDiagnostics(("SPAR040", "Conflict"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReferencedClosedErrorIsReadFromTheReference(bool metadata)
    {
        var run = Harness.Run("""
            namespace App;
            [SparContract("appPlanning")]
            public sealed class PlanningService
            {
                public Task<Result<int, Lib.LookupError>> FindAsync(string sku) =>
                    Task.FromResult<Result<int, Lib.LookupError>>(new Lib.LookupError.NotFound(sku));
            }
            """, Harness.Library(Lib, "Lib", metadata));

        run.AssertClean();
        Assert.Contains("case global::Lib.LookupError.NotFound c0:", run.Sources["SparSerializers.g.cs"]);
        Assert.Contains("| { readonly code: \"notFound\"; readonly sku: string };", run.Client["packages/@spar/appPlanning/index.ts"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReferencedOpenErrorIsRejectedAtTheCommand(bool metadata)
    {
        var run = Harness.Run("""
            namespace App;
            [SparContract("appPlanning")]
            public sealed class PlanningService
            {
                public Task<Result<int, Lib.OpenError>> A() => Task.FromResult<Result<int, Lib.OpenError>>(1);
            }
            """, Harness.Library(Lib, "Lib", metadata));

        // The error type has no location in this compilation: the diagnostic goes to the command's return type.
        run.AssertDiagnostics(("SPAR030", "Task<Result<int, Lib.OpenError>>"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ProblemsInsideAReferencedContract(bool metadata)
    {
        var library = Harness.Library("""
            namespace L3.A { public sealed record Item(string X); }
            namespace L3.B { public sealed record Item(int Y); }
            namespace L3
            {
                [SparContract] public sealed class CatalogService
                {
                    public Task<A.Item> FirstAsync() => Task.FromResult(new A.Item("x"));
                    public Task<B.Item> SecondAsync() => Task.FromResult(new B.Item(1));
                }

                [SparContract] public interface IBroken { Task<int> TwoAsync(string a, string b); Task<int> FineAsync(); }
            }
            """, "L3", metadata);

        var run = Harness.Run("namespace App; public static class Nothing { }", library);

        run.AssertDiagnostics(("SPAR011", null), ("SPAR040", null));
        Assert.Contains(run.Diagnostics, d => d.Contains("In referenced assembly 'L3': Command 'TwoAsync'", StringComparison.Ordinal));
        Assert.Contains(run.Diagnostics, d => d.Contains("In referenced assembly 'L3': Contract 'catalog' uses two different types named 'Item'", StringComparison.Ordinal));
        Assert.Equal(["broken"], run.Set.Contracts.Select(c => c.WireName));
    }
}
