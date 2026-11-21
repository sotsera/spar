using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Spar.Hosting;
using Spar.Linux.Interop;

namespace Spar.Linux;

/// <summary>
/// The browser settings of plan section 3c on the view, with the app's opt-ins (<see cref="BrowserOptions"/>), on the
/// main loop before the first load, as on Windows:
/// <list type="bullet">
/// <item>developer extras (WebKit's inspector, reached through the menu's Inspect Element) off, unless allowed or in a
/// Development build, and GTK's own inspector keys (Ctrl+Shift+I, Ctrl+Shift+D) stopped outside one;</item>
/// <item>no clipboard access for script outside the permission gate (<c>javascript-can-access-clipboard</c>), no
/// windows opened by script, no access from <c>file://</c> documents, no top-level <c>data:</c> navigation; WebKit's
/// own defaults, set here so a change of default never reaches an app unseen;</item>
/// <item>downloads cancelled (<c>download-started</c> on the session);</item>
/// <item>the page's file picker cancelled (<c>run-file-chooser</c>);</item>
/// <item>the default context menu cut down to Cut, Copy, Paste and Select all in editable fields, and Inspect Element
/// where devtools are on; elsewhere no menu (<c>context-menu</c>).</item>
/// </list>
/// History navigation is cancelled by <see cref="PageGuard"/>. WebKitGTK has no password manager or form autofill, and
/// binds no browser key (reload, print, find, zoom and history keys all reached the page and did nothing else, M1c step
/// 6), so <c>AllowPasswordSaving()</c> and <c>AllowAcceleratorKeys()</c> change nothing on Linux. Each handler answers
/// from what is in memory.
/// </summary>
internal sealed class BrowserDefaults
{
    /// <summary>How many cancelled downloads and pickers are logged in a run; a page that loops on them is not logged further.</summary>
    private const int LoggedRefusals = 20;

    /// <summary>How many context menus a Development build describes in the log.</summary>
    private const int DescribedMenus = 20;

    private const int ActionCopy = 14;
    private const int ActionCut = 15;
    private const int ActionPaste = 16;
    private const int ActionSelectAll = 18;
    private const int ActionInspectElement = 31;

    /// <summary><c>GDK_SHIFT_MASK</c> and <c>GDK_CONTROL_MASK</c>.</summary>
    private const uint ShiftMask = 1 << 0;
    private const uint ControlMask = 1 << 2;

    /// <summary><c>GDK_KEY_I</c>, <c>GDK_KEY_i</c>, <c>GDK_KEY_D</c>, <c>GDK_KEY_d</c>.</summary>
    private const uint KeyI = 0x49;
    private const uint KeyLowerI = 0x69;
    private const uint KeyD = 0x44;
    private const uint KeyLowerD = 0x64;

    private readonly HostLog _log;
    private readonly BrowserOptions _options;
    private readonly bool _development;
    private readonly bool _automated;
    private int _downloads;
    private int _pickers;
    private int _menus;

    /// <summary>Devtools (the inspector): allowed in C#, or a Development build.</summary>
    public bool DevTools => _options.DevTools || _development;

    /// <param name="development">A Development build: devtools on, everything else as shipped.</param>
    /// <param name="automated">The automated mode (Development and Test only): no context menu is shown.</param>
    public BrowserDefaults(HostLog log, BrowserOptions options, bool development, bool automated)
    {
        _log = log;
        _options = options;
        _development = development;
        _automated = automated;
    }

    /// <summary>
    /// Sets the view's settings and logs them in one line with the time it took (loop thread, before the first load). A
    /// Development build logs the values read back from WebKit; a Production build logs the values set.
    /// </summary>
    public void Apply(nint view)
    {
        var started = Stopwatch.GetTimestamp();
        var settings = WebKit.webkit_web_view_get_settings(view);
        WebKit.webkit_settings_set_enable_developer_extras(settings, Flag(DevTools));
        WebKit.webkit_settings_set_javascript_can_access_clipboard(settings, 0);
        WebKit.webkit_settings_set_javascript_can_open_windows_automatically(settings, 0);
        WebKit.webkit_settings_set_allow_file_access_from_file_urls(settings, 0);
        WebKit.webkit_settings_set_allow_universal_access_from_file_urls(settings, 0);
        WebKit.webkit_settings_set_allow_top_navigation_to_data_urls(settings, 0);
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        // Only a Development build reads them back: the switch is constant, so Production carries no getter calls.
        var readBack = SparDevelopment.IsSupported;
        _log.Info(string.Concat(
            readBack ? "Browser settings read back (" : "Browser settings set (",
            elapsed.ToString("0.00", CultureInfo.InvariantCulture),
            " ms): devtools ", OnOff(readBack ? WebKit.webkit_settings_get_enable_developer_extras(settings) : Flag(DevTools)),
            ", GTK inspector keys ", _development ? "on" : "off",
            ", script clipboard ", OnOff(readBack ? WebKit.webkit_settings_get_javascript_can_access_clipboard(settings) : 0),
            ", script windows ", OnOff(readBack ? WebKit.webkit_settings_get_javascript_can_open_windows_automatically(settings) : 0),
            ", file access ", OnOff(readBack ? WebKit.webkit_settings_get_allow_file_access_from_file_urls(settings) : 0),
            ", universal file access ", OnOff(readBack ? WebKit.webkit_settings_get_allow_universal_access_from_file_urls(settings) : 0),
            ", data: navigation ", OnOff(readBack ? WebKit.webkit_settings_get_allow_top_navigation_to_data_urls(settings) : 0),
            "; menu ", _options.ContextMenu ? "full" : "cut down",
            ", downloads ", _options.Downloads ? "allowed" : "denied",
            ", picker ", _options.PagePicker ? "allowed" : "cancelled",
            ", history ", _options.HistoryNavigation ? "allowed" : "cancelled", "."));
    }

    /// <summary>
    /// <c>download-started</c> on the session (loop thread): cancelled at once unless the app allowed downloads, so no
    /// file is written. A Development build names the address in the log.
    /// </summary>
    public void OnDownloadStarted(nint download)
    {
        if (_options.Downloads)
        {
            return;
        }

        WebKit.webkit_download_cancel(download);
        var address = SparDevelopment.IsSupported
            ? " of " + Shorten(Marshal.PtrToStringUTF8(WebKit.webkit_uri_request_get_uri(WebKit.webkit_download_get_request(download))) ?? "")
            : "";
        _downloads++;
        if (_downloads <= LoggedRefusals)
        {
            _log.Warn("A download" + address + " was cancelled (AllowDownloads allows it).");
        }
    }

    /// <summary>
    /// <c>run-file-chooser</c> (loop thread): true when the request was answered here, with no files, so no picker
    /// opens; false leaves it to WebKit, which shows the system's picker (<c>AllowPagePicker()</c>).
    /// </summary>
    public bool OnFileChooser(nint request)
    {
        if (_options.PagePicker)
        {
            return false;
        }

        WebKit.webkit_file_chooser_request_cancel(request);
        _pickers++;
        if (_pickers <= LoggedRefusals)
        {
            _log.Warn("The page's file picker was cancelled (AllowPagePicker allows it).");
        }

        return true;
    }

    /// <summary>
    /// <c>context-menu</c> (loop thread): true when no menu is shown. Unless the app allowed the full menu, an editable
    /// field keeps Cut, Copy, Paste and Select all (and Inspect Element where devtools are on); anywhere else only
    /// Inspect Element where devtools are on, or no menu at all. In the automated mode the menu is cut down the same
    /// way, then not shown.
    /// </summary>
    public bool OnContextMenu(nint menu, nint hitTest)
    {
        if (_options.ContextMenu)
        {
            return _automated;
        }

        var editable = WebKit.webkit_hit_test_result_context_is_editable(hitTest) != 0;

        // Only a Development build describes its menus: the switch is constant, so Production has no such line.
        var describe = SparDevelopment.IsSupported && _menus < DescribedMenus;
        var kept = "";
        var removed = 0;
        for (var index = (int)WebKit.webkit_context_menu_get_n_items(menu) - 1; index >= 0; index--)
        {
            var item = WebKit.webkit_context_menu_get_item_at_position(menu, (uint)index);
            var action = WebKit.webkit_context_menu_item_get_stock_action(item);
            if (KeptAction(action, editable, DevTools))
            {
                kept = describe ? ActionName(action) + " " + kept : kept;
                continue;
            }

            WebKit.webkit_context_menu_remove(menu, item);
            removed++;
        }

        var hidden = WebKit.webkit_context_menu_get_n_items(menu) == 0 || _automated;
        if (describe)
        {
            _menus++;
            _log.Info($"Context menu ({(editable ? "editable" : "not editable")}): kept [{kept.TrimEnd()}], removed {removed}{(hidden ? ", not shown" : "")}.");
        }

        return hidden;
    }

    /// <summary>
    /// Keeps GTK's own inspector closed outside a Development build (loop thread, on the main window before it shows):
    /// GTK 4 opens it on Ctrl+Shift+I and Ctrl+Shift+D in every app unless the user's settings say otherwise
    /// (<c>org.gtk.gtk4.Settings.Debug</c>, on by default), and it can read and change every object of the app's
    /// process, the view's settings included. A key controller in the window's bubble phase, which runs before GTK's
    /// own binding there, stops those two keys after the page has had them.
    /// </summary>
    public unsafe void KeepGtkInspectorClosed(nint window)
    {
        if (_development)
        {
            return;
        }

        var keys = Gtk.gtk_event_controller_key_new();
        _ = GLib.g_signal_connect_data(keys, "key-pressed", (delegate* unmanaged<nint, uint, uint, uint, nint, int>)&OnKeyPressed, 0, 0, 0);
        Gtk.gtk_widget_add_controller(window, keys);
    }

    /// <summary>Whether a key is GTK's inspector binding: Ctrl+Shift+I or Ctrl+Shift+D, by keyval and modifier state.</summary>
    internal static bool IsGtkInspectorKey(uint keyval, uint state) =>
        (state & (ShiftMask | ControlMask)) == (ShiftMask | ControlMask) && keyval is KeyI or KeyLowerI or KeyD or KeyLowerD;

    /// <summary>The context menu actions kept without <c>AllowContextMenu()</c>, by WebKit's stock action.</summary>
    internal static bool KeptAction(int action, bool editable, bool devTools) =>
        (editable && action is ActionCut or ActionCopy or ActionPaste or ActionSelectAll)
        || (devTools && action == ActionInspectElement);

    private static string ActionName(int action) => action switch
    {
        ActionCut => "cut",
        ActionCopy => "copy",
        ActionPaste => "paste",
        ActionSelectAll => "selectAll",
        ActionInspectElement => "inspectElement",
        _ => action.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary><c>key-pressed</c>: TRUE stops GTK's inspector keys; every other key goes on. No user data.</summary>
    [UnmanagedCallersOnly]
    private static int OnKeyPressed(nint controller, uint keyval, uint keycode, uint state, nint data) =>
        IsGtkInspectorKey(keyval, state) ? 1 : 0;

    private static int Flag(bool on) => on ? 1 : 0;

    private static string OnOff(int value) => value != 0 ? "on" : "off";

    private static string Shorten(string text) => text.Length <= 200 ? text : string.Concat(text.AsSpan(0, 200), "…");
}
