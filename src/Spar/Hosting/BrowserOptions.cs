namespace Spar;

/// <summary>
/// The browser's strict defaults and their opt-ins (plan section 3c, level 2), in <c>spar.Browser(b => …)</c>. Every
/// window shares one browser profile, so each opt-in is for the whole app. A Production build has all of them off; a
/// Development build has the same defaults except the devtools (F12, Inspect) and, on Windows, the reload keys (F5,
/// Ctrl+R), and on Linux it keeps JavaScriptCore's options from the environment, so a blocked download or picker shows
/// while developing, not after shipping. Spar logs every active opt-in at start-up.
/// </summary>
public sealed class BrowserOptions
{
    internal BrowserOptions()
    {
    }

    internal bool DevTools { get; private set; }

    internal bool AcceleratorKeys { get; private set; }

    internal bool PasswordSaving { get; private set; }

    internal bool Downloads { get; private set; }

    internal bool HistoryNavigation { get; private set; }

    internal bool ContextMenu { get; private set; }

    internal bool PagePicker { get; private set; }

    internal bool EnvironmentOverrides { get; private set; }

    internal HardwareAcceleration Acceleration { get; private set; } = Spar.HardwareAcceleration.Auto;

    /// <summary>
    /// The browser's devtools in every build: F12, Ctrl+Shift+I and the context menu's Inspect (on Linux, WebKitGTK's
    /// inspector through the menu's Inspect Element; GTK's own inspector stays closed outside a Debug build).
    /// <para><b>Risk:</b> anyone at the keyboard can read and change the page, its storage and every value it holds,
    /// and call the app's commands as the page does, also on a shared or kiosk machine. Devtools do not open a remote
    /// port, and the bridge's checks still apply, but the page is no longer only what you shipped.</para>
    /// <para><b>Reasonable for:</b> an internal tool whose users are its developers, or a support build.</para>
    /// <para><b>Safer:</b> a Debug build, which has devtools already, or the app's own logs (<c>spar.Diagnostics</c>).</para>
    /// </summary>
    public BrowserOptions AllowDevTools()
    {
        DevTools = true;
        return this;
    }

    /// <summary>
    /// The browser's own keys: reload (F5, Ctrl+R), print (Ctrl+P), find (Ctrl+F), zoom (Ctrl+plus and minus), back and
    /// forward (Alt+Left, Alt+Right) and the devtools keys when devtools are allowed. Editing keys and the page's own
    /// shortcuts work without this. WebKitGTK binds none of these keys itself, so on Linux this changes nothing.
    /// <para><b>Risk:</b> a reload or a history key throws away what the page has not saved and restarts it in the
    /// middle of the user's work, and print and find show browser screens that are not part of the app.</para>
    /// <para><b>Reasonable for:</b> an app that is mostly a document viewer and wants the browser's find and zoom.</para>
    /// <para><b>Safer:</b> handle the keys you need in the page and call your own code, such as a reload that saves first.</para>
    /// </summary>
    public BrowserOptions AllowAcceleratorKeys()
    {
        AcceleratorKeys = true;
        return this;
    }

    /// <summary>
    /// The browser's offer to save passwords, and its general autofill of names, addresses and other form fields.
    /// WebKitGTK has neither, so on Linux this changes nothing.
    /// <para><b>Risk:</b> passwords typed into the page, such as a database's, are kept in the app's browser profile,
    /// where any program running as the user can read them, and offered again in other fields of the page.</para>
    /// <para><b>Reasonable for:</b> an app whose page signs in to a web service the user also uses in a browser.</para>
    /// <para><b>Safer:</b> keep secrets in the system's credential store through the app's backend, and never in the page.</para>
    /// </summary>
    public BrowserOptions AllowPasswordSaving()
    {
        PasswordSaving = true;
        return this;
    }

    /// <summary>
    /// Downloads started by the page: a link with <c>download</c>, a file served as an attachment, or a blob the page
    /// builds. They are saved to the user's Downloads folder with the browser's download bar.
    /// <para><b>Risk:</b> the page writes files of any name and content to the user's disk without the app's code
    /// seeing them, and a compromised page can drop an executable where the user will find it.</para>
    /// <para><b>Reasonable for:</b> an app that shows web content whose downloads the user expects.</para>
    /// <para><b>Safer:</b> a command that saves through the native save dialog in the backend, which chooses the name
    /// and checks the content.</para>
    /// </summary>
    public BrowserOptions AllowDownloads()
    {
        Downloads = true;
        return this;
    }

    /// <summary>
    /// History navigation: the mouse's back and forward buttons, Alt+Left and Alt+Right (with
    /// <see cref="AllowAcceleratorKeys"/>), the touchpad's swipe, and the page's own <c>history.back()</c> to an earlier
    /// document. Moves within one document (a router's <c>pushState</c> entries) work without this.
    /// <para><b>Risk:</b> a stray click on a mouse's side button leaves the page in the middle of the user's work,
    /// losing what it has not saved, and returns to a document the app already left.</para>
    /// <para><b>Reasonable for:</b> an app made of several documents that the user browses like a site.</para>
    /// <para><b>Safer:</b> a single-page app whose router handles its own back and forward.</para>
    /// </summary>
    public BrowserOptions AllowHistoryNavigation()
    {
        HistoryNavigation = true;
        return this;
    }

    /// <summary>
    /// The browser's full context menu: back, reload, save as, print, open in a new window and the other browser
    /// items. Without it, editable fields keep Cut, Copy, Paste and Select all, and elsewhere no menu shows.
    /// <para><b>Risk:</b> "Save as", "Print" and the link and image items let the user take the page's content and
    /// addresses outside the app's own commands, and reload and back lose unsaved work.</para>
    /// <para><b>Reasonable for:</b> an app that shows web content the user should handle like a browser page.</para>
    /// <para><b>Safer:</b> a menu of the app's own, drawn by the page on <c>contextmenu</c>, which calls your commands.</para>
    /// </summary>
    public BrowserOptions AllowContextMenu()
    {
        ContextMenu = true;
        return this;
    }

    /// <summary>
    /// The browser's own file pickers: <c>&lt;input type="file"&gt;</c> opens the system's open dialog again, and the
    /// File System Access pickers (<c>showOpenFilePicker</c>, <c>showSaveFilePicker</c>,
    /// <c>showDirectoryPicker</c>) exist again.
    /// <para><b>Risk:</b> the page gets the content of any file the user picks, and with the File System Access pickers
    /// a handle it can write through, outside the app's file grants: the backend never sees the file, so it cannot
    /// check, limit or log what the page reads or writes.</para>
    /// <para><b>Reasonable for:</b> an existing web page with its own upload form, while it moves to the app's commands.</para>
    /// <para><b>Safer:</b> the app's file commands (<c>files.open()</c>), which show the native dialog from the backend
    /// and hand the page a grant, never a path.</para>
    /// </summary>
    public BrowserOptions AllowPagePicker()
    {
        PagePicker = true;
        return this;
    }

    /// <summary>
    /// The environment variables that change the browser before it starts. On Windows, the WebView2 ones:
    /// <c>WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS</c>, <c>WEBVIEW2_BROWSER_EXECUTABLE_FOLDER</c>,
    /// <c>WEBVIEW2_USER_DATA_FOLDER</c>, the release-channel variables and the script-debugger variables. On Linux,
    /// WebKitGTK's <c>WEBKIT_INSPECTOR_SERVER</c>, <c>WEBKIT_INSPECTOR_HTTP_SERVER</c> and
    /// <c>WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS</c>, and in a Release build every JavaScriptCore option
    /// (<c>JSC_*</c>; a Debug build keeps those for debugging). Without this call Spar removes them from its own process
    /// before the browser starts, logs each one it removed by name, and neither the browser nor its child processes see
    /// them. Everything else passes, the Linux GPU workarounds (<c>WEBKIT_DISABLE_DMABUF_RENDERER</c>,
    /// <c>WEBKIT_DISABLE_COMPOSITING_MODE</c>, <c>__NV_DISABLE_EXPLICIT_SYNC</c>) included. <c>JSC_useWasm</c> always
    /// follows the page security's <c>AllowWebAssembly()</c>, with or without this call. Policies an administrator sets
    /// in the registry stay in force either way.
    /// <para><b>Risk:</b> whoever can set the app's environment, such as a shortcut or a script that starts it, can open
    /// a remote debugging or inspector port that any local program can use to read and drive the page, start another
    /// browser build, move the profile to a folder they prepared, run the page's web processes without their sandbox,
    /// or change how JavaScriptCore runs the page's code.</para>
    /// <para><b>Reasonable for:</b> a support build that must be debugged on a user's machine, for a limited time.</para>
    /// <para><b>Safer:</b> a Debug build with its devtools, or <see cref="AllowDevTools"/>, neither of which opens a port.</para>
    /// </summary>
    public BrowserOptions AllowBrowserEnvironmentOverrides()
    {
        EnvironmentOverrides = true;
        return this;
    }

    /// <summary>
    /// Whether the browser draws with the GPU (plan section 3, "Software GPUs"). <b>Linux only for now: Windows
    /// ignores it</b>, and WebView2 keeps its own choice there.
    /// <para><see cref="Spar.HardwareAcceleration.Auto"/>, the default, turns acceleration off where the only GPU is a
    /// software renderer (llvmpipe, lavapipe, SwiftShader and the like, as in virtual machines, WSL and containers):
    /// there it made the first window about 0.35 s slower and the app and its web processes about 270 MB larger,
    /// for nothing.
    /// <see cref="Spar.HardwareAcceleration.Always"/> keeps WebKit's and GTK's own GPU drawing even then, and
    /// <see cref="Spar.HardwareAcceleration.Never"/> turns it off everywhere, for a graphics driver that breaks the
    /// page.</para>
    /// <para>It is not a security setting. Spar logs the choice and what it found at start-up. The user's own
    /// <c>GSK_RENDERER</c> and <c>WEBKIT_DISABLE_DMABUF_RENDERER</c> always win over it.</para>
    /// </summary>
    public BrowserOptions HardwareAcceleration(HardwareAcceleration acceleration)
    {
        if (!Enum.IsDefined(acceleration))
        {
            throw new ArgumentOutOfRangeException(nameof(acceleration), acceleration, "Not a HardwareAcceleration value.");
        }

        Acceleration = acceleration;
        return this;
    }

    /// <summary>The opt-ins in force, by their method names, for the start-up log (plan section 3c, level 2); empty for none.</summary>
    internal string Active() => string.Concat(
        DevTools ? " AllowDevTools()" : "",
        AcceleratorKeys ? " AllowAcceleratorKeys()" : "",
        PasswordSaving ? " AllowPasswordSaving()" : "",
        Downloads ? " AllowDownloads()" : "",
        HistoryNavigation ? " AllowHistoryNavigation()" : "",
        ContextMenu ? " AllowContextMenu()" : "",
        PagePicker ? " AllowPagePicker()" : "",
        EnvironmentOverrides ? " AllowBrowserEnvironmentOverrides()" : "").TrimStart();
}

/// <summary>
/// Whether the browser draws with the GPU, in <c>spar.Browser(b => b.HardwareAcceleration(…))</c>. Linux only for now:
/// Windows ignores it.
/// </summary>
public enum HardwareAcceleration
{
    /// <summary>
    /// The default: the GPU where it is a real one, and no acceleration where the only GPU is a software renderer.
    /// Spar decides before the window exists, from the GPU's render node and, when there is one, the renderer's name.
    /// </summary>
    Auto,

    /// <summary>WebKit's and GTK's own GPU drawing, even on a software renderer.</summary>
    Always,

    /// <summary>No GPU drawing, on any machine: for a graphics driver that breaks the page.</summary>
    Never,
}
