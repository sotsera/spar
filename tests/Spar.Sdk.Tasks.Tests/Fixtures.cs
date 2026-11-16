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

    /// <summary>
    /// The shapes most likely to come out differently: reserved words as property and parameter names, a list of lists
    /// of nullable values, an empty record, a nullable record, and an error whose cases carry nothing.
    /// </summary>
    public const string Edges = """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Spar;

        namespace Edges;

        public sealed record Nothing();

        public sealed record Odd(string Class, int Default, IReadOnlyList<IReadOnlyList<int?>> Grid, long? Big, Nothing? Empty);

        public closed record Refusal
        {
            public sealed record Busy : Refusal;

            public sealed record Gone : Refusal;
        }

        [SparContract]
        public sealed class OddService
        {
            [SparCommand]
            public Task<Odd> GetAsync(string @new) => throw new NotImplementedException();

            [SparCommand]
            public Task<Result<Refusal>> TryAsync(Nothing request) => throw new NotImplementedException();

            [SparCommand]
            public Task<IReadOnlyList<long?>> NumbersAsync(int count) => throw new NotImplementedException();
        }
        """;
}
