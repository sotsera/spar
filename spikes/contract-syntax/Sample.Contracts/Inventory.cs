using Spar;

namespace Sample.Contracts;

/// <summary>Shared with the app's contracts: emitted once in TypeScript and re-exported.</summary>
public sealed record Money(double Amount, string Currency);

/// <summary>Same simple name as <c>Sample.App.Item</c>; the app gives its own one another TypeScript name.</summary>
public sealed record Item(string Sku, int Quantity, Money? Price);

public sealed record QuoteRequest(string Sku, int Quantity);

/// <summary>A closed error record read by the app's generator from metadata.</summary>
public closed record LookupError
{
    public sealed record NotFound(string Sku) : LookupError;

    public sealed record Discontinued(string Sku, string? Replacement) : LookupError;
}

/// <summary>Class form in a referenced assembly. Wire name 'inventory'.</summary>
[SparContract]
public sealed class InventoryService
{
    private static readonly Item[] Items = [new("A-1", 3, new Money(9.5, "EUR")), new("B-2", 0, null)];

    public Task<Item> GetAsync(string sku, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Items.First(i => i.Sku == sku));
    }

    public async Task<Result<Item, LookupError>> FindAsync(string sku, CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        if (sku == "OLD-1")
        {
            return new LookupError.Discontinued(sku, "A-1");
        }

        var item = Items.FirstOrDefault(i => i.Sku == sku);
        return item is null ? new LookupError.NotFound(sku) : item;
    }
}

/// <summary>Interface form in a referenced assembly. Wire name 'pricing'; the app registers <see cref="Pricing"/>.</summary>
[SparContract]
public interface IPricingService
{
    Task<Money> QuoteAsync(QuoteRequest request, CancellationToken cancellationToken);
}

public sealed class Pricing : IPricingService
{
    public Task<Money> QuoteAsync(QuoteRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new Money(request.Quantity * 2.5, "EUR"));
}
