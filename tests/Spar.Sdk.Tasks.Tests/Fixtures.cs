namespace Spar.Sdk.Tasks.Tests;

/// <summary>Contracts the client tests generate from.</summary>
internal static class Fixtures
{
    /// <summary>Two contracts sharing <c>Item</c>, every wire kind, every return shape, and documentation to carry over.</summary>
    public const string Shop = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Spar;

        namespace Shop;

        /// <summary>An item in the catalogue.</summary>
        /// <param name="Sku">The stock-keeping unit, for example <c>A-100</c>.</param>
        public sealed record Item(string Sku, long Stock, double Price, IReadOnlyList<string> Tags, Item? Replacement);

        public sealed record Basket(Item[] Items, int? Coupon, IReadOnlyList<string?> Notes);

        public sealed record Receipt(Guid Id, DateTimeOffset At, bool Paid);

        /// <summary>Why an order failed; never ends this comment early: */</summary>
        public closed record OrderError
        {
            /// <summary>The item is gone.</summary>
            public sealed record OutOfStock(string Sku, long Left) : OrderError;

            public sealed record Closed : OrderError;
        }

        /// <summary>Takes orders.</summary>
        [SparContract]
        public sealed class OrdersService
        {
            /// <summary>Places an order for the <see cref="Basket"/>.</summary>
            [SparCommand]
            public Task<Result<Receipt, OrderError>> PlaceAsync(Basket basket) => throw new NotImplementedException();

            [SparCommand]
            public Task<Result<OrderError>> CancelAsync(string orderId) => throw new NotImplementedException();

            [SparCommand]
            public Task ClearAsync() => throw new NotImplementedException();

            [SparCommand]
            public ValueTask<long> CountAsync(CancellationToken token) => throw new NotImplementedException();
        }

        [SparContract]
        public interface IStockService
        {
            Task<Item> FindAsync(string sku);

            Task<IReadOnlyList<Item>> ListAsync();
        }
        """;
}
