using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Sotsera.Spar.Windows.Interop;

internal static class ComReferences
{
    private static readonly StrategyBasedComWrappers _wrappers = new();

    // Unique instances allow deterministic release on the owning STA instead of the GC finalizer thread.
    internal static T Borrow<T>(nint pointer) where T : class
    {
        ArgumentOutOfRangeException.ThrowIfZero(pointer);
        return (T)_wrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.UniqueInstance);
    }

    internal static T Take<T>(nint pointer) where T : class
    {
        try
        {
            return Borrow<T>(pointer);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    internal static void Release(object? instance)
    {
        if (instance is ComObject wrapper)
        {
            wrapper.FinalRelease();
        }
    }

    internal static void WithCallback(object callback, Guid interfaceId, Action<nint> use)
    {
        var unknown = _wrappers.GetOrCreateComInterfaceForObject(callback, CreateComInterfaceFlags.None);
        try
        {
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in interfaceId, out var pointer));
            try
            {
                use(pointer);
            }
            finally
            {
                Marshal.Release(pointer);
            }
        }
        finally
        {
            Marshal.Release(unknown);
            GC.KeepAlive(callback);
        }
    }
}
