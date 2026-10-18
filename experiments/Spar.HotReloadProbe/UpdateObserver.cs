using System.Reflection.Metadata;

[assembly: MetadataUpdateHandler(typeof(Sotsera.Spar.HotReloadProbe.UpdateObserver))]

namespace Sotsera.Spar.HotReloadProbe;

// .NET applies the deltas. This observer records actual runtime notifications.
internal static class UpdateObserver
{
    private static readonly object _gate = new();
    private static long _revision;
    private static int _clearCacheCount;
    private static int _callbackThreadId;
    private static string[] _updatedTypes = [];

    public static void ClearCache(Type[]? updatedTypes)
    {
        lock (_gate)
        {
            _clearCacheCount++;
        }
    }

    public static void UpdateApplication(Type[]? updatedTypes)
    {
        lock (_gate)
        {
            _revision++;
            _callbackThreadId = Environment.CurrentManagedThreadId;
            _updatedTypes = updatedTypes?
                .Select(type => type.FullName ?? type.Name)
                .ToArray() ?? [];
        }
    }

    public static UpdateSnapshot Read()
    {
        lock (_gate)
        {
            return new UpdateSnapshot(_revision, _clearCacheCount, _callbackThreadId, _updatedTypes);
        }
    }
}

internal sealed record UpdateSnapshot(long Revision, int ClearCacheCount, int CallbackThreadId, string[] UpdatedTypes);
