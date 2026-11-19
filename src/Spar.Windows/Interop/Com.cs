using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Spar.Windows.Interop;

/// <summary>
/// Ownership of WebView2 objects (skill "spar-native-interop"): each pointer becomes a wrapper of its own
/// (<see cref="CreateObjectFlags.UniqueInstance"/>), which <see cref="Release"/> lets go of at once, on the thread
/// that owns it, instead of leaving it to the finalizer.
/// </summary>
internal static class Com
{
    /// <summary>The wrappers for unique instances: stateless strategies, safe from any thread.</summary>
    private static readonly StrategyBasedComWrappers Wrappers = new();

    /// <summary>Wraps a pointer whose reference the caller owns; the wrapper takes that reference over.</summary>
    public static T Take<T>(nint pointer)
        where T : class
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

    /// <summary>Wraps a borrowed pointer (an event's args) with a reference of the wrapper's own.</summary>
    public static T Borrow<T>(nint pointer)
        where T : class =>
        (T)Wrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.UniqueInstance);

    /// <summary>Releases what a wrapper holds; the wrapper is unusable afterwards. Null is ignored.</summary>
    public static void Release(object? wrapper)
    {
        if (wrapper is ComObject com)
        {
            com.FinalRelease();
        }
    }
}
