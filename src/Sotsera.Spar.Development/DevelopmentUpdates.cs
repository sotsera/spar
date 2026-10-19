using System.Reflection.Metadata;

[assembly: MetadataUpdateHandler(typeof(Sotsera.Spar.Development.DevelopmentUpdates))]

namespace Sotsera.Spar.Development;

// This observes the selected .NET engine. It never produces or applies deltas.
internal static class DevelopmentUpdates
{
    private static Action<long, Type[]?>? _observer;
    private static long _revision;

    public static void ClearCache(Type[]? updatedTypes)
    {
    }

    public static void UpdateApplication(Type[]? updatedTypes)
    {
        Volatile.Read(ref _observer)?.Invoke(Interlocked.Increment(ref _revision), updatedTypes);
    }

    internal static void Subscribe(Action<long, Type[]?> observer)
    {
        if (Interlocked.CompareExchange(ref _observer, observer, null) is not null)
        {
            throw new InvalidOperationException("Only one development application can own update observation.");
        }
    }

    internal static void Unsubscribe(Action<long, Type[]?> observer)
    {
        Interlocked.CompareExchange(ref _observer, null, observer);
    }
}
