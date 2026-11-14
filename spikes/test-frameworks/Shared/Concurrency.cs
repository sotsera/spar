namespace Spike.Subject;

/// <summary>Counts how many probe tests overlap, to show each framework's default parallelism.</summary>
public static class Concurrency
{
    private static int s_current;
    private static int s_max;

    public static int Max => Volatile.Read(ref s_max);

    public static async Task<int> ProbeAsync(CancellationToken cancellationToken)
    {
        var now = Interlocked.Increment(ref s_current);
        int max;
        do
        {
            max = Volatile.Read(ref s_max);
        }
        while (now > max && Interlocked.CompareExchange(ref s_max, now, max) != max);

        try
        {
            await Task.Delay(250, cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref s_current);
        }

        return Max;
    }
}
