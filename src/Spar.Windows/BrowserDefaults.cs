using System.Diagnostics;
using System.Globalization;
using Spar.Hosting;
using Spar.Windows.Interop;
using Windows.Win32;

namespace Spar.Windows;

/// <summary>
/// The browser settings of plan section 3c on each WebView, with the app's opt-ins (<see cref="BrowserOptions"/>), all
/// applied on the UI thread before the first navigation:
/// <list type="bullet">
/// <item>devtools (F12, Inspect) off, unless allowed or in a Development build;</item>
/// <item>the browser's accelerator keys off; where devtools are on, only their keys (and in Development the reload
/// keys) reach the browser, through <c>AcceleratorKeyPressed</c>, since the setting is one switch for every key;</item>
/// <item>password saving and general autofill off, swipe navigation off, host objects never allowed, and SmartScreen's
/// reputation check off while the navigation lock keeps the page on the app's origin (decision "SmartScreen default");</item>
/// <item>downloads cancelled (<c>DownloadStarting</c>);</item>
/// <item>the default context menu cut down to Cut, Copy, Paste and Select all in editable fields, and Inspect where
/// devtools are on; elsewhere no menu (<c>ContextMenuRequested</c>).</item>
/// </list>
/// History navigation is cancelled by <see cref="PageGuard"/>, and the page's pickers by <see cref="PagePicker"/> and
/// <see cref="BrowserEnvironment"/>. Each handler answers from what is in memory.
/// </summary>
internal sealed class BrowserDefaults
{
    /// <summary>How many cancelled downloads are logged in a run; a page that loops on them is not logged further.</summary>
    private const int LoggedDownloads = 20;

    /// <summary>How many context menus a Development build describes in the log.</summary>
    private const int DescribedMenus = 20;

    private const uint F5 = 0x74;
    private const uint F12 = 0x7B;
    private const uint KeyC = 0x43;
    private const uint KeyI = 0x49;
    private const uint KeyJ = 0x4A;
    private const uint KeyR = 0x52;
    private const int Shift = 0x10;
    private const int Control = 0x11;
    private const int Menu = 0x12;

    private readonly HostLog _log;
    private readonly BrowserOptions _options;
    private readonly bool _development;
    private readonly bool _automated;
    private int _downloads;
    private int _menus;

    /// <summary>Devtools (F12 and Inspect): allowed in C#, or a Development build.</summary>
    public bool DevTools => _options.DevTools || _development;

    /// <summary>Only some keys reach the browser: devtools are on, but not every browser key.</summary>
    public bool FiltersKeys => DevTools && !_options.AcceleratorKeys;

    /// <summary>The default context menu is cut down unless the app allowed all of it.</summary>
    public bool FiltersContextMenu => !_options.ContextMenu;

    /// <summary>Downloads are cancelled unless the app allowed them.</summary>
    public bool DeniesDownloads => !_options.Downloads;

    /// <summary><c>&lt;input type="file"&gt;</c>'s picker is intercepted unless the app allowed the page's pickers.</summary>
    public bool BlocksPagePicker => !_options.PagePicker;

    /// <summary>The browser arguments for the environment options (<see cref="BrowserEnvironment.Arguments"/>).</summary>
    public string? BrowserArguments => BrowserEnvironment.Arguments(_options.PagePicker);

    /// <param name="development">A Development build: devtools and the reload keys on, everything else as shipped.</param>
    /// <param name="automated">The automated mode (Development and Test only): no context menu is shown on the desktop.</param>
    public BrowserDefaults(HostLog log, BrowserOptions options, bool development, bool automated)
    {
        _log = log;
        _options = options;
        _development = development;
        _automated = automated;
    }

    /// <summary>
    /// Sets the WebView's settings and logs them in one line with the time it took. A Development build logs the values
    /// read back from the browser, which runs the same calls; a Production build logs the values set, so the getters'
    /// code stays out of its image (the Notebook's size budget). Every interface is in Spar's minimum Runtime, so a
    /// missing one fails the start as a Runtime that is too old (exit 4), never a start with the defaults off.
    /// </summary>
    public void Apply(ICoreWebView2_22 webView)
    {
        var started = Stopwatch.GetTimestamp();
        var settings = Com.Take<ICoreWebView2Settings8>(webView.GetSettings());
        try
        {
            settings.PutAreDevToolsEnabled(Flag(DevTools));
            settings.PutAreBrowserAcceleratorKeysEnabled(Flag(_options.AcceleratorKeys || DevTools));
            settings.PutIsPasswordAutosaveEnabled(Flag(_options.PasswordSaving));
            settings.PutIsGeneralAutofillEnabled(Flag(_options.PasswordSaving));
            settings.PutIsSwipeNavigationEnabled(Flag(_options.HistoryNavigation));
            settings.PutAreHostObjectsAllowed(0);
            settings.PutAreDefaultContextMenusEnabled(1);
            settings.PutIsReputationCheckingRequired(0);
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

            var readBack = SparDevelopment.IsSupported;
            _log.Info(string.Concat(
                readBack ? "Browser settings read back (" : "Browser settings set (",
                elapsed.ToString("0.00", CultureInfo.InvariantCulture),
                " ms): devtools ", OnOff(readBack ? settings.GetAreDevToolsEnabled() : Flag(DevTools)),
                ", keys ", OnOff(readBack ? settings.GetAreBrowserAcceleratorKeysEnabled() : Flag(_options.AcceleratorKeys || DevTools)),
                FiltersKeys ? " (filtered)" : "",
                ", password autosave ", OnOff(readBack ? settings.GetIsPasswordAutosaveEnabled() : Flag(_options.PasswordSaving)),
                ", autofill ", OnOff(readBack ? settings.GetIsGeneralAutofillEnabled() : Flag(_options.PasswordSaving)),
                ", swipe ", OnOff(readBack ? settings.GetIsSwipeNavigationEnabled() : Flag(_options.HistoryNavigation)),
                ", host objects ", OnOff(readBack ? settings.GetAreHostObjectsAllowed() : 0),
                ", reputation check ", OnOff(readBack ? settings.GetIsReputationCheckingRequired() : 0),
                "; menu ", FiltersContextMenu ? "cut down" : "full",
                ", downloads ", DeniesDownloads ? "denied" : "allowed",
                ", history ", _options.HistoryNavigation ? "allowed" : "cancelled", "."));
        }
        finally
        {
            Com.Release(settings);
        }
    }

    /// <summary>
    /// <c>DownloadStarting</c>: cancelled and handled, so neither a file nor the download bar appears. A Development
    /// build names the address in the log.
    /// </summary>
    public void OnDownloadStarting(nint argsPointer)
    {
        var args = Com.Borrow<ICoreWebView2DownloadStartingEventArgs>(argsPointer);
        var uri = "";
        try
        {
            args.PutCancel(1);
            args.PutHandled(1);
            if (SparDevelopment.IsSupported)
            {
                uri = " of " + Shorten(DownloadUri(args));
            }
        }
        finally
        {
            Com.Release(args);
        }

        _downloads++;
        if (_downloads <= LoggedDownloads)
        {
            _log.Warn("A download" + uri + " was cancelled (AllowDownloads allows it).");
        }
    }

    /// <summary>
    /// <c>ContextMenuRequested</c>: an editable field keeps Cut, Copy, Paste and Select all (and Inspect where devtools
    /// are on); anywhere else only Inspect where devtools are on, or no menu at all. In the automated mode the menu is
    /// cut down the same way, then not shown, so nothing appears on the desktop of an unattended run.
    /// </summary>
    public void OnContextMenuRequested(nint argsPointer)
    {
        var args = Com.Borrow<ICoreWebView2ContextMenuRequestedEventArgs>(argsPointer);
        try
        {
            var editable = IsEditable(args);

            // Only a Development build describes its menus: the switch is constant, so Production has no such line.
            var describe = SparDevelopment.IsSupported && _menus < DescribedMenus;
            var kept = "";
            var removed = 0;
            bool hidden;
            var items = Com.Take<ICoreWebView2ContextMenuItemCollection>(args.GetMenuItems());
            try
            {
                for (var index = (int)items.GetCount() - 1; index >= 0; index--)
                {
                    var name = ItemName(items, (uint)index);
                    if (KeptItem(name, editable, DevTools))
                    {
                        kept = describe ? name + " " + kept : kept;
                        continue;
                    }

                    items.RemoveValueAtIndex((uint)index);
                    removed++;
                }

                hidden = items.GetCount() == 0 || _automated;
            }
            finally
            {
                Com.Release(items);
            }

            if (hidden)
            {
                args.PutHandled(1);
            }

            if (describe)
            {
                _menus++;
                _log.Info($"Context menu ({(editable ? "editable" : "not editable")}): kept [{kept.TrimEnd()}], removed {removed}{(hidden ? ", not shown" : "")}.");
            }
        }
        finally
        {
            Com.Release(args);
        }
    }

    /// <summary>
    /// <c>AcceleratorKeyPressed</c> on the controller, where devtools are on but not every browser key: the browser
    /// acts only on the devtools keys (F12, Ctrl+Shift+I, J, C) and, in Development, the reload keys (F5, Ctrl+R, with
    /// Shift or Ctrl for a reload without the cache). The page still receives every key.
    /// </summary>
    public void OnAcceleratorKeyPressed(nint argsPointer)
    {
        var args = Com.Borrow<ICoreWebView2AcceleratorKeyPressedEventArgs2>(argsPointer);
        try
        {
            if (!BrowserKeyAllowed(args.GetVirtualKey(), Down(Control), Down(Shift), Down(Menu), _development))
            {
                args.PutIsBrowserAcceleratorKeyEnabled(0);
            }
        }
        finally
        {
            Com.Release(args);
        }
    }

    /// <summary>Whether the browser may act on a key while only devtools (and in Development the reload keys) are allowed.</summary>
    internal static bool BrowserKeyAllowed(uint key, bool control, bool shift, bool alt, bool development)
    {
        if (alt)
        {
            return false;
        }

        var devTools = (key == F12 && !control && !shift) || (control && shift && key is KeyI or KeyJ or KeyC);
        var reload = development && (key == F5 || (control && key == KeyR));
        return devTools || reload;
    }

    /// <summary>The context menu items kept without <c>AllowContextMenu()</c>, by WebView2's item names.</summary>
    internal static bool KeptItem(string name, bool editable, bool devTools) =>
        (editable && name is "cut" or "copy" or "paste" or "selectAll") || (devTools && name == "inspectElement");

    private static bool IsEditable(ICoreWebView2ContextMenuRequestedEventArgs args)
    {
        var target = Com.Take<ICoreWebView2ContextMenuTarget>(args.GetContextMenuTarget());
        try
        {
            return target.GetIsEditable() != 0;
        }
        finally
        {
            Com.Release(target);
        }
    }

    private static string DownloadUri(ICoreWebView2DownloadStartingEventArgs args)
    {
        var operation = Com.Take<ICoreWebView2DownloadOperation>(args.GetDownloadOperation());
        try
        {
            return operation.GetUri();
        }
        finally
        {
            Com.Release(operation);
        }
    }

    private static string ItemName(ICoreWebView2ContextMenuItemCollection items, uint index)
    {
        var item = Com.Take<ICoreWebView2ContextMenuItem>(items.GetValueAtIndex(index));
        try
        {
            return item.GetName();
        }
        finally
        {
            Com.Release(item);
        }
    }

    private static bool Down(int virtualKey) => PInvoke.GetKeyState(virtualKey) < 0;

    private static int Flag(bool on) => on ? 1 : 0;

    private static string OnOff(int value) => value != 0 ? "on" : "off";

    private static string Shorten(string text) => text.Length <= 200 ? text : string.Concat(text.AsSpan(0, 200), "…");
}
