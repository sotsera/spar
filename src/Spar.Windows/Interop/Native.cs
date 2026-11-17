using System.Runtime.InteropServices;

namespace Spar.Windows.Interop;

/// <summary>The imports CsWin32 does not cover: the WebView2 loader and the memory stream for responses.</summary>
internal static partial class Native
{
    /// <summary>
    /// Creates the environment. Under NativeAOT the loader is linked into the executable (Spar.Sdk passes
    /// <c>WebView2LoaderStatic.lib</c> with <c>DirectPInvoke</c>); on CoreCLR it is <c>WebView2Loader.dll</c> beside it.
    /// </summary>
    [LibraryImport("WebView2Loader", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int CreateCoreWebView2EnvironmentWithOptions(
        string? browserExecutableFolder,
        string? userDataFolder,
        nint environmentOptions,
        ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler environmentCreatedHandler);

    /// <summary>A window's user data (<c>GWLP_USERDATA</c> is -21); 64-bit Windows exports the pointer-sized forms.</summary>
    [LibraryImport("user32", EntryPoint = "GetWindowLongPtrW")]
    public static partial nint GetWindowLongPtr(nint window, int index);

    [LibraryImport("user32", EntryPoint = "SetWindowLongPtrW")]
    public static partial nint SetWindowLongPtr(nint window, int index, nint value);

    /// <summary>An <c>IStream</c> over a copy of the bytes, with one reference the caller owns, or 0 when out of memory.</summary>
    [LibraryImport("shlwapi", EntryPoint = "SHCreateMemStream")]
    public static unsafe partial nint CreateMemoryStream(byte* initial, uint size);
}
