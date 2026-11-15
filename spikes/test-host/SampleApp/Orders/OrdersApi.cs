namespace SampleApp.Orders;

/// <summary>A real resource: here, the company's order database.</summary>
public interface IOrderDatabase
{
    Task<int> CountOpenOrdersAsync(CancellationToken cancellationToken);
}

public sealed class SqlOrderDatabase : IOrderDatabase
{
    // Spike: stands for opening a connection to a real server. The guard tests check a test host never builds one.
    internal static int Constructed;

    public SqlOrderDatabase()
    {
        Interlocked.Increment(ref Constructed);
    }

    public Task<int> CountOpenOrdersAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException("This would query the production order database.");
}

public sealed class OrdersApi(IOrderDatabase database)
{
    public Task<int> CountAsync(CancellationToken cancellationToken) => database.CountOpenOrdersAsync(cancellationToken);
}
