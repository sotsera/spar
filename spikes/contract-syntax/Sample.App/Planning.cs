using System.Runtime.CompilerServices;
using Sample.Contracts;
using Spar;

namespace Sample.App;

public sealed record BuildPlanRequest(string Connection, string Table, bool Force);

public sealed record Plan(Guid Id, string Name, IReadOnlyList<PlanStep> Steps, Money? Budget);

public sealed record PlanStep(int Order, string Title);

/// <summary>Same simple name as <c>Sample.Contracts.Item</c>, which this contract also uses.</summary>
[SparName("PlanItem")]
public sealed record Item(string Label, double Weight);

public closed record PlanError
{
    public sealed record ConnectionNotFound(string ConnectionId) : PlanError;

    public sealed record TableMissing(string Connection, string Table) : PlanError;

    /// <summary>A case without payload.</summary>
    public sealed record Forbidden : PlanError;
}

/// <summary>Scoped: one per call, shared by everything the call resolves.</summary>
public sealed class CallScope
{
    private static int s_created;

    public CallScope()
    {
        Number = Interlocked.Increment(ref s_created);
    }

    public static int Created => Volatile.Read(ref s_created);

    public int Number { get; }
}

/// <summary>Class form: wire name 'planning' (type name without 'Service').</summary>
[SparContract]
public sealed class PlanningService(CallScope scope)
{
    private static int s_instances;

    public static int Instances => Volatile.Read(ref s_instances);

    public int Instance { get; } = Interlocked.Increment(ref s_instances);

    /// <summary>Scalar request: <c>planning.get("p1")</c>.</summary>
    public Task<Plan> GetAsync(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plan = new Plan(Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"), id, [new PlanStep(1, "scan")], new Money(12, "EUR"));
        return Task.FromResult(plan);
    }

    /// <summary>No request, no token.</summary>
    public Task<int> CountAsync() => Task.FromResult(scope.Number);

    /// <summary>Expected errors: a closed record converts implicitly into the result.</summary>
    public async Task<Result<Plan, PlanError>> BuildAsync(BuildPlanRequest request, CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Connection != "main")
        {
            return new PlanError.ConnectionNotFound(request.Connection);
        }

        if (request.Table.Length == 0)
        {
            return new PlanError.TableMissing(request.Connection, request.Table);
        }

        if (!request.Force && request.Table == "secret")
        {
            return new PlanError.Forbidden();
        }

        return new Plan(Guid.Empty, request.Table, [], null);
    }

    /// <summary>A stream with a scalar request.</summary>
    public async IAsyncEnumerable<PlanStep> StepsAsync(int count, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 1; i <= count; i++)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return new PlanStep(i, $"step {i} of call {scope.Number}");
        }
    }

    /// <summary>Explicit command name, a Guid request and a type shared with another contract.</summary>
    [SparCommand("total")]
    public Task<Money> GetTotalAsync(Guid planId, CancellationToken cancellationToken) =>
        Task.FromResult(new Money(planId == Guid.Empty ? 0 : 42, "EUR"));

    /// <summary>An error type and a DTO from the referenced assembly, read from metadata.</summary>
    public Task<Result<Sample.Contracts.Item, LookupError>> LookupAsync(string sku) =>
        Task.FromResult<Result<Sample.Contracts.Item, LookupError>>(sku == "A-1"
            ? new Sample.Contracts.Item("A-1", 1, null)
            : new LookupError.NotFound(sku));

    /// <summary>The app's own Item, renamed in TypeScript with [SparName].</summary>
    public Task<Item[]> ItemsAsync() => Task.FromResult<Item[]>([new Item("box", 1.5)]);

    /// <summary>Not public: not a command.</summary>
    internal int Helper() => Instance;
}
