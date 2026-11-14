using Spar;

namespace Sample.Invalid;

/// <summary>Same wire name as Sample.Contracts.InventoryService: SPAR005.</summary>
[SparContract]
public sealed class InventoryService
{
    public Task<int> CountAsync() => Task.FromResult(0);
}

/// <summary>Same TypeScript name as Sample.Contracts.Item in contract 'orders': SPAR040.</summary>
public sealed record Item(string Label);

[SparContract("orders")]
public sealed class OrdersService
{
    public Task<Item> LocalAsync() => Task.FromResult(new Item("x"));

    public Task<Sample.Contracts.Item> ReferencedAsync() => Task.FromResult(new Sample.Contracts.Item("A-1", 1, null));
}

public abstract record OpenError
{
    public sealed record Failed : OpenError;
}

public closed record JobError
{
    public sealed record Failed(string Message) : JobError;
}

[SparContract]
public interface IReports
{
    ValueTask<int> CountAsync();

    Task<int> FindAsync(string a, string b, CancellationToken cancellationToken);

    Task<Result<int, OpenError>> RunAsync();

    Task<Result<int, JobError>> StartAsync();

    Task<int> FineAsync(CancellationToken cancellationToken);
}
