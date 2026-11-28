using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Spar.Windows.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Spar.Windows;

/// <summary>
/// How UI Automation sees a window of a page-drawn title bar (<c>docs/evidence/windows-sandbox-checks</c>, native UIA
/// client): for the app's window, everything Windows' own provider says, without the non-client part, so the native
/// title bar the frame no longer has (a "System" menu and empty Minimize, Maximize and Close buttons) is gone and the
/// page's buttons are the ones a screen reader finds; for the host's caption window and WebView2's drag-region windows
/// (<paramref name="hidden"/>), out of the control and content views, so they add no unnamed panes. Everything else
/// comes from <c>UiaHostProviderFromHwnd</c>. Called by UI Automation on the window's UI thread.
/// </summary>
[GeneratedComClass]
internal sealed partial class WindowProvider(nint window, bool hidden) : IRawElementProviderSimple
{
    public int GetProviderOptions(out int options)
    {
        options = Automation.ServerSideProvider | (hidden ? 0 : Automation.RefuseNonClientSupport);
        return 0;
    }

    public int GetPatternProvider(int patternId, out nint provider)
    {
        provider = 0;
        return 0;
    }

    public int GetPropertyValue(int propertyId, out Variant value)
    {
        value = default;
        if (hidden && propertyId is Automation.IsControlElementPropertyId or Automation.IsContentElementPropertyId)
        {
            // VARIANT_FALSE.
            value.Type = Variant.Bool;
        }

        return 0;
    }

    public int GetHostRawElementProvider(out nint provider) => Automation.UiaHostProviderFromHwnd(window, out provider);
}

/// <summary>
/// WebView2's drag-region windows (one transparent <c>Chrome_WidgetWin_0</c> per rectangle of the page's drag areas,
/// in the app's process, on the window's UI thread) answer UI Automation with a hidden <see cref="WindowProvider"/>:
/// each is subclassed once, when it is first found below the window, and lets go of its provider when it is
/// destroyed. WebView2 makes and remakes them as the page's drag areas change, so the window looks again after each
/// new layout. UI thread.
/// </summary>
internal static unsafe class DragRegionWindows
{
    /// <summary>The subclass's id: one per procedure, which identifies Spar's subclass on a window.</summary>
    private const nuint SubclassId = 0x5350_4152;

    /// <summary>Subclasses the drag-region windows below <paramref name="window"/> not subclassed yet; the count found.</summary>
    public static int Hide(HWND window, HWND sink)
    {
        var found = new List<HWND>();
        var handle = GCHandle.Alloc(found);
        try
        {
            _ = PInvoke.EnumChildWindows(window, &Collect, (LPARAM)GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }

        var thread = PInvoke.GetCurrentThreadId();
        var count = 0;
        foreach (var child in found)
        {
            if (child == sink || !IsDragRegion(child) || PInvoke.GetWindowThreadProcessId(child, null) != thread)
            {
                continue;
            }

            count++;
            nuint existing;
            if (PInvoke.GetWindowSubclass(child, &Subclass, SubclassId, &existing))
            {
                continue;
            }

            var provider = GCHandle.Alloc(new WindowProvider((nint)child.Value, hidden: true));
            if (!PInvoke.SetWindowSubclass(child, &Subclass, SubclassId, (nuint)GCHandle.ToIntPtr(provider)))
            {
                provider.Free();
            }
        }

        return count;
    }

    /// <summary>A drag-region window: WebView2's window class, and transparent to the pointer's painting.</summary>
    private static bool IsDragRegion(HWND child)
    {
        const string ClassName = "Chrome_WidgetWin_0";
        var name = stackalloc char[ClassName.Length + 2];
        var length = PInvoke.GetClassName(child, name, ClassName.Length + 2);
        if (length != ClassName.Length || !new ReadOnlySpan<char>(name, length).SequenceEqual(ClassName))
        {
            return false;
        }

        var extended = (WINDOW_EX_STYLE)PInvoke.GetWindowLong(child, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        return extended.HasFlag(WINDOW_EX_STYLE.WS_EX_TRANSPARENT);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static BOOL Collect(HWND child, LPARAM lParam)
    {
        if (GCHandle.FromIntPtr(lParam.Value).Target is List<HWND> found)
        {
            found.Add(child);
        }

        return true;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static LRESULT Subclass(HWND window, uint message, WPARAM wParam, LPARAM lParam, nuint id, nuint data)
    {
        try
        {
            var provider = GCHandle.FromIntPtr((nint)data);
            if (message == PInvoke.WM_GETOBJECT && (int)lParam.Value == Automation.RootObjectId && provider.Target is WindowProvider hidden)
            {
                return new LRESULT(Automation.UiaReturnRawElementProvider((nint)window.Value, wParam.Value, lParam.Value, hidden));
            }

            if (message == PInvoke.WM_NCDESTROY)
            {
                _ = PInvoke.RemoveWindowSubclass(window, &Subclass, id);
                _ = Automation.UiaReturnRawElementProvider((nint)window.Value, 0, 0, null);
                provider.Free();
            }
        }
        catch
        {
            // Nothing may unwind into WebView2's window procedure; the window goes on as it would without Spar.
        }

        return PInvoke.DefSubclassProc(window, message, wParam, lParam);
    }
}
