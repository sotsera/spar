using Spar.Windows.Interop;

namespace Spar.Windows;

/// <summary>
/// The page's zoom (decision "The window design, from the windows spike", item 3), and the drag areas of a page-drawn
/// title bar (<see cref="DragRegions"/>). WebView2 zooms on Ctrl+wheel and Ctrl+plus, minus and 0 itself, and the host
/// keeps every factor within the window's limits, starts at the remembered one, and reports each change
/// (<see cref="ZoomChanged"/>), from which the window saves it and tells its page. A window whose zoom is off has the
/// browser's zoom control turned off. UI thread.
/// </summary>
internal sealed partial class BrowserView
{
    private double _zoom = 1;

    /// <summary>The window's zoom: whether it is on, its limits, and the factor it starts at.</summary>
    public (bool Enabled, double Min, double Max, double Start) Zoom { get; init; } = (true, 0.25, 5, 1);

    /// <summary>The page's zoom changed, to a factor within the limits.</summary>
    public Action<double>? ZoomChanged { get; init; }

    /// <summary>Whether CSS <c>app-region</c> marks drag areas: on for a page-drawn title bar (<c>TitleBar.Custom</c>).</summary>
    public bool DragRegions { get; init; }

    /// <summary>The page's zoom now; the factor it will start at before the controller exists.</summary>
    public double ZoomFactor => _controller is null ? Math.Clamp(Zoom.Start, Zoom.Min, Zoom.Max) : _zoom;

    /// <summary>
    /// Sets the page's zoom, kept within the limits; nothing while zoom is off or before the controller exists. WebView2
    /// raises <c>ZoomFactorChanged</c> only for the user's own zoom, not for <c>put_ZoomFactor</c>, so the change is
    /// reported here.
    /// </summary>
    public void SetZoom(double factor)
    {
        if (_controller is { } controller && Zoom.Enabled)
        {
            controller.PutZoomFactor(Math.Clamp(factor, Zoom.Min, Zoom.Max));
            OnZoomFactorChanged(controller);
        }
    }

    /// <summary>The new controller's zoom: the start factor, the browser's control off when zoom is, and its changes followed.</summary>
    private void StartZoom(int generation, ICoreWebView2Controller controller, ICoreWebView2_22 webView)
    {
        if (!Zoom.Enabled)
        {
            Com.Take<ICoreWebView2Settings8>(webView.GetSettings()).PutIsZoomControlEnabled(0);
            return;
        }

        _zoom = Math.Clamp(Zoom.Start, Zoom.Min, Zoom.Max);
        if (_zoom != 1)
        {
            controller.PutZoomFactor(_zoom);
        }

        var changed = Handler(generation, "ZoomFactorChanged", _ => OnZoomFactorChanged(controller));
        Subscribe(changed, controller.AddZoomFactorChanged(changed), controller.RemoveZoomFactorChanged);
    }

    /// <summary>
    /// A page-drawn title bar's drag areas: CSS <c>app-region</c> marks them, from the next navigation on, and WebView2
    /// moves the window from them, with double-click and the system menu (plan 3, "Title bar drawn by the page").
    /// </summary>
    private void StartDragRegions(ICoreWebView2_22 webView)
    {
        if (!DragRegions)
        {
            return;
        }

        var settings = Com.Take<ICoreWebView2Settings9>(webView.GetSettings());
        settings.PutIsNonClientRegionSupportEnabled(1);
        Com.Release(settings);
    }

    /// <summary>A factor outside the limits is set back to the nearest one, which raises the event again.</summary>
    private void OnZoomFactorChanged(ICoreWebView2Controller controller)
    {
        var factor = controller.GetZoomFactor();
        var kept = Math.Clamp(factor, Zoom.Min, Zoom.Max);
        if (kept != factor)
        {
            controller.PutZoomFactor(kept);
            return;
        }

        _zoom = factor;
        ZoomChanged?.Invoke(factor);
    }
}
