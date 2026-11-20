namespace Spar;

/// <summary>Stand-in for the browser's strict defaults and their opt-ins (plan section 3c), in <c>spar.Browser(b => …)</c>.</summary>
public sealed class BrowserOptions
{
    private readonly List<string> _allowed = [];

    internal BrowserOptions()
    {
    }

    internal IReadOnlyList<string> Allowed => _allowed;

    /// <summary>F12 and Inspect in every build.</summary>
    public BrowserOptions AllowDevTools() => Allow(nameof(AllowDevTools));

    /// <summary>The browser's own keys: reload, print, find, zoom, back and forward.</summary>
    public BrowserOptions AllowAcceleratorKeys() => Allow(nameof(AllowAcceleratorKeys));

    /// <summary>The offer to save passwords, and general autofill.</summary>
    public BrowserOptions AllowPasswordSaving() => Allow(nameof(AllowPasswordSaving));

    /// <summary>Downloads the page starts.</summary>
    public BrowserOptions AllowDownloads() => Allow(nameof(AllowDownloads));

    /// <summary>Back and forward to another document: mouse buttons, Alt+Left, swipe, history.back().</summary>
    public BrowserOptions AllowHistoryNavigation() => Allow(nameof(AllowHistoryNavigation));

    /// <summary>The browser's full context menu.</summary>
    public BrowserOptions AllowContextMenu() => Allow(nameof(AllowContextMenu));

    /// <summary>&lt;input type="file"&gt;'s picker and the File System Access pickers.</summary>
    public BrowserOptions AllowPagePicker() => Allow(nameof(AllowPagePicker));

    /// <summary>
    /// The environment variables that change the browser before it starts: WebView2's on Windows; on Linux the inspector
    /// servers, the sandbox switch and, in a Release build, JavaScriptCore's options.
    /// </summary>
    public BrowserOptions AllowBrowserEnvironmentOverrides() => Allow(nameof(AllowBrowserEnvironmentOverrides));

    /// <summary>Whether the browser draws with the GPU; Linux only for now, Windows ignores it.</summary>
    public BrowserOptions HardwareAcceleration(HardwareAcceleration acceleration)
    {
        _ = acceleration;
        return this;
    }

    private BrowserOptions Allow(string name)
    {
        _allowed.Add(name);
        return this;
    }
}

/// <summary>Whether the browser draws with the GPU, in <c>spar.Browser(b => b.HardwareAcceleration(…))</c>.</summary>
public enum HardwareAcceleration
{
    Auto,
    Always,
    Never,
}
