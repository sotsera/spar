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

        public sealed record Quote(string Sku, double Price);

        public closed record TickerError
        {
            public sealed record Unknown(string Sku) : TickerError;

            public sealed record Closed : TickerError;
        }

        /// <summary>Live stock.</summary>
        [SparContract]
        public sealed class TickerService
        {
            /// <summary>Every item, as it is read.</summary>
            [SparCommand]
            public IAsyncEnumerable<Item> ItemsAsync(CancellationToken token) => throw new NotImplementedException();

            [SparCommand]
            public IAsyncEnumerable<Result<Quote, TickerError>> QuotesAsync(string since) => throw new NotImplementedException();

            /// <summary>Stock changes; observation only.</summary>
            [SparCommand]
            [SparSubscription(Capacity = 64)]
            public IAsyncEnumerable<Item> ChangesAsync(CancellationToken token) => throw new NotImplementedException();

            [SparCommand]
            [SparSubscription(LatestOnly = true, Resume = SubscriptionResume.Manual)]
            public IAsyncEnumerable<Result<long, TickerError>> LevelAsync(string sku) => throw new NotImplementedException();
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

            [SparCommand]
            public Task<Mood> MoodAsync(Mood request) => throw new NotImplementedException();
        }

        /// <summary>How it feels; both contracts use it, so it lives in the shared module.</summary>
        public enum Mood { Calm, Busy }

        public enum Level { Low, High, @class }

        /// <summary>Every other kind of the wire-rules table.</summary>
        public sealed record Kinds(
            byte B, sbyte Sb, short S, ushort Us, uint Ui, ulong Ul, float F, decimal M, char C,
            DateTime At, DateOnly Day, TimeOnly Time, TimeSpan Span, byte[] Data, byte[]? Maybe, IReadOnlyList<ulong?> Ids,
            IReadOnlyList<byte> Small, Level Level, Mood? Mood, IReadOnlyList<Level> Levels);

        [SparContract]
        public sealed class KindsService
        {
            [SparCommand]
            public Task<Kinds> EchoAsync(Kinds request) => throw new NotImplementedException();

            [SparCommand]
            public Task<decimal?> TotalAsync(byte[] data) => throw new NotImplementedException();
        }
        """;
}
