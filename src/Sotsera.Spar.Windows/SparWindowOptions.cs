using Sotsera.Spar.Bridge;

namespace Sotsera.Spar.Windows;

/// <summary>Initial, trusted local document and persistent browser profile for one Windows/x64 window.</summary>
public sealed class SparWindowOptions
{
#if DEBUG
    /// <summary>Observe a frame opportunity after the first completed typed call, without application test exports.</summary>
    public bool EnableDevelopmentReadiness { get; init; }

    /// <summary>Explicit opt-in to bounded inspection. This property and its implementation do not exist in Release.</summary>
    public bool EnableDevelopmentInspection { get; init; }
#endif

    /// <summary>Optional owner-authorized loopback content. Local HTML is used when absent.</summary>
    public SparLoopbackContent? LoopbackContent { get; init; }

    /// <summary>Packaged production assets, mutually exclusive with loopback content. Must match the bridge contract.</summary>
    public SparAssetBundle? Assets { get; init; }

    public SparBridge? Bridge
    {
        get; init;
    }

    public Action<SparBridgeEvent>? BridgeEvent
    {
        get; init;
    }

    public required string Title
    {
        get; init;
    }

    public string Html
    {
        get; init;
    } = "";

    /// <summary>An application-owned directory. Spar leases it exclusively and does not delete browser data.</summary>
    public required string UserDataFolder
    {
        get; init;
    }

    public int Width { get; init; } = 1000;

    /// <summary>Optional fixed WebView2 Runtime directory. Null selects the installed runtime.</summary>
    public string? BrowserRuntimeFolder
    {
        get;
        init;
    }

    public int Height { get; init; } = 700;

    /// <summary>Called on the UI thread before WebView initialization. This does not establish rendering readiness.</summary>
    public Action<SparWindow>? Created
    {
        get; init;
    }

    /// <summary>Called after successful navigation. Visible rendering and bridge readiness are separate concerns.</summary>
    public Action<SparWindow>? DocumentLoaded
    {
        get; init;
    }
}
