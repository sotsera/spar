namespace Spar.Generator.Tests;

internal static class Fixtures
{
    /// <summary>A contract with every shape this generator supports today: returns, wire types, errors, both forms.</summary>
    public const string Catalog = """
        public sealed record Detail(
            string Name,
            string? Note,
            bool Done,
            int Count,
            long Big,
            double Ratio,
            System.Guid Key,
            System.DateTimeOffset At,
            int? Maybe,
            System.Collections.Generic.IReadOnlyList<string> Tags,
            System.Collections.Generic.IReadOnlyList<string?> Cells,
            Item[] Items,
            Detail? Parent);

        /// <summary>Why it failed.</summary>
        public closed record CatalogError
        {
            public sealed record Missing(string Name) : CatalogError;

            public sealed record Locked : CatalogError;

            [SparName("tooBig")]
            public sealed record Oversized(long Limit) : CatalogError;
        }

        [SparContract]
        public sealed class CatalogService
        {
            [SparCommand]
            public Task<Result<Detail, CatalogError>> FindAsync(Item request, CancellationToken cancellationToken) => null!;

            [SparCommand(TimeoutSeconds = 30)]
            public Task<Result<CatalogError>> RemoveAsync(Item request) => null!;

            [SparCommand]
            public Task<System.Collections.Generic.IReadOnlyList<Item>> ListAsync() => null!;

            [SparCommand]
            public ValueTask<int> CountAsync(CancellationToken cancellationToken) => default;

            [SparCommand]
            public Task ClearAsync() => Task.CompletedTask;

            [SparCommand("rename")]
            public ValueTask<Result<Item, CatalogError>> ChangeNameAsync(string request) => default;

            [SparCommand]
            public System.Collections.Generic.IAsyncEnumerable<Item> RowsAsync(int request, CancellationToken cancellationToken) => null!;

            [SparCommand]
            public System.Collections.Generic.IAsyncEnumerable<Result<Detail, CatalogError>> ScanAsync(Item request) => null!;

            [SparCommand]
            [SparSubscription(Capacity = 32)]
            public System.Collections.Generic.IAsyncEnumerable<Item> WatchAsync(CancellationToken cancellationToken) => null!;

            [SparCommand]
            [SparSubscription(LatestOnly = true, Resume = SubscriptionResume.Manual)]
            public System.Collections.Generic.IAsyncEnumerable<Result<int, CatalogError>> LevelAsync() => null!;

            public Task NotACommandAsync() => Task.CompletedTask;
        }

        [SparContract]
        public interface IIOStatsService
        {
            Task<Item> ReadAsync(Item request);
        }

        """;
}
