using Spar.AppWindows;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Spar.Windows;

/// <summary>
/// The window's remembered place (decision "M3d's decide-first questions", item 5), from <c>windows.json</c> through the
/// app's <see cref="PlacementStore"/>: where it opens, and what it keeps of each move, resize or maximize. A modal opens
/// centred on its opener, at its remembered size; another window of a kind with several opens offset from the last one
/// still open; a remembered place whose corner is on no monitor opens centred on the primary one. A test window keeps
/// no position, since it stays offscreen.
/// </summary>
internal sealed partial class AppWindow
{
    /// <summary>How far a new window of a kind with several opens from the last one, in CSS pixels.</summary>
    private const int CascadeOffset = 32;

    private PlacementStore? Placements => _windows?.Placements;

    /// <summary>Where the window opens, and whether maximized, before any test or development rule.</summary>
    private (RECT Bounds, bool Maximized) StartBounds(AppWindow? modalOwner)
    {
        var saved = Placements?.Get(Options);
        var size = saved is { } placement ? (placement.Width, placement.Height) : Options.InitialSize;
        if (modalOwner is not null)
        {
            return (WindowPlacement.CentredOn(modalOwner.Handle, size, CustomFrame), false);
        }

        if (saved is { HasPosition: true } && WindowPlacement.Restored(saved.Left!.Value, saved.Top!.Value, size, CustomFrame) is { } restored)
        {
            return (restored, saved.Maximized);
        }

        if (_opened is { } opened && LastOfKind(opened.Kind) is { } last)
        {
            return (WindowPlacement.Cascaded(last.Handle, size, CascadeOffset, CustomFrame), false);
        }

        return (WindowPlacement.Initial(size, CustomFrame), saved?.Maximized ?? false);
    }

    /// <summary>The window of <paramref name="kind"/> opened last that is still open, other than this one.</summary>
    private AppWindow? LastOfKind(string kind) =>
        Family?.Others.LastOrDefault(other => other != this && other._opened?.Kind == kind && !other.IsClosing && !other._window.IsNull);

    /// <summary>
    /// The window moved, resized, or was maximized or restored (<c>WM_WINDOWPOSCHANGED</c>, UI thread): its place, as its
    /// kind keeps it, goes to the store, which writes once the changes settle. A minimized window keeps the place it had;
    /// a maximized one keeps the normal place it had before. A closing window keeps what it had: its view is going.
    /// </summary>
    private void PlacementChanged()
    {
        if (Placements is not { } store || _window.IsNull || _destroyed || _state == State.Closing || PInvoke.IsIconic(_window))
        {
            return;
        }

        var zoom = CurrentZoom();
        if (PInvoke.IsZoomed(_window))
        {
            var before = store.Get(Options);
            var (clientWidth, clientHeight) = WindowPlacement.ClientSize(_window);
            store.Update(Options, new SavedPlacement(before?.Left, before?.Top, before?.Width ?? clientWidth, before?.Height ?? clientHeight, true, zoom));
            return;
        }

        var (width, height) = WindowPlacement.ClientSize(_window);
        int? left = null;
        int? top = null;
        if (!_offscreen && PInvoke.GetWindowRect(_window, out var frame))
        {
            left = frame.left;
            top = frame.top;
        }

        store.Update(Options, new SavedPlacement(left, top, width, height, false, zoom));
    }

    /// <summary>The page's zoom now; the remembered one before the view exists.</summary>
    private double CurrentZoom() => _view?.ZoomFactor ?? Placements?.Get(Options)?.Zoom ?? 1;

    /// <summary>
    /// The page's zoom changed, by the user or by <c>zoom.set</c> (UI thread): it is remembered with the window's place,
    /// and the page hears it.
    /// </summary>
    private void OnZoomChanged(double factor)
    {
        if (_files is { } window)
        {
            window.Zoom = factor;
        }

        PlacementChanged();

        // The caption window over a page-drawn title bar scales with the zoom, even when the page's layout stays the same.
        UpdateCaption();
        if (_bridge.Task.IsCompletedSuccessfully)
        {
            _bridge.Task.Result.SendZoom(factor);
        }
    }
}
